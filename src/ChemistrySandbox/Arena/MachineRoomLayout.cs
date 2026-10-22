using System.Numerics;
using RustyChemistry.Content;
using RustyChemistry.Simulation;

namespace ChemistrySandbox.Arena;

/// <summary>
/// A sick machine for the Q90 channels (PLACEHOLDER scenario): an open-topped room of three bays, each with a
/// wall of gears on a frame pillar, all pillars joined by one frame girder. A pathogen distorts one Wuxing
/// medium in each bay, so the gears there jam:
/// <list type="bullet">
/// <item>bay A, an overheated bearing: fire pools around the gears;</item>
/// <item>bay B, a flooded basin: water wells up in a stone basin and spills toward the gears;</item>
/// <item>bay C, a starved drive: wood drains away from the gears.</item>
/// </list>
/// Stone walls with low doorways part the bays, so what spreads, holds or travels around corners matters.
/// </summary>
internal static class MachineRoomLayout
{
    internal const int Width = 48;
    internal const int Height = 16;
    internal const int Depth = 32;

    private const int WallTop = 10;
    private const int GirderZ = 15;

    // Pathogen strengths, per cell per second: what each bay's distortion feeds in (or drains).
    private const float OverheatRate = 2.5f, FloodRate = 5f, StarveRate = -1.2f;

    internal static Vector3 CameraHome => new(Width / 2f, 2f, Depth / 2f);

    internal static (ChemistryField Field, IReadOnlyList<Symptom> Symptoms) Build(ChemistryPack pack, ulong seed)
    {
        ChemistryField field = new(pack, new ChemistryFieldSettings("air", Seed: seed));
        int air = pack.Material("air"), stone = pack.Material("stone"), frame = pack.Material("frame"), gear = pack.Material("gear");

        field.Fill(new(0, 0, 0), new(Width - 1, Height - 1, Depth - 1), air);
        field.Fill(new(0, 0, 0), new(Width - 1, 0, Depth - 1), stone);
        Walls(field, stone);

        // Bay partitions, each with a low doorway, on opposite sides so the bays connect in a chain.
        field.Fill(new(16, 1, 1), new(16, WallTop, Depth - 2), stone);
        field.Fill(new(16, 1, 13), new(16, 4, 17), air);
        field.Fill(new(32, 1, 1), new(32, WallTop, Depth - 2), stone);
        field.Fill(new(32, 1, 3), new(32, 4, 7), air);

        // The basin in bay B: a stone rim two high, water's pathogen at its bottom.
        field.Fill(new(19, 1, 17), new(28, 2, 25), stone);
        field.Fill(new(20, 1, 18), new(27, 2, 24), air);

        // One girder along the wall tops joins every bay's pillar, through the partitions.
        field.Fill(new(1, WallTop, GirderZ), new(Width - 2, WallTop, GirderZ), frame);

        Symptom[] symptoms =
        [
            Bay(field, "Bay A: overheated bearing", "fire", SymptomKind.Excess, 8, gear, frame),
            Bay(field, "Bay B: flooded basin", "water", SymptomKind.Excess, 24, gear, frame),
            Bay(field, "Bay C: starved drive", "wood", SymptomKind.Deficit, 40, gear, frame),
        ];

        field.AddEmitter(new EmitterSpec(EmitterShape.Sphere(new Vector3(8.5f, 3.5f, 17.5f), 2f), pack.Quantity("fire"), OverheatRate));
        field.AddEmitter(new EmitterSpec(EmitterShape.Sphere(new Vector3(23.5f, 1.5f, 19.5f), 2.5f), pack.Quantity("water"), FloodRate));
        field.AddEmitter(new EmitterSpec(EmitterShape.Sphere(new Vector3(40.5f, 3.5f, 16f), 2f), pack.Quantity("wood"), StarveRate));
        return (field, symptoms);
    }

    private static void Walls(ChemistryField field, int stone)
    {
        field.Fill(new(0, 1, 0), new(Width - 1, WallTop, 0), stone);
        field.Fill(new(0, 1, Depth - 1), new(Width - 1, WallTop, Depth - 1), stone);
        field.Fill(new(0, 1, 0), new(0, WallTop, Depth - 1), stone);
        field.Fill(new(Width - 1, 1, 0), new(Width - 1, WallTop, Depth - 1), stone);
    }

    /// <summary>A three-by-three wall of gears centred on <paramref name="x"/>, on a pillar up to the girder.</summary>
    private static Symptom Bay(ChemistryField field, string name, string medium, SymptomKind kind, int x, int gear, int frame)
    {
        Cell min = new(x - 1, 2, GirderZ), max = new(x + 1, 4, GirderZ);
        field.Fill(min, max, gear);
        field.Fill(new(x, 1, GirderZ), new(x, 1, GirderZ), frame);
        field.Fill(new(x, 5, GirderZ), new(x, WallTop - 1, GirderZ), frame);
        return new Symptom(name, medium, kind, min, max);
    }
}
