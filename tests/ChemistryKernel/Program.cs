using System.Diagnostics;
using System.Numerics;
using RustyChemistry.Checks;
using RustyChemistry.Content;
using RustyChemistry.Simulation;

// The kernel's operators, checked without a host: each section builds a small field from an authored
// pack and checks one semantic claim the design makes (docs/chemistry.md).

ChemistryPack fire = ChemistryPack.Read(Packs.Fire);
ChemistryPack diffusion = ChemistryPack.Read(Packs.Diffusion);
ChemistryPack impulse = ChemistryPack.Read(Packs.Impulse);

int heat = fire.Quantity("heat"), moisture = fire.Quantity("moisture"), smoke = fire.Quantity("smoke");
int fuel = fire.Quantity("fuel"), bind = fire.Quantity("bind");
int air = fire.Material("air"), stone = fire.Material("stone"), grass = fire.Material("grass");
int burning = fire.Material("burning-grass"), ash = fire.Material("ash");
int ignite = fire.Transforms.IndexOf(fire.Transforms.Single(rule => rule.Id == "ignite"));
int douse = fire.Transforms.IndexOf(fire.Transforms.Single(rule => rule.Id == "douse"));

// --- transfer: conservation in a sealed room, loss through an open edge ---------------------------
{
    int warmth = diffusion.Quantity("heat");
    ChemistryField room = new(diffusion, new("air"));
    room.Fill(new(0, 0, 0), new(9, 9, 9), diffusion.Material("wall"));
    room.Fill(new(1, 1, 1), new(8, 8, 8), diffusion.Material("air"));
    room.SetQuantity(new(4, 4, 4), warmth, 1000f);
    Run(room, 300);
    Check.Near(room.Total(warmth), 1000, 0.5, "heat in a sealed room must be conserved");
    Check.That(room.Quantity(new(8, 8, 8), warmth) > 1f, "heat must reach the far corner of the room");
    Check.That(room.Quantity(new(0, 4, 4), warmth) == 0f, "heat must not enter a wall that does not conduct it");

    // Walled in on every side but x = -1, which lies outside the loaded region.
    ChemistryField open = new(diffusion, new("wall"));
    open.Fill(new(0, 1, 1), new(4, 1, 1), diffusion.Material("air"));
    open.SetQuantity(new(2, 1, 1), warmth, 1000f);
    Run(open, 200);
    Check.That(open.Total(warmth) < 500, "heat must leave through the open edge of the loaded region");
}

// --- confine: a held region keeps its heat, an unheld one lets it spread -------------------------
{
    int warmth = diffusion.Quantity("heat"), hold = diffusion.Quantity("hold");
    ChemistryField held = new(diffusion, new("wall"));
    held.Fill(new(1, 1, 1), new(9, 1, 1), diffusion.Material("air"));
    for (int x = 4; x <= 6; x++)
    {
        held.SetQuantity(new(x, 1, 1), hold, 1f);
    }

    held.SetQuantity(new(5, 1, 1), warmth, 900f);
    Run(held, 200);
    Check.That(held.Quantity(new(3, 1, 1), warmth) < 1e-3f && held.Quantity(new(7, 1, 1), warmth) < 1e-3f,
        "heat must not cross the edge of a held region");
    Check.Near(held.Quantity(new(4, 1, 1), warmth), 300, 1, "heat must spread freely inside the held region");

    ChemistryField free = new(diffusion, new("wall"));
    free.Fill(new(1, 1, 1), new(9, 1, 1), diffusion.Material("air"));
    free.SetQuantity(new(5, 1, 1), warmth, 900f);
    Run(free, 20);
    Check.That(free.Quantity(new(2, 1, 1), warmth) > 10f, "without a hold the same heat must spread along the row");
}

