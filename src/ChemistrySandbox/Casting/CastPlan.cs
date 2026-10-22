using RustyChemistry.Simulation;

namespace ChemistrySandbox.Casting;

/// <summary>One thing a cast does to the field, through one of its operators.</summary>
internal abstract record CastOperation
{
    internal abstract void Apply(ChemistryField field);
}

internal sealed record EmitOperation(EmitterSpec Spec) : CastOperation
{
    internal override void Apply(ChemistryField field) => field.AddEmitter(Spec);
}

internal sealed record DischargeOperation(Cell Origin, int Quantity, float Amount, float MinConductance, int MaxCells) : CastOperation
{
    internal override void Apply(ChemistryField field) => field.QueueDischarge(Origin, Quantity, Amount, MinConductance, MaxCells);
}

internal sealed record FrontOperation(Cell Origin, int Quantity, float Amplitude, float Speed, float Attenuation, float MaxDistance)
    : CastOperation
{
    internal override void Apply(ChemistryField field) => field.StartFront(Origin, Quantity, Amplitude, Speed, Attenuation, MaxDistance);
}

/// <summary>A cast's operations, each due some seconds after it lands, and a line saying what was composed.</summary>
internal sealed record CastPlan(IReadOnlyList<(float At, CastOperation Operation)> Operations, float Delay, string Summary);

/// <summary>How a cast is felt: scales on its strength, its radius and how long it takes to land.</summary>
internal readonly record struct CastFeel(float Strength, float Radius, float Delay)
{
    internal static CastFeel Plain => new(1f, 1f, 1f);
}

/// <summary>Something the player can use on a clicked cell.</summary>
internal interface ICastTool
{
    string Id { get; }

    string Label { get; }

    /// <summary>A short line under the label, if any: a channel's trigrams.</summary>
    string? Detail { get; }

    /// <summary>The trigrams the tool contains, which decide whether an Emperor makes it vivid; none for a plain tool.</summary>
    IReadOnlyList<string> Trigrams { get; }

    /// <summary>What using the tool on <paramref name="hit"/> does, given the open cell in front of the clicked face.</summary>
    CastPlan Plan(ChemistryField field, Cell hit, Cell above, CastFeel feel);
}
