using RustyChemistry.Simulation;

namespace RustyChemistry.Casting;

/// <summary>One thing a cast does to the field, through one of its operators.</summary>
public abstract record CastOperation
{
    public abstract void Apply(ChemistryField field);
}

public sealed record EmitOperation(EmitterSpec Spec) : CastOperation
{
    public override void Apply(ChemistryField field) => field.AddEmitter(Spec);
}

public sealed record DischargeOperation(Cell Origin, int Quantity, float Amount, float MinConductance, int MaxCells) : CastOperation
{
    public override void Apply(ChemistryField field) => field.QueueDischarge(Origin, Quantity, Amount, MinConductance, MaxCells);
}

public sealed record FrontOperation(Cell Origin, int Quantity, float Amplitude, float Speed, float Attenuation, float MaxDistance)
    : CastOperation
{
    public override void Apply(ChemistryField field) => field.StartFront(Origin, Quantity, Amplitude, Speed, Attenuation, MaxDistance);
}

/// <summary>A cast's operations, each due some seconds after it lands, and a line saying what was composed.</summary>
public sealed record CastPlan(IReadOnlyList<(float At, CastOperation Operation)> Operations, float Delay, string Summary);

/// <summary>How a cast is felt: scales on its strength, its radius and how long it takes to land.</summary>
public readonly record struct CastFeel(float Strength, float Radius, float Delay)
{
    public static CastFeel Plain => new(1f, 1f, 1f);
}

/// <summary>Where a cast aims: the cell it struck, and the open cell in front of the struck face.</summary>
public readonly record struct CastTarget(Cell Hit, Cell Above);

/// <summary>Something that can be cast at a target.</summary>
public interface ICastTool
{
    string Id { get; }

    string Label { get; }

    /// <summary>A short line under the label, if any: a formula's trigrams.</summary>
    string? Detail { get; }

    /// <summary>The trigrams the tool is composed of, which a resonance may read; none for a plain tool.</summary>
    IReadOnlyList<string> Trigrams { get; }

    /// <summary>What casting the tool at <paramref name="target"/> does.</summary>
    CastPlan Plan(ChemistryField field, CastTarget target, CastFeel feel);
}