// --- transform: fire spreads through grass, stops at stone, burns out to ash ----------------------
{
    ChemistryField field = GrassRowWithGap(seed: 7);
    List<(long Step, Cell Cell, int To)> changes = Burn(field, 900);
    Check.That(Enumerable.Range(0, 10).All(x => Column(field, x).All(cell => field.MaterialAt(cell) == ash)),
        "every grass cell before the gap must have burnt to ash");
    Check.That(Enumerable.Range(13, 7).All(x => Column(field, x).All(cell => field.MaterialAt(cell) == grass)),
        "grass beyond a stone gap must not catch");
    Check.That(changes.Any(change => change.Cell.X == 9 && change.To == burning), "the fire must travel the whole strip");
    Check.That(field.Quantity(new(5, 1, 1), fuel) == 0f, "burnt grass must have used its fuel");

    // --- determinism: chance draws are keyed, so a seed replays exactly ---
    List<(long Step, Cell Cell, int To)> replay = Burn(GrassRowWithGap(seed: 7), 900);
    List<(long Step, Cell Cell, int To)> other = Burn(GrassRowWithGap(seed: 8), 900);
    Check.That(changes.SequenceEqual(replay), "the same seed must replay the same fire");
    Check.That(!changes.SequenceEqual(other), "another seed must burn differently");
}

// --- hysteresis: a doused cell does not reignite while it is wet, though still hot ----------------
{
    ChemistryField field = new(fire, new("air"));
    Cell cell = new(0, 0, 0);
    field.Load(cell, burning);
    field.SetQuantity(cell, fuel, 6f);
    field.SetQuantity(cell, heat, 600f);
    field.AddEmitter(new EmitterSpec(EmitterShape.Cells(cell, cell), moisture, Rate: 2f, Lifetime: 0.5f));
    float heatWhenDoused = float.NaN;
    bool reignited = false;
    for (int step = 0; step < 60; step++)
    {
        field.Step();
        if (field.MaterialChanges.Any(change => change.Transform == douse))
        {
            heatWhenDoused = field.Quantity(cell, heat);
        }

        reignited |= field.MaterialChanges.Any(change => change.Transform == ignite);
    }

    Check.That(heatWhenDoused >= 250f, "dousing must happen while the cell is still above the ignition heat");
    Check.That(!reignited && field.MaterialAt(cell) == grass, "wet grass must not reignite");
}

// --- bind: a bound patch resists ignition at the same heat -------------------------------------
{
    ChemistryField field = new(fire, new("air", Seed: 3));
    field.Fill(new(0, 0, 0), new(9, 0, 4), grass);
    field.Fill(new(64, 0, 0), new(73, 0, 4), grass);
    foreach (Cell cell in Box(new(0, 0, 0), new(9, 0, 4)).Concat(Box(new(64, 0, 0), new(73, 0, 4))))
    {
        field.SetQuantity(cell, heat, 1500f);
    }

    foreach (Cell cell in Box(new(64, 0, 0), new(73, 0, 4)))
    {
        field.SetQuantity(cell, bind, 9f);
    }

    int free = 0, bound = 0;
    for (int step = 0; step < 5; step++)
    {
        field.Step();
        free += field.MaterialChanges.Count(change => change.Transform == ignite && change.Cell.X < 32);
        bound += field.MaterialChanges.Count(change => change.Transform == ignite && change.Cell.X >= 32);
    }

    Check.That(free >= 20 && free > 3 * bound, $"binding must resist ignition (free {free}, bound {bound})");
}

// --- modulator: rain damping fire scales combustion, it does not switch it ---------------------
{
    float FuelLeft(float modulation)
    {
        ChemistryField field = new(fire, new("air"));
        Cell cell = new(0, 0, 0);
        field.Load(cell, burning);
        field.SetQuantity(cell, fuel, 6f);
        field.SetModulator("fire", modulation);
        Run(field, 30);
        return field.Quantity(cell, fuel);
    }

    Check.Near(FuelLeft(1f), 3f, 0.01, "full fire must burn a unit of fuel a second");
    Check.Near(FuelLeft(0.25f), 5.25f, 0.01, "quartered fire must burn a quarter as fast");
}

