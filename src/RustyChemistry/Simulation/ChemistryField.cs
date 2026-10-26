using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using RustyChemistry.Content;

namespace RustyChemistry.Simulation;

/// <summary>
/// A sparse field of chemistry cells running one pack. The product loads the cells it cares about, sets
/// ambient, drift and modulators from its world, adds emitters, and calls <see cref="Step"/> once per fixed
/// step of its admitted time; it then reads this step's events and samples cells. The field never calls out.
/// <para>
/// Each step runs in phases: emitters, transfer (double-buffered), then per cell reactions, settling and
/// transforms (all local), then discharges and fronts. All spatial interaction goes through transfer,
/// discharge and fronts; rules read only their own cell, so no phase depends on the order cells are visited.
/// </para>
/// </summary>
public sealed partial class ChemistryField
{
    /// <summary>The edge length of a chunk: the field loads, wakes and sleeps cells in cubes this many across.</summary>
    public const int ChunkSize = FieldChunk.Size;

    /// <summary>Steps without change before an active chunk sleeps.</summary>
    private const int SleepAfterQuietSteps = 4;

    private const int CellCount = FieldChunk.CellCount;

    private static readonly Comparer<FieldChunk> ByKey = Comparer<FieldChunk>.Create((a, b) => a.Key.CompareTo(b.Key));

    private static readonly Cell[] FaceOffsets = [new(-1, 0, 0), new(1, 0, 0), new(0, -1, 0), new(0, 1, 0), new(0, 0, -1), new(0, 0, 1)];

    private readonly int quantityCount;
    private readonly float[] conductance;

    /// <summary>Each quantity's conductance by material: the transfer loop's lookup.</summary>
    private readonly float[][] conductanceOf;
    private readonly bool[] sky;
    private readonly float[] min;
    private readonly float[] max;
    private readonly float[] epsilon;
    private readonly float[] ambient;
    private readonly SettleRule[][] settlingFor;
    private readonly float[] modulators;
    private readonly int[][] reactionsFor;
    private readonly int[][] transformsFor;
    private readonly ushort defaultMaterial;

    private readonly Dictionary<ChunkKey, FieldChunk> chunkMap = [];
    private readonly List<FieldChunk> chunks = [];
    private readonly SortedDictionary<int, EmitterState> emitters = [];
    private readonly List<PendingDischarge> pendingDischarges = [];
    private readonly List<Front> fronts = [];
    private readonly List<MaterialChange> materialChanges = [];
    private readonly List<Discharge> discharges = [];
    private readonly List<FrontArrival> arrivals = [];
    private readonly List<FieldChunk> transferring = [];
    private readonly List<int> expired = [];
    private int nextEmitter;
    private int nextFront;
    private Vector3 drift;

    public ChemistryField(ChemistryPack pack, ChemistryFieldSettings settings)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(settings);
        if (!(settings.StepSeconds > 0f) || !(settings.CellSize > 0f))
        {
            throw new ArgumentException("the step and the cell size must be positive", nameof(settings));
        }

        if (pack.Materials.Length > ushort.MaxValue)
        {
            throw new ArgumentException("a field holds at most 65535 materials", nameof(pack));
        }

        Pack = pack;
        Settings = settings;
        defaultMaterial = (ushort)pack.Material(settings.DefaultMaterial);
        quantityCount = pack.Quantities.Length;

        ImmutableArray<QuantityDefinition> quantities = pack.Quantities;
        min = [.. quantities.Select(quantity => quantity.Min)];
        max = [.. quantities.Select(quantity => quantity.Max)];
        epsilon = [.. quantities.Select(quantity => quantity.Epsilon)];
        ambient = [.. quantities.Select(quantity => quantity.Ambient)];
        modulators = [.. pack.Modulators.Select(_ => 1f)];

