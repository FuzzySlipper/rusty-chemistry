using RustyChemistry.Content;

namespace ChemistrySandbox.Channels;

/// <summary>
/// The three-layer composition tables for wardens-circuit's channels (spell-channel-system-spec §7.6) and the
/// Emperor's Horseman container (§3). Every channel is a row naming its two trigrams and its governance; its
/// behaviour comes from <see cref="Elements"/>, <see cref="Deliveries"/> and <see cref="Tempos"/>, never from
/// the row itself. The element, delivery, governance and muted tables are placeholders awaiting rulings.
/// </summary>
internal sealed record ChannelContent(
    string[] Media,
    Dictionary<string, ElementProfile> Elements,
    Dictionary<string, DeliveryProfile> Deliveries,
    Dictionary<string, TempoProfile> Tempos,
    float MotionSpeed,
    ChannelDefinition[] Channels,
    EmperorDefinition[] Emperors,
    ChainProfile Chain,
    MutedCandidate[] Muted)
{
    internal static readonly string[] Gravity = ["heaven", "earth"];
    internal static readonly string[] ElementTrigrams = ["fire", "water", "thunder", "wind", "lake", "mountain"];
    internal static readonly string[] Operators = ["transform", "transfer", "discharge", "propagate", "confine", "bind"];

    internal void Check(ChemistryPack pack, string path)
    {
        foreach (string medium in Media)
        {
            pack.Quantity(medium);
        }

        foreach ((string element, ElementProfile profile) in Elements)
        {
            Require(ElementTrigrams.Contains(element), path, $"'{element}' is not an element trigram");
            Require(Operators.Contains(profile.Operator), path, $"element '{element}' has unknown operator '{profile.Operator}'");
            Require((profile.Quantity is null) == (profile.Operator == "transfer"), path,
                $"element '{element}': transfer moves the targeted medium; every other operator names its quantity");
            if (profile.Quantity is string quantity)
            {
                pack.Quantity(quantity);
            }
        }

        foreach (ChannelDefinition channel in Channels)
        {
            string[] trigrams = [channel.Upper, channel.Lower];
            Require(trigrams.Count(Gravity.Contains) == 1, path,
                $"hex {channel.Hex} needs exactly one gravity trigram (heaven or earth) and one element trigram");
            (string delivery, string element) = channel.Composition;
            Require(Elements.ContainsKey(element), path, $"hex {channel.Hex}: no profile for element '{element}'");
            Require(Deliveries.ContainsKey(delivery), path, $"hex {channel.Hex}: no delivery '{delivery}'");
            Governance governance = channel.Governance;
            Require(governance.Motion is "toward" or "away", path, $"hex {channel.Hex}: motion is toward or away");
            Require(governance.Target is "excess" or "deficit", path, $"hex {channel.Hex}: target is excess or deficit");
            Require(Tempos.ContainsKey(governance.Tempo), path, $"hex {channel.Hex}: no tempo '{governance.Tempo}'");
        }

        foreach (EmperorDefinition emperor in Emperors)
        {
            foreach (string trigram in emperor.Horseman)
            {
                Require(Gravity.Contains(trigram) || ElementTrigrams.Contains(trigram), path,
                    $"emperor '{emperor.Id}': '{trigram}' is not a trigram");
            }
        }

        Require(Emperors.Length > 0 && Muted.Length > 0, path, "channels need at least one emperor and one muted candidate");
    }

    private static void Require(bool holds, string path, string problem)
    {
        if (!holds)
        {
            throw new InvalidDataException($"{path}: {problem}");
        }
    }
}

/// <summary>Layer 1: what an element trigram does to matter, as one kernel operator acting through a quantity.</summary>
internal sealed record ElementProfile(string Operator, float Amount, string? Quantity = null);

/// <summary>
/// Layer 2: how a gravity trigram in its position delivers the element: over what <see cref="Radius"/>, at what
/// <see cref="Strength"/>, after what <see cref="Delay"/>, how far a discharge or front may <see cref="Reach"/>,
/// and its own inward (positive) or outward draw on what it delivers.
/// </summary>
internal sealed record DeliveryProfile(float Radius, float Strength, float Delay, float Reach, float Gather);

/// <summary>A governance tempo: how long the cast lasts, in how many repeats, at what level, and how fast its fronts run.</summary>
internal sealed record TempoProfile(float Seconds, int Repeats, float Level, float FrontSpeed);

/// <summary>A hexagram as a castable channel: its two trigrams and its governance.</summary>
internal sealed record ChannelDefinition(int Hex, string Name, string Upper, string Lower, Governance Governance)
{
    /// <summary>The delivery (gravity trigram and its position, e.g. <c>heaven-upper</c>) and the element trigram.</summary>
    internal (string Delivery, string Element) Composition => ChannelContent.Gravity.Contains(Upper)
        ? ($"{Upper}-upper", Lower)
        : ($"{Lower}-lower", Upper);
}

/// <summary>
/// Layer 3, as a placeholder convention: every hexagram fills the same three slots. <see cref="Motion"/> pushes
/// what the cast moves inward (toward) or outward (away); <see cref="Target"/> picks the medium most above
/// (excess) or below (deficit) its healthy level; <see cref="Tempo"/> names a tempo profile.
/// </summary>
internal sealed record Governance(string Motion, string Target, string Tempo);

internal sealed record EmperorDefinition(string Id, string Label, string[] Horseman);

/// <summary>A vivid cast within <see cref="Window"/> seconds of the last vivid one chains: instant, at <see cref="Strength"/>.</summary>
internal sealed record ChainProfile(float Window, float Strength);

/// <summary>A candidate meaning of "muted": muted casts never chain, and also scale their delay, strength and radius.</summary>
internal sealed record MutedCandidate(string Id, string Label, float Delay = 1f, float Strength = 1f, float Radius = 1f);
