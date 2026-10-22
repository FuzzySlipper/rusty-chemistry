using System.Globalization;
using RustyChemistry.Content;
using RustyChemistry.Simulation;

namespace ChemistrySandbox.Arena;

internal enum SymptomKind : byte
{
    Excess,
    Deficit,
}

/// <summary>
/// A readable condition at a symptom locus (the projectile brief's SymptomLocus): a medium off its healthy
/// level around a set of gears, and how many of those gears have jammed. Read only, never decided here.
/// </summary>
internal sealed record Symptom(string Name, string Medium, SymptomKind Kind, Cell Min, Cell Max)
{
    internal SymptomReading Read(ChemistryField field, ChemistryPack pack)
    {
        int quantity = pack.Quantity(Medium), jammed = pack.Material("jammed-gear");
        float sum = 0f;
        int cells = 0, stuck = 0;
        for (int z = Min.Z; z <= Max.Z; z++)
        {
            for (int y = Min.Y; y <= Max.Y; y++)
            {
                for (int x = Min.X; x <= Max.X; x++)
                {
                    Cell cell = new(x, y, z);
                    sum += field.Quantity(cell, quantity);
                    stuck += field.MaterialAt(cell) == jammed ? 1 : 0;
                    cells++;
                }
            }
        }

        return new SymptomReading(Name, Medium, sum / cells, field.Ambient(quantity), stuck, cells);
    }
}

internal readonly record struct SymptomReading(string Name, string Medium, float Level, float Healthy, int Jammed, int Gears)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Name}: {Medium} {Level:0.##} (healthy {Healthy:0.#}), {Jammed}/{Gears} gears jammed");
}
