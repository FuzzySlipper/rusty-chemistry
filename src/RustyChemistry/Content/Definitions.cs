using System.Collections.Immutable;

namespace RustyChemistry.Content;

/// <summary>
/// A continuous value every cell holds: heat, moisture, smoke, fuel, a channel's medium. Values stay
/// within <see cref="Min"/>..<see cref="Max"/> and relax toward the field's ambient at <see cref="Settle"/>
/// per second. A change smaller than <see cref="Epsilon"/> counts as no change, which is what lets a quiet
/// chunk sleep.
/// </summary>
public sealed record QuantityDefinition(string Id, float Ambient, float Min, float Max, float Settle, float Epsilon,
    TransferDefinition? Transfer);

/// <summary>
/// How a quantity moves between face neighbours: diffusion at <see cref="Rate"/> per second, scaled by the
/// lower conductance of the two cells; with <see cref="Drift"/>, also carried by the field's drift (wind).
/// <see cref="Rise"/> carries it upward at that many cells per second (downward when negative): buoyancy.
/// A difference in <see cref="BlockedBy"/> across a face closes it: the edge of a held region confines.
/// </summary>
public sealed record TransferDefinition(float Rate, bool Drift, int? BlockedBy, int? Modulator, float Rise = 0f);

/// <summary>
/// A cell identity. <see cref="Conductance"/> (0..1 per quantity) is how readily each quantity passes into
/// and out of it; <see cref="Initial"/> is what a freshly loaded cell holds, the ambient where null;
/// <see cref="Rest"/> is what it settles toward instead of the ambient, where set; <see cref="Sky"/> says
/// whether the open sky passes through it.
/// </summary>
public sealed record MaterialDefinition(string Id, bool Sky, ImmutableArray<float> Conductance, ImmutableArray<float?> Initial,
    ImmutableArray<RestDefinition?> Rest);

/// <summary>
/// A material's own resting value for a quantity: its cells settle toward <see cref="To"/> at <see cref="Settle"/>
/// per second, rather than toward the field's ambient at the quantity's rate. Living grass keeps its water this
/// way, recovering what it loses and drying only while something dries it faster.
/// </summary>
public readonly record struct RestDefinition(float To, float Settle);

public enum ConditionKind : byte
{
    AtLeast,
    AtMost,
    Open,
    Closed,
}

/// <summary>A test of the cell's own state; rules never look at neighbours.</summary>
public readonly record struct Condition(ConditionKind Kind, int Quantity, float Threshold);

public readonly record struct QuantityAmount(int Quantity, float Amount);

/// <summary>
/// A continuous local conversion: while its conditions hold in one of its materials (all when
/// <see cref="Materials"/> is empty), it runs at <see cref="Rate"/> units per second (times the
/// <see cref="Per"/> quantity's value, when named), consuming and producing per unit. It slows to what the
/// consumed quantities have above their minimum, so nothing is driven below its bound.
/// </summary>
public sealed record ReactionRule(string Id, ImmutableArray<int> Materials, ImmutableArray<Condition> Conditions, float Rate,
    int? Per, ImmutableArray<QuantityAmount> Consumes, ImmutableArray<QuantityAmount> Produces, int? Modulator);

/// <summary>
/// A discrete identity change: a cell of a <see cref="From"/> material becomes <see cref="To"/> once its
/// conditions hold, at once or, with <see cref="Chance"/>, at that rate per second; the
/// <see cref="ResistedBy"/> quantity divides the rate by one plus its value. <see cref="Set"/> assigns
/// quantities as the identity changes. Hysteresis is two transforms with different thresholds.
/// </summary>
public sealed record TransformRule(string Id, ImmutableArray<int> From, int To, ImmutableArray<Condition> Conditions, float? Chance,
    int? ResistedBy, int? Modulator, ImmutableArray<QuantityAmount> Set);