// --- smoke: trapped under a roof, vented in the open, carried by the wind ----------------------
{
    ChemistryField field = new(fire, new("air"));
    field.Fill(new(0, 0, 0), new(23, 0, 23), stone);
    field.Fill(new(0, 1, 0), new(23, 8, 23), air);
    field.Fill(new(2, 1, 2), new(8, 5, 8), stone);
    field.Fill(new(3, 1, 3), new(7, 4, 7), air);
    field.MarkOpenColumns();
    Check.That(!field.IsOpen(new(5, 2, 5)) && field.IsOpen(new(16, 2, 16)), "a roofed cell must be closed and an outdoor one open");

    field.AddEmitter(new EmitterSpec(EmitterShape.Sphere(new Cell(5, 2, 5).Center, 1f), smoke, Rate: 1f, Lifetime: 5f));
    field.AddEmitter(new EmitterSpec(EmitterShape.Sphere(new Cell(16, 2, 16).Center, 1f), smoke, Rate: 1f, Lifetime: 5f));
    Run(field, 200);
    float inside = Box(new(3, 1, 3), new(7, 4, 7)).Sum(cell => field.Quantity(cell, smoke));
    float outside = Box(new(11, 1, 11), new(21, 8, 21)).Sum(cell => field.Quantity(cell, smoke));
    Check.Near(inside, 35, 1, "smoke must stay in a closed hut");
    Check.That(outside < 0.5f, $"smoke must clear in the open (left {outside})");

    ChemistryField windy = new(fire, new("air"));
    windy.Fill(new(0, 0, 0), new(31, 0, 31), air);
    windy.Drift = new Vector3(0.6f, 0f, 0f);
    windy.AddEmitter(new EmitterSpec(EmitterShape.Sphere(new Cell(16, 0, 16).Center, 1f), smoke, Rate: 1f, Lifetime: 1f));
    Run(windy, 40);
    float downwind = Box(new(17, 0, 0), new(31, 15, 31)).Sum(cell => windy.Quantity(cell, smoke));
    float upwind = Box(new(0, 0, 0), new(15, 15, 31)).Sum(cell => windy.Quantity(cell, smoke));
    Check.That(downwind > 2 * upwind, $"wind must carry smoke downwind (down {downwind}, up {upwind})");
}

// --- gather: an inward emitter concentrates without creating ----------------------------------
{
    ChemistryField field = new(fire, new("stone"));
    field.Fill(new(0, 1, 0), new(14, 1, 14), air);
    foreach (Cell cell in Box(new(0, 1, 0), new(14, 1, 14)))
    {
        field.SetQuantity(cell, smoke, 1f);
    }

    double before = field.Total(smoke);
    field.AddEmitter(new EmitterSpec(EmitterShape.Sphere(new Cell(7, 1, 7).Center, 5f), smoke, Rate: 0f, Gather: 2f));
    Run(field, 30);
    Check.That(field.Quantity(new(7, 1, 7), smoke) > 1.5f, "gathering must concentrate smoke at the centre");
    Check.That(field.Total(smoke) <= before + 1e-3, "gathering must not create smoke");
}

// --- discharge: a bolt charges the puddle it lands in, not the one across dry ground -----------
{
    int charge = impulse.Quantity("charge");
    ChemistryField field = new(impulse, new("air"));
    field.Fill(new(0, 0, 0), new(4, 0, 0), impulse.Material("water"));
    field.Load(new(5, 0, 0), impulse.Material("dirt"));
    field.Fill(new(6, 0, 0), new(8, 0, 0), impulse.Material("water"));
    field.QueueDischarge(new(2, 0, 0), charge, 50f, minConductance: 0.5f, maxCells: 64);
    field.Step();
    Discharge bolt = field.Discharges.Single();
    Check.That(bolt.Cells.Count == 5 && bolt.Cells[0] == new Cell(2, 0, 0), "the discharge must reach exactly its puddle, origin first");
    Check.That(field.Quantity(new(0, 0, 0), charge) > 40f && field.Quantity(new(6, 0, 0), charge) == 0f,
        "the far puddle must stay uncharged");

    field.QueueDischarge(new(5, 0, 0), charge, 50f, minConductance: 0.5f, maxCells: 64);
    field.Step();
    Check.That(field.Discharges.Single().Cells.SequenceEqual([new Cell(5, 0, 0)]), "a bolt into dirt must stay where it lands");
}

