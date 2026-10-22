using ChemistrySandbox.Setup;
using RustyChemistry.Content;
using RustyChemistry.Simulation;

namespace ChemistrySandbox.Tools;

/// <summary>
/// The authored tools and which one is in hand. Using one on a cell is the delivery layer at its simplest:
/// an emitter for a while, a discharge, or a front, through the field's own operators.
/// </summary>
internal sealed class ToolBelt(IReadOnlyList<ToolDefinition> tools, ChemistryPack pack)
{
    internal IReadOnlyList<ToolDefinition> Tools { get; } = tools;

    internal ToolDefinition Current { get; private set; } = tools[0];

    internal bool Select(string id)
    {
        ToolDefinition? tool = Tools.FirstOrDefault(tool => tool.Id == id);
        if (tool is null)
        {
            return false;
        }

        Current = tool;
        return true;
    }

    /// <summary>Uses the tool in hand on a clicked cell, given the open cell in front of the clicked face.</summary>
    internal string Use(ChemistryField field, Cell hit, Cell above)
    {
        ToolDefinition tool = Current;
        Cell at = tool.At == "above" ? above : hit;
        if (!field.IsLoaded(at))
        {
            return $"{tool.Label}: outside the arena";
        }

        string where = $"{pack.Materials[field.MaterialAt(at)].Id} at {at.X},{at.Y},{at.Z}";
        if (tool.Emit is EmitTool emit)
        {
            field.AddEmitter(new EmitterSpec(EmitterShape.Sphere(at.Center, emit.Radius), pack.Quantity(emit.Quantity), emit.Rate,
                Lifetime: emit.Seconds));
        }
        else if (tool.Discharge is DischargeTool discharge)
        {
            field.QueueDischarge(at, pack.Quantity(discharge.Quantity), discharge.Amount, discharge.MinConductance, discharge.MaxCells);
        }
        else if (tool.Front is FrontTool front)
        {
            field.StartFront(at, pack.Quantity(front.Quantity), front.Amplitude, front.Speed, front.Attenuation, front.MaxDistance);
        }

        return $"{tool.Label} on {where}";
    }
}
