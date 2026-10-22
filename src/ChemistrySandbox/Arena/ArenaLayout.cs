using RustyChemistry.Content;
using RustyChemistry.Simulation;

namespace ChemistrySandbox.Arena;

/// <summary>
/// The test arena, cell by cell: a meadow on dirt and bedrock, with set pieces for each thing the physical
/// pack should show. A grass fire meets a stone firebreak; a stone hut with a campfire inside fills with
/// smoke; a pond wets its shore and carries a shock; a wooden shed and trees burn slower than grass.
/// </summary>
internal static class ArenaLayout
{
    internal const int Width = 64;
    internal const int Height = 32;
    internal const int Depth = 64;

    /// <summary>The top of the ground: grass sits here, and everything stands on it.</summary>
    internal const int Surface = 3;

    private static readonly (int X, int Z)[] Trees = [(24, 20), (28, 29), (19, 31), (42, 22), (51, 13), (13, 15), (54, 31), (22, 9)];

    internal static ChemistryField Build(ChemistryPack pack, ulong seed)
    {
        ChemistryField field = new(pack, new ChemistryFieldSettings("air", Seed: seed));
        int air = pack.Material("air"), stone = pack.Material("stone"), dirt = pack.Material("dirt");
        int grass = pack.Material("grass"), wood = pack.Material("wood"), leaves = pack.Material("leaves");
        int water = pack.Material("water");

        field.Fill(new(0, 0, 0), new(Width - 1, Height - 1, Depth - 1), air);
        field.Fill(new(0, 0, 0), new(Width - 1, 0, Depth - 1), stone);
        field.Fill(new(0, 1, 0), new(Width - 1, Surface - 1, Depth - 1), dirt);
        field.Fill(new(0, Surface, 0), new(Width - 1, Surface, Depth - 1), grass);

        // A stone path three cells wide across the meadow: wide enough that grass fire does not jump it.
        field.Fill(new(36, Surface, 18), new(38, Surface, Depth - 1), stone);

        // The pond, three deep, and a puddle beside it that a shock in the pond should not reach.
        for (int z = 0; z < Depth; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                float dx = x + 0.5f - 48f, dz = z + 0.5f - 46f;
                if ((dx * dx) + (dz * dz) <= 36f)
                {
                    field.Fill(new(x, 1, z), new(x, Surface, z), water);
                }
            }
        }

        field.Fill(new(40, Surface, 55), new(42, Surface, 57), water);

        // The stone hut: a closed room with a doorway and a campfire, where smoke should collect.
        field.Fill(new(8, Surface + 1, 38), new(18, Surface + 6, 48), stone);
        field.Fill(new(9, Surface + 1, 39), new(17, Surface + 5, 47), air);
        field.Fill(new(9, Surface, 39), new(17, Surface, 47), dirt);
        field.Fill(new(18, Surface + 1, 43), new(18, Surface + 3, 44), air);
        field.Fill(new(12, Surface + 1, 43), new(14, Surface + 1, 43), wood);
        field.Fill(new(13, Surface + 1, 42), new(13, Surface + 2, 44), wood);

        // The wooden shed: everything burns, only slower than the grass around it.
        field.Fill(new(28, Surface + 1, 6), new(34, Surface + 5, 12), wood);
        field.Fill(new(29, Surface + 1, 7), new(33, Surface + 4, 11), air);
        field.Fill(new(31, Surface + 1, 6), new(31, Surface + 2, 6), air);

        foreach ((int x, int z) in Trees)
        {
            Tree(field, x, z, wood, leaves, air);
        }

        field.MarkOpenColumns();
        return field;
    }

    private static void Tree(ChemistryField field, int x, int z, int wood, int leaves, int air)
    {
        const int Trunk = 5;
        int top = Surface + Trunk;
        for (int dz = -2; dz <= 2; dz++)
        {
            for (int dy = -1; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    Cell cell = new(x + dx, top + dy, z + dz);
                    if ((dx * dx) + (dy * dy * 2) + (dz * dz) <= 6 && field.MaterialAt(cell) == air)
                    {
                        field.Load(cell, leaves);
                    }
                }
            }
        }

        field.Fill(new(x, Surface + 1, z), new(x, top, z), wood);
    }
}
