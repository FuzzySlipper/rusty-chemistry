namespace ChemistrySandbox.Channels;

/// <summary>
/// The active Emperor's Horseman container (spec §3): a channel containing one of its trigrams is vivid, every
/// other one muted. Never blocked, never refused. Which candidate meaning of "muted" applies is a choice
/// here because the spec has not ruled it.
/// </summary>
internal sealed class Resonance(ChannelContent content)
{
    internal IReadOnlyList<EmperorDefinition> Emperors => content.Emperors;

    internal IReadOnlyList<MutedCandidate> MutedCandidates => content.Muted;

    internal ChainProfile Chain => content.Chain;

    internal EmperorDefinition Emperor { get; private set; } = content.Emperors[0];

    internal MutedCandidate Muted { get; private set; } = content.Muted[0];

    internal bool SelectEmperor(string id) => Select(content.Emperors, emperor => emperor.Id == id, emperor => Emperor = emperor);

    internal bool SelectMuted(string id) => Select(content.Muted, muted => muted.Id == id, muted => Muted = muted);

    internal bool IsVivid(IReadOnlyList<string> trigrams) => trigrams.Any(Emperor.Horseman.Contains);

    private static bool Select<T>(IEnumerable<T> choices, Func<T, bool> match, Action<T> apply)
    {
        T? chosen = choices.FirstOrDefault(match);
        if (chosen is null)
        {
            return false;
        }

        apply(chosen);
        return true;
    }
}
