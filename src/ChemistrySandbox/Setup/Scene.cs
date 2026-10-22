using System.Numerics;
using ChemistrySandbox.Arena;
using ChemistrySandbox.Casting;
using ChemistrySandbox.Channels;
using Rusty.Engine;
using RustyChemistry.Content;
using RustyChemistry.Simulation;

namespace ChemistrySandbox.Setup;

/// <summary>
/// One loaded scene: its content and pack, the tools it offers, the Emperor's resonance when it has channels,
/// and how to build a fresh field for it. The resonance outlives resets, so a chosen Emperor stays chosen.
/// </summary>
internal sealed class Scene
{
    internal Scene(SceneEntry entry, ProductContent content)
    {
        Entry = entry;
        (Content, Pack) = SceneContent.Read(content, entry.File);
        if (Content.Layout is not ("arena" or "machine-room"))
        {
            throw new InvalidDataException($"{entry.File}: unknown layout '{Content.Layout}'");
        }

        Tools = Content.Channels is ChannelContent channels
            ? [.. channels.Channels.Select(channel => (ICastTool)new ChannelTool(channel, channels, Pack))]
            : [.. Content.Tools!.Select(tool => (ICastTool)new PhysicalTool(tool, Pack))];
        Resonance = Content.Channels is ChannelContent composed ? new Resonance(composed) : null;
    }

    internal SceneEntry Entry { get; }

    internal SceneContent Content { get; }

    internal ChemistryPack Pack { get; }

    internal IReadOnlyList<ICastTool> Tools { get; }

    internal Resonance? Resonance { get; }

    internal Vector3 CameraHome => Content.Layout == "arena"
        ? new Vector3(ArenaLayout.Width / 2f, ArenaLayout.Surface, ArenaLayout.Depth / 2f)
        : MachineRoomLayout.CameraHome;

    internal (ChemistryField Field, IReadOnlyList<Symptom> Symptoms) Build() => Content.Layout == "arena"
        ? (ArenaLayout.Build(Pack, Content.Seed), [])
        : MachineRoomLayout.Build(Pack, Content.Seed);
}
