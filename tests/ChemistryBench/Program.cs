using System.Diagnostics;
using System.Numerics;
using RustyChemistry.Content;
using RustyChemistry.Simulation;

// The kernel's step cost on the window a product keeps around a player: 3 x 3 chunk columns, 3 chunks tall (soil,
// a surface of cover, air above), 27 chunks, freshly loaded. Each scenario prints the mean step over its first
// 30 s (the load settling, or a fire burning) and, after five minutes, the mean and worst over 30 s more and the
// chunks awake then, with a hash of the field's state, so an optimisation can be checked to leave results
// unchanged. meadow.json is a snapshot of craftsurvive's pack; --pack runs another with the same material names.
//
//   dotnet run -c Release --project tests/ChemistryBench [-- --pack <path>] [--steps <n per 30 s phase>]

// The later phase starts after this many phases: five minutes at the default 30 s.
const int LaterPhase = 10;
string packPath = Path.Combine(AppContext.BaseDirectory, "meadow.json");
int steps = 300;
for (int index = 0; index + 1 < args.Length; index += 2)
{
    switch (args[index])
    {
        case "--pack": packPath = args[index + 1]; break;
        case "--steps": steps = int.Parse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture); break;
        default: throw new ArgumentException($"unknown option {args[index]}");
    }
}

ChemistryPack pack = ChemistryPack.Read(File.ReadAllText(packPath));
int heat = pack.Quantity("heat");
Vector3 wind = new(0.6f, 0f, 0.2f);
Cell torch = new(24, 16, 24);

Console.WriteLine($"{"scenario",-22} {"first ms",9} {"later ms",9} {"worst",7} {"awake",7}  hash");
foreach (string cover in new[] { "grass", "dry-grass" })
{
    Scenario($"{cover} still", Window(cover), lit: false);
    Scenario($"{cover} windy", Window(cover, wind), lit: false);
    Scenario($"{cover} fire still", Window(cover), lit: true);
    Scenario($"{cover} fire windy", Window(cover, wind), lit: true);
}

ChemistryField Window(string cover, Vector3 drift = default)
{
    ChemistryField field = new(pack, new ChemistryFieldSettings("air", 0.1f, Seed: 10380));
    field.Fill(new(0, 0, 0), new(47, 15, 47), pack.Material("soil"));
    field.Fill(new(0, 16, 0), new(47, 16, 47), pack.Material(cover));
    field.Fill(new(0, 17, 0), new(47, 47, 47), pack.Material("air"));
    field.MarkOpenColumns();
    field.SetAmbient(heat, 22f);
    foreach (string modulator in pack.Modulators)
    {
        field.SetModulator(modulator, 0f);
    }

    field.Drift = drift;
    return field;
}

// The first phase, then on to five minutes, then a measured later phase: a field left alone should be asleep by then.
void Scenario(string label, ChemistryField field, bool lit)
{
    if (lit)
    {
        field.AddEmitter(new EmitterSpec(EmitterShape.Sphere(torch.Center, 1.2f), heat, 4000f, Lifetime: 0.6f));
    }

    (double first, _, _) = Run(field, steps);
    Run(field, (LaterPhase - 1) * steps);
    (double mean, double worst, double awake) = Run(field, steps);
    Console.WriteLine($"{label,-22} {first,9:0.00} {mean,9:0.00} {worst,7:0.00} {awake,4:0.0}/{field.ChunkCount}  {Hash(field):x16}");
}

static (double Mean, double Worst, double Awake) Run(ChemistryField field, int steps)
{
    double total = 0, worst = 0, awake = 0;
    for (int step = 0; step < steps; step++)
    {
        long began = Stopwatch.GetTimestamp();
        field.Step();
        double milliseconds = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
        total += milliseconds;
        worst = Math.Max(worst, milliseconds);
        awake += field.ActiveChunkCount;
    }

    return (total / steps, worst, awake / steps);
}

// FNV-1a over every loaded cell's material and quantity bits, chunk by chunk.
static ulong Hash(ChemistryField field)
{
    const int Cells = ChemistryField.ChunkSize * ChemistryField.ChunkSize * ChemistryField.ChunkSize;
    ulong hash = 14695981039346656037UL;
    ushort[] materials = new ushort[Cells];
    float[] values = new float[Cells];
    foreach (Cell chunk in field.LoadedChunks())
    {
        field.ReadChunkMaterials(chunk, materials);
        foreach (ushort material in materials)
        {
            hash = (hash ^ material) * 1099511628211UL;
        }

        for (int quantity = 0; quantity < field.Pack.Quantities.Length; quantity++)
        {
            field.ReadChunk(chunk, quantity, values);
            foreach (float value in values)
            {
                hash = (hash ^ BitConverter.SingleToUInt32Bits(value)) * 1099511628211UL;
            }
        }
    }

    return hash;
}