        conductance = new float[pack.Materials.Length * quantityCount];
        sky = new bool[pack.Materials.Length];
        reactionsFor = new int[pack.Materials.Length][];
        transformsFor = new int[pack.Materials.Length][];
        settlingFor = new SettleRule[pack.Materials.Length][];
        for (int material = 0; material < pack.Materials.Length; material++)
        {
            MaterialDefinition definition = pack.Materials[material];
            definition.Conductance.CopyTo(conductance, material * quantityCount);
            sky[material] = definition.Sky;
            int current = material;
            reactionsFor[material] = [.. Enumerable.Range(0, pack.Reactions.Length)
                .Where(rule => pack.Reactions[rule].Materials.IsEmpty || pack.Reactions[rule].Materials.Contains(current))];
            transformsFor[material] = [.. Enumerable.Range(0, pack.Transforms.Length)
                .Where(rule => pack.Transforms[rule].From.Contains(current))];
            settlingFor[material] = [.. Enumerable.Range(0, quantityCount)
                .Select(quantity => definition.Rest[quantity] is RestDefinition rest
                    ? new SettleRule(quantity, Factor(rest.Settle), true, rest.To)
                    : new SettleRule(quantity, Factor(quantities[quantity].Settle), false, 0f))
                .Where(rule => rule.Factor > 0f)];
        }

