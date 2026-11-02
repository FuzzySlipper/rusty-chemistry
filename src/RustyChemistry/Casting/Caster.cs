using RustyChemistry.Simulation;

namespace RustyChemistry.Casting;

/// <summary>
/// The casts on their way. A cast lands after its delay and its operations follow on field time: the caster
/// advances only as the field steps, so a paused field holds them, and nothing here keeps a clock of its own.
/// </summary>
public sealed class Caster
{
    private readonly List<(double Due, CastOperation Operation)> pending = [];

    /// <summary>Field time the caster has advanced through, in seconds.</summary>
    public double Now { get; private set; }

    /// <summary>Operations still to come, from casts not yet landed or still running.</summary>
    public int Pending => pending.Count;

    /// <summary>Casts <paramref name="tool"/> at <paramref name="target"/> with a feel and returns its plan.</summary>
    public CastPlan Cast(ChemistryField field, ICastTool tool, CastTarget target, CastFeel feel)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(tool);
        CastPlan plan = tool.Plan(field, target, feel);
        double lands = Now + (plan.Delay * feel.Delay);
        foreach ((float at, CastOperation operation) in plan.Operations)
        {
            pending.Add((lands + at, operation));
        }

        return plan;
    }

    /// <summary>Moves cast time on by one field step, applying what falls due; call it before the field steps.</summary>
    public void Advance(ChemistryField field, double seconds)
    {
        ArgumentNullException.ThrowIfNull(field);
        Now += seconds;
        if (pending.Count == 0)
        {
            return;
        }

        pending.Sort((a, b) => a.Due.CompareTo(b.Due));
        int due = pending.FindIndex(entry => entry.Due > Now);
        int count = due < 0 ? pending.Count : due;
        for (int index = 0; index < count; index++)
        {
            pending[index].Operation.Apply(field);
        }

        pending.RemoveRange(0, count);
    }

    /// <summary>Drops every cast still on its way, as when the field is rebuilt.</summary>
    public void Clear() => pending.Clear();
}