// --- propagate: a front bends around a wall and slows through brush ----------------------------
{
    int blast = impulse.Quantity("blast");
    Dictionary<Cell, FrontArrival> Blast(bool wall, bool brush)
    {
        ChemistryField field = new(impulse, new("stone"));
        field.Fill(new(0, 0, 0), new(10, 0, 10), impulse.Material("air"));
        if (wall)
        {
            field.Fill(new(5, 0, 0), new(5, 0, 8), impulse.Material("stone"));
        }

        if (brush)
        {
            field.Load(new(3, 0, 4), impulse.Material("brush"));
        }

        field.StartFront(new(2, 0, 4), blast, amplitude: 50f, speed: 10f, attenuation: 0.05f, maxDistance: 40f);
        Dictionary<Cell, FrontArrival> reached = [];
        for (int step = 0; step < 100 && (step == 0 || field.ActiveFrontCount > 0); step++)
        {
            field.Step();
            foreach (FrontArrival arrival in field.FrontArrivals)
            {
                reached.Add(arrival.Cell, arrival);
            }
        }

        return reached;
    }

    Dictionary<Cell, FrontArrival> clear = Blast(wall: false, brush: false);
    Dictionary<Cell, FrontArrival> walled = Blast(wall: true, brush: false);
    Check.That(clear[new(8, 0, 4)].Distance == 6f, "in the open a front must take the direct path");
    Check.That(walled[new(8, 0, 4)].Distance == 16f, "behind a wall a front must go around through the gap");
    Check.That(!walled.ContainsKey(new(5, 0, 4)), "a front must not enter stone");
    Check.That(walled[new(8, 0, 4)].Amount < walled[new(3, 0, 4)].Amount, "a front must weaken with distance");
    Check.That(Blast(wall: false, brush: true)[new(3, 0, 4)].Distance == 2f, "brush that half conducts must cost two cells of path");
}

// --- sleep: a quiet field costs nothing; a disturbance wakes only its chunk and settles again ---
{
    ChemistryField field = new(fire, new("air"));
    field.Fill(new(0, 0, 0), new(19, 0, 3), stone);
    Run(field, 10);
    Check.That(field.ActiveChunkCount == 0, "a quiet field must sleep");
    field.SetQuantity(new(4, 0, 1), heat, 400f);
    field.Step();
    Check.That(field.ActiveChunkCount == 1, "warming one cell must wake only its chunk");
    int steps = 0;
    while (field.ActiveChunkCount > 0 && steps < 5000)
    {
        field.Step();
        steps++;
    }

    Check.That(field.ActiveChunkCount == 0, $"the field must sleep again once the heat has settled ({steps} steps)");
}