        conductanceOf = [.. Enumerable.Range(0, quantityCount).Select(quantity =>
            Enumerable.Range(0, pack.Materials.Length).Select(material => conductance[(material * quantityCount) + quantity]).ToArray())];
    }

    public ChemistryPack Pack { get; }

    public ChemistryFieldSettings Settings { get; }

    /// <summary>Steps run so far; the next step's chance draws are keyed by this.</summary>
    public long StepIndex { get; private set; }

    public int ChunkCount => chunks.Count;

    public int ActiveChunkCount => chunks.Count(chunk => chunk.Active);

    public int ActiveFrontCount => fronts.Count;

    /// <summary>The wind, in cells per second, carrying every quantity whose transfer drifts.</summary>
    public Vector3 Drift
    {
        get => drift;
        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "drift must be finite");
            }

            drift = value;
            WakeAll();
        }
    }

    /// <summary>The identity changes of the last step, in cell order.</summary>
    public IReadOnlyList<MaterialChange> MaterialChanges => materialChanges;

    /// <summary>The discharges the last step resolved, in the order they were queued.</summary>
    public IReadOnlyList<Discharge> Discharges => discharges;

    /// <summary>The cells fronts reached in the last step, nearest first for each front.</summary>
    public IReadOnlyList<FrontArrival> FrontArrivals => arrivals;

    public Cell CellAt(Vector3 world)
    {
        Vector3 local = (world - Settings.Origin) / Settings.CellSize;
        return new((int)MathF.Floor(local.X), (int)MathF.Floor(local.Y), (int)MathF.Floor(local.Z));
    }

    public Vector3 WorldCenter(Cell cell) => Settings.Origin + (cell.Center * Settings.CellSize);

    /// <summary>
    /// Loads a cell as a material holding its initial quantities, creating its chunk (filled with the
    /// default material) if needed. Sky exposure is kept; call <see cref="MarkOpenColumns"/> or
    /// <see cref="SetOpen"/> after changing what lies above.
    /// </summary>
    public void Load(Cell cell, int material)
    {
        CheckMaterial(material);
        FieldChunk chunk = ChunkFor(ChunkKey.Of(cell));
        int index = FieldChunk.Index(cell);
        chunk.Materials[index] = (ushort)material;
        for (int quantity = 0; quantity < quantityCount; quantity++)
        {
            chunk.Values[(quantity * CellCount) + index] = InitialValue(material, quantity);
        }

        chunk.Wake();
        WakeAcross(chunk, index);
    }

    /// <summary>Loads every cell from <paramref name="from"/> to <paramref name="to"/>, inclusive.</summary>
    public void Fill(Cell from, Cell to, int material)
    {
        for (int z = Math.Min(from.Z, to.Z); z <= Math.Max(from.Z, to.Z); z++)
        {
            for (int y = Math.Min(from.Y, to.Y); y <= Math.Max(from.Y, to.Y); y++)
            {
                for (int x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++)
                {
                    Load(new Cell(x, y, z), material);
                }
            }
        }
    }

    /// <summary>
    /// Loads every cell of the chunk holding the cell at once, each as the material at its index (x + 16y +
    /// 256z from the chunk's first cell, as <see cref="ReadChunk"/>) holding its initial quantities. A chunk
    /// already loaded is replaced. Sky exposure is kept, as with <see cref="Load"/>.
    /// </summary>
    public void LoadChunk(Cell cell, ReadOnlySpan<ushort> materials)
    {
        if (materials.Length != CellCount)
        {
            throw new ArgumentException($"a chunk holds {CellCount} cells", nameof(materials));
        }

        foreach (ushort material in materials)
        {
            CheckMaterial(material);
        }

        FieldChunk chunk = ChunkFor(ChunkKey.Of(cell));
        materials.CopyTo(chunk.Materials);
        for (int index = 0; index < CellCount; index++)
        {
            for (int quantity = 0; quantity < quantityCount; quantity++)
            {
                chunk.Values[(quantity * CellCount) + index] = InitialValue(materials[index], quantity);
            }
        }

        chunk.Wake();
        WakeNeighbours(chunk.Key);
    }

    /// <summary>
    /// Unloads the chunk holding the cell, between steps: its cells then read as the default material at
    /// ambient, and its neighbours exchange with the ambient across the faces it leaves, as at any edge of
    /// the loaded region. Emitters, discharges and fronts reaching it pass it by. Returns whether it was loaded.
    /// </summary>
    public bool UnloadChunk(Cell cell)
    {
        if (!chunkMap.Remove(ChunkKey.Of(cell), out FieldChunk? chunk))
        {
            return false;
        }

        chunks.RemoveAt(chunks.BinarySearch(chunk, ByKey));
        WakeNeighbours(chunk.Key);
        return true;
    }

    public bool IsLoaded(Cell cell) => chunkMap.ContainsKey(ChunkKey.Of(cell));

    /// <summary>The cell's material; outside the loaded region, the default material.</summary>
    public int MaterialAt(Cell cell) => TryLocate(cell, out FieldChunk? chunk, out int index) ? chunk.Materials[index] : defaultMaterial;

    /// <summary>The cell's value of a quantity; outside the loaded region, the ambient.</summary>
    public float Quantity(Cell cell, int quantity)
    {
        CheckQuantity(quantity);
        return TryLocate(cell, out FieldChunk? chunk, out int index) ? chunk.Values[(quantity * CellCount) + index] : ambient[quantity];
    }

    /// <summary>Sets a loaded cell's value of a quantity, within its bounds.</summary>
    public void SetQuantity(Cell cell, int quantity, float value)
    {
        CheckQuantity(quantity);
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "a quantity's value must be finite");
        }

        FieldChunk chunk = Loaded(cell, out int index);
        chunk.Values[(quantity * CellCount) + index] = Math.Clamp(value, min[quantity], max[quantity]);
        chunk.Wake();
        WakeAcross(chunk, index);
    }

    public bool IsOpen(Cell cell) => TryLocate(cell, out FieldChunk? chunk, out int index) && chunk.Open[index];

    public void SetOpen(Cell cell, bool open)
    {
        FieldChunk chunk = Loaded(cell, out int index);
        chunk.Open[index] = open;
        chunk.Wake();
        WakeAcross(chunk, index);
    }

    /// <summary>
    /// Marks each loaded cell open when it and every loaded cell above it let the sky through. Space above
    /// the highest loaded chunk, and gaps between loaded chunks, pass on what lies above them.
    /// </summary>
    public void MarkOpenColumns()
    {
        foreach (IGrouping<(int X, int Z), FieldChunk> column in chunks.GroupBy(chunk => (chunk.Key.X, chunk.Key.Z)))
        {
            FieldChunk[] stack = [.. column.OrderByDescending(chunk => chunk.Key.Y)];
            for (int z = 0; z < FieldChunk.Size; z++)
            {
                for (int x = 0; x < FieldChunk.Size; x++)
                {
                    bool open = true;
                    foreach (FieldChunk chunk in stack)
                    {
                        for (int y = FieldChunk.Mask; y >= 0; y--)
                        {
                            int index = FieldChunk.Index(x, y, z);
                            open &= sky[chunk.Materials[index]];
                            chunk.Open[index] = open;
                        }
                    }
                }
            }

            foreach (FieldChunk chunk in stack)
            {
                chunk.Wake();
            }
        }
    }

    /// <summary>
    /// Re-marks sky exposure down one column of loaded cells, as <see cref="MarkOpenColumns"/> does for all of
    /// them: after an identity in it changed whether the sky passes. Only chunks whose exposure changed wake.
    /// </summary>
    public void MarkOpenColumn(int x, int z)
    {
        int localX = x & FieldChunk.Mask;
        int localZ = z & FieldChunk.Mask;
        bool open = true;
        foreach (FieldChunk chunk in chunks.Where(chunk => chunk.Key.X == x >> FieldChunk.Shift && chunk.Key.Z == z >> FieldChunk.Shift)
            .OrderByDescending(chunk => chunk.Key.Y))
        {
            bool changed = false;
            for (int y = FieldChunk.Mask; y >= 0; y--)
            {
                int index = FieldChunk.Index(localX, y, localZ);
                open &= sky[chunk.Materials[index]];
                changed |= chunk.Open[index] != open;
                chunk.Open[index] = open;
            }

            if (changed)
            {
                chunk.Wake();
            }
        }
    }

    /// <summary>The first cell of every loaded chunk, in chunk order.</summary>
    public IEnumerable<Cell> LoadedChunks() => chunks.Select(chunk => chunk.CellAt(0));

    /// <summary>Whether the chunk holding the cell is loaded and still changing; a sleeping chunk's cells hold still.</summary>
    public bool IsActive(Cell cell) => chunkMap.TryGetValue(ChunkKey.Of(cell), out FieldChunk? chunk) && chunk.Active;

    /// <summary>
    /// Copies the values of a quantity in the loaded chunk holding the cell, indexed x + 16y + 256z from the
    /// chunk's first cell: a bulk read for presentation.
    /// </summary>
    public void ReadChunk(Cell cell, int quantity, Span<float> values)
    {
        CheckQuantity(quantity);
        FieldChunk chunk = Loaded(cell, out _);
        chunk.Values.AsSpan(quantity * CellCount, CellCount).CopyTo(values);
    }

    /// <summary>Copies the materials of the loaded chunk holding the cell, indexed as <see cref="ReadChunk"/>.</summary>
    public void ReadChunkMaterials(Cell cell, Span<ushort> materials) => Loaded(cell, out _).Materials.CopyTo(materials);

    public float Ambient(int quantity)
    {
        CheckQuantity(quantity);
        return ambient[quantity];
    }

    /// <summary>Sets what a quantity settles toward and what lies beyond the loaded region.</summary>
    public void SetAmbient(int quantity, float value)
    {
        CheckQuantity(quantity);
        if (!(value >= min[quantity] && value <= max[quantity]))
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"ambient must be within {min[quantity]}..{max[quantity]}");
        }

        ambient[quantity] = value;
        WakeAll();
    }

    public float Modulator(string name) => modulators[Pack.Modulator(name)];

    /// <summary>Sets a named multiplier on the rates of the rules that use it (1 is neutral).</summary>
    public void SetModulator(string name, float value)
    {
        if (!(value >= 0f) || !float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "a modulator must be finite and not negative");
        }

        modulators[Pack.Modulator(name)] = value;
        WakeAll();
    }

    public EmitterId AddEmitter(EmitterSpec emitter)
    {
        CheckEmitter(emitter);
        EmitterId id = new(nextEmitter++);
        emitters.Add(id.Value, new EmitterState(emitter));
        return id;
    }

    /// <summary>Moves or retunes an emitter in place; its lifetime starts again from the new spec.</summary>
    public void ReplaceEmitter(EmitterId id, EmitterSpec emitter)
    {
        CheckEmitter(emitter);
        if (!emitters.ContainsKey(id.Value))
        {
            throw new ArgumentException($"no emitter {id.Value}", nameof(id));
        }

        emitters[id.Value] = new EmitterState(emitter);
    }

    public bool RemoveEmitter(EmitterId id) => emitters.Remove(id.Value);

    public bool HasEmitter(EmitterId id) => emitters.ContainsKey(id.Value);

    /// <summary>The sum of a quantity over every loaded cell.</summary>
    public double Total(int quantity)
    {
        CheckQuantity(quantity);
        double total = 0;
        foreach (FieldChunk chunk in chunks)
        {
            foreach (float value in chunk.Values.AsSpan(quantity * CellCount, CellCount))
            {
                total += value;
            }
        }

        return total;
    }

    /// <summary>Advances the field by one fixed step and replaces the step's events.</summary>
    public void Step()
    {
        materialChanges.Clear();
        discharges.Clear();
        arrivals.Clear();
        float seconds = Settings.StepSeconds;

        ApplyEmitters(seconds);
        Transfer(seconds);
        foreach (FieldChunk chunk in chunks)
        {
            if (chunk.Active)
            {
                Local(chunk, seconds);
            }
        }

        ResolveDischarges();
        AdvanceFronts(seconds);
        Rest();
        StepIndex++;
    }

    private void ApplyEmitters(float seconds)
    {
        expired.Clear();
        foreach ((int id, EmitterState emitter) in emitters)
        {
            EmitterSpec spec = emitter.Spec;
            foreach ((FieldChunk chunk, int index) in CellsIn(spec.Shape))
            {
                if (spec.Rate != 0f)
                {
                    Change(chunk, spec.Quantity, index, spec.Rate * seconds);
                }

                chunk.Wake();
            }

            emitter.Remaining -= seconds;
            if (emitter.Remaining <= 0f)
            {
                expired.Add(id);
            }
        }

        foreach (int id in expired)
        {
            emitters.Remove(id);
        }
    }

    private void Local(FieldChunk chunk, float seconds)
    {
        for (int index = 0; index < CellCount; index++)
        {
            int material = chunk.Materials[index];
            foreach (int rule in reactionsFor[material])
            {
                React(chunk, index, Pack.Reactions[rule], seconds);
            }

            foreach (SettleRule settle in settlingFor[material])
            {
                float value = chunk.Values[(settle.Quantity * CellCount) + index];
                float toward = settle.OwnRest ? settle.Rest : ambient[settle.Quantity];
                Change(chunk, settle.Quantity, index, (toward - value) * settle.Factor);
            }

            if (transformsFor[material].Length > 0)
            {
                TryTransform(chunk, index, material, seconds);
            }
        }
    }

    private void React(FieldChunk chunk, int index, ReactionRule rule, float seconds)
    {
        // The rate first: a rule switched off by its modulator, or with none of its per quantity, is skipped
        // without testing its conditions.
        float extent = rule.Rate * Modulation(rule.Modulator) * seconds;
        if (rule.Per is int per)
        {
            extent *= Math.Max(0f, Value(chunk, per, index));
        }

        if (extent <= 0f || !Holds(rule.Conditions, chunk, index))
        {
            return;
        }

        foreach (QuantityAmount consumed in rule.Consumes)
        {
            float available = Math.Max(0f, Value(chunk, consumed.Quantity, index) - min[consumed.Quantity]);
            extent = Math.Min(extent, available / consumed.Amount);
        }

        if (extent <= 0f)
        {
            return;
        }

        foreach (QuantityAmount consumed in rule.Consumes)
        {
            Change(chunk, consumed.Quantity, index, -consumed.Amount * extent);
        }

        foreach (QuantityAmount produced in rule.Produces)
        {
            Change(chunk, produced.Quantity, index, produced.Amount * extent);
        }
    }

    private void TryTransform(FieldChunk chunk, int index, int material, float seconds)
    {
        foreach (int rule in transformsFor[material])
        {
            TransformRule transform = Pack.Transforms[rule];
            if (!Holds(transform.Conditions, chunk, index))
            {
                continue;
            }

            if (transform.Chance is float chance)
            {
                float rate = chance * Modulation(transform.Modulator);
                if (transform.ResistedBy is int resisting)
                {
                    rate /= 1f + Math.Max(0f, Value(chunk, resisting, index));
                }

                double probability = 1.0 - Math.Exp(-rate * (double)seconds);
                if (probability <= 0.0)
                {
                    continue;
                }

                // A transform that may still fire keeps its chunk awake.
                chunk.Touched = true;
                if (KeyedDraw.Unit(Settings.Seed, StepIndex, chunk.CellAt(index), rule) >= probability)
                {
                    continue;
                }
            }

            chunk.Materials[index] = (ushort)transform.To;
            foreach (QuantityAmount assigned in transform.Set)
            {
                chunk.Values[(assigned.Quantity * CellCount) + index] = assigned.Amount;
            }

            chunk.Touched = true;
            materialChanges.Add(new MaterialChange(chunk.CellAt(index), material, transform.To, rule));
            return;
        }
    }

    private static bool Holds(ImmutableArray<Condition> conditions, FieldChunk chunk, int index)
    {
        foreach (Condition condition in conditions)
        {
            bool holds = condition.Kind switch
            {
                ConditionKind.AtLeast => Value(chunk, condition.Quantity, index) >= condition.Threshold,
                ConditionKind.AtMost => Value(chunk, condition.Quantity, index) <= condition.Threshold,
                ConditionKind.Open => chunk.Open[index],
                _ => !chunk.Open[index],
            };
            if (!holds)
            {
                return false;
            }
        }

        return true;
    }

    private void Rest()
    {
        foreach (FieldChunk chunk in chunks)
        {
            if (chunk.Swapped)
            {
                if (Changed(chunk))
                {
                    chunk.Wake();
                }

                chunk.Swapped = false;
            }

            if (chunk.Active)
            {
                if (chunk.Touched)
                {
                    chunk.QuietSteps = 0;
                }
                else if (++chunk.QuietSteps >= SleepAfterQuietSteps)
                {
                    chunk.Active = false;
                    KeepSettled(chunk);
                }
            }

            chunk.Touched = false;
        }
    }

    /// <summary>
    /// Keeps, as a chunk falls asleep, the values its neighbours hold next to it: its cells settled against them,
    /// so transfer from an active neighbour later hands it only what differs from that (see Across).
    /// </summary>
    private void KeepSettled(FieldChunk chunk)
    {
        const int FaceCells = FieldChunk.Size * FieldChunk.Size;
        float[] settled = chunk.Settled ??= new float[6 * quantityCount * FaceCells];
        chunk.IsSettled = true;
        ChunkKey key = chunk.Key;
        for (int face = 0; face < 6; face++)
        {
            int axis = face / 2;
            bool positive = face % 2 == 1;
            int step = positive ? 1 : -1;
            ChunkKey neighbourKey = axis switch
            {
                0 => key with { X = key.X + step },
                1 => key with { Y = key.Y + step },
                _ => key with { Z = key.Z + step },
            };
            chunk.SettledFaces[face] = chunkMap.TryGetValue(neighbourKey, out FieldChunk? neighbour);
            if (neighbour is null)
            {
                continue;
            }

            // The neighbour's layer touching this chunk: its lowest along the axis on the positive side.
            int layer = positive ? 0 : FieldChunk.Mask;
            for (int a = 0; a < FieldChunk.Size; a++)
            {
                for (int b = 0; b < FieldChunk.Size; b++)
                {
                    int index = axis switch
                    {
                        0 => FieldChunk.Index(layer, a, b),
                        1 => FieldChunk.Index(a, layer, b),
                        _ => FieldChunk.Index(a, b, layer),
                    };
                    int cell = FieldChunk.FaceCell(index, axis);
                    for (int quantity = 0; quantity < quantityCount; quantity++)
                    {
                        settled[(((face * quantityCount) + quantity) * FaceCells) + cell] = neighbour.Values[(quantity * CellCount) + index];
                    }
                }
            }
        }
    }

    /// <summary>
    /// Adds to a cell's quantity within its bounds; a change above epsilon keeps the chunk awake, unless transfer
    /// swapped its buffers this step, when the step's end compares its values instead (<see cref="Changed"/>).
    /// </summary>
    private void Change(FieldChunk chunk, int quantity, int index, float delta)
    {
        int at = (quantity * CellCount) + index;
        float before = chunk.Values[at];
        float after = Math.Clamp(before + delta, min[quantity], max[quantity]);
        chunk.Values[at] = after;
        if (!chunk.Swapped && Math.Abs(after - before) > epsilon[quantity])
        {
            chunk.Touched = true;
        }
    }

    private static float Value(FieldChunk chunk, int quantity, int index) => chunk.Values[(quantity * CellCount) + index];

    private float Conductance(FieldChunk chunk, int index, int quantity) => conductance[(chunk.Materials[index] * quantityCount) + quantity];

    private float Modulation(int? modulator) => modulator is int index ? modulators[index] : 1f;

    private float InitialValue(int material, int quantity) => Pack.Materials[material].Initial[quantity] ?? ambient[quantity];

    private IEnumerable<(FieldChunk Chunk, int Index)> CellsIn(EmitterShape shape)
    {
        int x0 = (int)MathF.Floor(shape.Min.X), x1 = (int)MathF.Ceiling(shape.Max.X) - 1;
        int y0 = (int)MathF.Floor(shape.Min.Y), y1 = (int)MathF.Ceiling(shape.Max.Y) - 1;
        int z0 = (int)MathF.Floor(shape.Min.Z), z1 = (int)MathF.Ceiling(shape.Max.Z) - 1;
        for (int z = z0; z <= z1; z++)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Cell cell = new(x, y, z);
                    if (shape.Contains(cell.Center) && TryLocate(cell, out FieldChunk? chunk, out int index))
                    {
                        yield return (chunk, index);
                    }
                }
            }
        }
    }

    private FieldChunk ChunkFor(ChunkKey key)
    {
        if (chunkMap.TryGetValue(key, out FieldChunk? found))
        {
            return found;
        }

        FieldChunk chunk = new(key, quantityCount);
        Array.Fill(chunk.Materials, defaultMaterial);
        for (int quantity = 0; quantity < quantityCount; quantity++)
        {
            Array.Fill(chunk.Values, InitialValue(defaultMaterial, quantity), quantity * CellCount, CellCount);
        }

        chunkMap.Add(key, chunk);
        int at = chunks.BinarySearch(chunk, ByKey);
        chunks.Insert(~at, chunk);
        WakeNeighbours(key);
        return chunk;
    }

    private bool TryLocate(Cell cell, [NotNullWhen(true)] out FieldChunk? chunk, out int index)
    {
        index = FieldChunk.Index(cell);
        return chunkMap.TryGetValue(ChunkKey.Of(cell), out chunk);
    }

    private FieldChunk Loaded(Cell cell, out int index) =>
        TryLocate(cell, out FieldChunk? chunk, out index) ? chunk : throw new ArgumentException($"cell {cell} is not loaded", nameof(cell));

    /// <summary>
    /// Wakes the loaded chunks across the faces a border cell touches: an edit there changes the exchange a
    /// sleeping neighbour settled against (a source sealed off, a face closed or opened), which it would
    /// otherwise never see, since transfer skips a closed face and a sleeping chunk takes only what differs.
    /// </summary>
    private void WakeAcross(FieldChunk chunk, int index)
    {
        ChunkKey key = chunk.Key;
        int x = index & FieldChunk.Mask, y = (index >> FieldChunk.Shift) & FieldChunk.Mask, z = index >> (2 * FieldChunk.Shift);
        WakeBorder(x, key with { X = key.X - 1 }, key with { X = key.X + 1 });
        WakeBorder(y, key with { Y = key.Y - 1 }, key with { Y = key.Y + 1 });
        WakeBorder(z, key with { Z = key.Z - 1 }, key with { Z = key.Z + 1 });

        void WakeBorder(int along, ChunkKey lower, ChunkKey upper)
        {
            if (along == 0)
            {
                WakeLoaded(lower);
            }
            else if (along == FieldChunk.Mask)
            {
                WakeLoaded(upper);
            }
        }
    }

    /// <summary>Wakes the six chunks around one that was loaded, replaced or unloaded whole.</summary>
    private void WakeNeighbours(ChunkKey key)
    {
        WakeLoaded(key with { X = key.X - 1 });
        WakeLoaded(key with { X = key.X + 1 });
        WakeLoaded(key with { Y = key.Y - 1 });
        WakeLoaded(key with { Y = key.Y + 1 });
        WakeLoaded(key with { Z = key.Z - 1 });
        WakeLoaded(key with { Z = key.Z + 1 });
    }

    private void WakeLoaded(ChunkKey key)
    {
        if (chunkMap.TryGetValue(key, out FieldChunk? neighbour))
        {
            neighbour.Wake();
        }
    }

    private void WakeAll()
    {
        foreach (FieldChunk chunk in chunks)
        {
            chunk.Wake();
        }
    }

    private void CheckQuantity(int quantity) => ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)quantity, (uint)quantityCount, nameof(quantity));

    private void CheckMaterial(int material) => ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)material, (uint)Pack.Materials.Length, nameof(material));

    private void CheckEmitter(EmitterSpec emitter)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        CheckQuantity(emitter.Quantity);
        if (!float.IsFinite(emitter.Rate) || !float.IsFinite(emitter.Gather) || !(emitter.Lifetime > 0f) || !(emitter.Shape.Radius >= 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(emitter), "an emitter needs a finite rate and gather, a positive lifetime and a radius not below zero");
        }
    }

    private sealed class EmitterState(EmitterSpec spec)
    {
        internal EmitterSpec Spec { get; } = spec;

        internal float Remaining { get; set; } = spec.Lifetime;
    }

    /// <summary>The share of the way to its target a settling value moves in one step, at a rate per second.</summary>
    private float Factor(float settle) => 1f - MathF.Exp(-settle * Settings.StepSeconds);

    /// <summary>How a material's cells settle one quantity: toward the ambient, or toward the material's own rest.</summary>
    private readonly record struct SettleRule(int Quantity, float Factor, bool OwnRest, float Rest);
}
