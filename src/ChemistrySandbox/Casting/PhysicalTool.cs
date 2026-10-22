using ChemistrySandbox.Setup;
using RustyChemistry.Content;
using RustyChemistry.Simulation;

namespace ChemistrySandbox.Casting;

/// <summary>
/// An authored tool with one effect: an emitter for a while, a discharge, or a front, at the clicked cell or
/// the open cell in front of it. The delivery layer at its simplest, with no composition.
/// </summary>
internal sealed class PhysicalTool(ToolDefinition tool, ChemistryPack pack) : ICastTool
{
    public string Id => tool.Id;

    public string Label => tool.Label;

    public string? Detail => null;

    public IReadOnlyList<string> Trigrams => [];

    public CastPlan Plan(ChemistryField field, Cell hit, Cell above, CastFeel feel)
    {
        Cell at = tool.At == "above" ? above : hit;
        CastOperation operation = tool switch
        {
            { Emit: EmitTool emit } => new EmitOperation(new EmitterSpec(EmitterShape.Sphere(at.Center, emit.Radius * feel.Radius),
                pack.Quantity(emit.Quantity), emit.Rate * feel.Strength, Lifetime: emit.Seconds)),
            { Discharge: DischargeTool discharge } => new DischargeOperation(at, pack.Quantity(discharge.Quantity),
                discharge.Amount * feel.Strength, discharge.MinConductance, discharge.MaxCells),
            { Front: FrontTool front } => new FrontOperation(at, pack.Quantity(front.Quantity), front.Amplitude * feel.Strength,
                front.Speed, front.Attenuation, front.MaxDistance * feel.Radius),
            _ => throw new InvalidOperationException($"tool '{tool.Id}' has no effect"),
        };
        string where = field.IsLoaded(at) ? $"{pack.Materials[field.MaterialAt(at)].Id} at {at.X},{at.Y},{at.Z}" : "outside the field";
        return new CastPlan([(0f, operation)], 0f, $"on {where}");
    }
}