// --- the reader: problems are reported with their path, not dropped ----------------------------
{
    void Rejects(string json, string path, string problem)
    {
        try
        {
            ChemistryPack.Read(json);
            Check.That(false, $"the reader must reject: {problem}");
        }
        catch (ChemistryContentException error)
        {
            Check.That(error.Path == path && error.Problem.Contains(problem, StringComparison.Ordinal),
                $"expected '{path}: …{problem}…', got '{error.Message}'");
        }
    }

    const string Head = """{ "quantities": [{ "id": "heat" }], "materials": [{ "id": "air" }]""";
    Rejects(Head + """, "transforms": [{ "id": "t", "from": ["air"], "to": "burnt-grass", "when": [{ "quantity": "heat", "atLeast": 1 }] }] }""",
        "$.transforms[0].to", "unknown material 'burnt-grass'");
    Rejects("""{ "quantities": [{ "id": "heat" }], "materials": [{ "id": "air", "colour": "blue" }] }""",
        "$.materials[0].colour", "unknown property");
    Rejects("""{ "quantities": [{ "id": "heat", "min": 5, "max": 1 }], "materials": [{ "id": "air" }] }""",
        "$.quantities[0]", "min 5 is above max 1");
    Rejects("""{ "quantities": [{ "id": "heat" }], "materials": [{ "id": "air", "conduct": { "heat": 1.5 } }] }""",
        "$.materials[0].conduct.heat", "within 0..1");
    Rejects("""{ "quantities": [{ "id": "heat" }], "materials": [{ "id": "air" }, { "id": "air" }] }""",
        "$.materials[1].id", "already declared");
    Rejects(Head + """, "materials2": [] }""", "$.materials2", "unknown property");
    Rejects(Head + """, "transforms": [{ "id": "t", "from": ["air"], "to": "air", "when": [{ "open": true }] }] }""",
        "$.transforms[0].to", "must change identity");
    Rejects("""{ "quantities": [{ "id": "heat" }, { "id": "b" }], "materials": [{ "id": "air" }, { "id": "x" }], "transforms": [{ "id": "t", "from": ["air"], "to": "x", "resistedBy": "b", "when": [{ "open": true }] }] }""",
        "$.transforms[0]", "an immediate transform has none");
    Rejects("{ \"quantities\": [", "$", "");
    Check.That(fire.Modulators.SequenceEqual(["fire"]), "the fire pack must declare its one modulator");
}

// --- cost: a burning meadow, for the record rather than as a gate -----------------------------
{
    ChemistryField field = new(fire, new("air", Seed: 1));
    field.Fill(new(0, 0, 0), new(63, 0, 63), stone);
    field.Fill(new(0, 1, 0), new(63, 1, 63), grass);
    field.Fill(new(30, 1, 30), new(33, 1, 33), burning);
    foreach (Cell cell in Box(new(30, 1, 30), new(33, 1, 33)))
    {
        field.SetQuantity(cell, fuel, 6f);
        field.SetQuantity(cell, heat, 600f);
    }

    Stopwatch clock = Stopwatch.StartNew();
    Run(field, 300);
    clock.Stop();
    int burnt = Box(new(0, 1, 0), new(63, 1, 63)).Count(cell => field.MaterialAt(cell) != grass);
    Console.WriteLine($"meadow 64x64 (16 chunks), 300 steps: {clock.Elapsed.TotalMilliseconds / 300:F2} ms/step, " +
        $"{burnt} of 4096 cells caught, {field.ActiveChunkCount} chunks active at the end");
}

return Check.Finish();

static void Run(ChemistryField field, int steps)
{
    for (int step = 0; step < steps; step++)
    {
        field.Step();
    }
}

static IEnumerable<Cell> Box(Cell from, Cell to)
{
    for (int z = from.Z; z <= to.Z; z++)
    {
        for (int y = from.Y; y <= to.Y; y++)
        {
            for (int x = from.X; x <= to.X; x++)
            {
                yield return new Cell(x, y, z);
            }
        }
    }
}

static IEnumerable<Cell> Column(ChemistryField field, int x) => Enumerable.Range(0, 3).Select(z => new Cell(x, 1, z));

// On a stone floor: grass at x 0..9 and 13..19 (three cells deep), stone at 10..12, lit at x = 0.
ChemistryField GrassRowWithGap(ulong seed)
{
    ChemistryField field = new(fire, new("air", Seed: seed));
    field.Fill(new(0, 0, 0), new(19, 0, 2), stone);
    field.Fill(new(0, 1, 0), new(9, 1, 2), grass);
    field.Fill(new(10, 1, 0), new(12, 1, 2), stone);
    field.Fill(new(13, 1, 0), new(19, 1, 2), grass);
    foreach (Cell cell in Column(field, 0))
    {
        field.SetQuantity(cell, heat, 600f);
    }

    return field;
}

static List<(long Step, Cell Cell, int To)> Burn(ChemistryField field, int steps)
{
    List<(long Step, Cell Cell, int To)> changes = [];
    for (int step = 0; step < steps; step++)
    {
        field.Step();
        changes.AddRange(field.MaterialChanges.Select(change => (field.StepIndex, change.Cell, change.To)));
    }

    return changes;
}
