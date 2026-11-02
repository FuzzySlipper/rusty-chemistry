using ChemistrySandbox.Channels;
using RustyChemistry.Casting;
using RustyChemistry.Simulation;

namespace ChemistrySandbox.Casting;

/// <summary>
/// The tools in reach, which one is in hand, and the shared caster their casts go through. With a
/// <see cref="Resonance"/>, vivid casts chain and muted ones take the muted feel.
/// </summary>
internal sealed class ToolBelt(IReadOnlyList<ICastTool> tools, Resonance? resonance)
{
    private readonly Caster caster = new();
    private double lastVivid = double.NegativeInfinity;

    internal IReadOnlyList<ICastTool> Tools { get; } = tools;

    internal ICastTool Current { get; private set; } = tools[0];

    internal Resonance? Resonance { get; } = resonance;

    /// <summary>Operations still to come, from casts not yet landed or still running.</summary>
    internal int Pending => caster.Pending;

    internal bool Select(string id)
    {
        ICastTool? tool = Tools.FirstOrDefault(tool => tool.Id == id);
        if (tool is null)
        {
            return false;
        }

        Current = tool;
        return true;
    }

    /// <summary>Whether a tool is vivid under the active Emperor; plain tools and scenes without one are.</summary>
    internal bool IsVivid(ICastTool tool) => Resonance?.IsVivid(tool.Trigrams) ?? true;

    /// <summary>Casts the tool in hand at a clicked cell and says what it composed.</summary>
    internal string Cast(ChemistryField field, Cell hit, Cell above)
    {
        ICastTool tool = Current;
        CastFeel feel = CastFeel.Plain;
        string tone = "";
        if (Resonance is not null)
        {
            if (!IsVivid(tool))
            {
                MutedCandidate muted = Resonance.Muted;
                feel = new CastFeel(muted.Strength, muted.Radius, muted.Delay);
                tone = $"muted ({muted.Label.ToLowerInvariant()})";
            }
            else if (caster.Now - lastVivid <= Resonance.Chain.Window)
            {
                feel = new CastFeel(Resonance.Chain.Strength, 1f, 0f);
                tone = "vivid, chained";
                lastVivid = caster.Now;
            }
            else
            {
                tone = "vivid";
                lastVivid = caster.Now;
            }
        }

        CastPlan plan = caster.Cast(field, tool, new CastTarget(hit, above), feel);
        return tone.Length == 0 ? $"{tool.Label} {plan.Summary}" : $"{tool.Label}, {tone}: {plan.Summary}";
    }

    /// <summary>Moves cast time on by one field step, applying what falls due before the field steps.</summary>
    internal void Advance(ChemistryField field, double seconds) => caster.Advance(field, seconds);
}
