using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChemistrySandbox.Channels;
using Rusty.Engine;
using RustyChemistry.Casting;
using RustyChemistry.Content;
using EngineColor = Rusty.Engine.Color;

namespace ChemistrySandbox.Setup;

/// <summary>The scenes the sandbox offers (<c>content/sandbox/scenes.json</c>), each in its own file.</summary>
internal sealed record SceneList(SceneEntry[] Scenes)
{
    internal const string Path = "sandbox/scenes.json";

    internal static SceneList Read(ProductContent content) =>
        JsonSerializer.Deserialize(content.ReadBytes(Path).Span, SandboxJson.Default.SceneList) ?? throw new InvalidDataException($"{Path}: empty");
}

internal sealed record SceneEntry(string Id, string Label, string File);

/// <summary>
/// One scene's authored setup and the chemistry pack it names: which layout builds the field, how each
/// material looks, which quantities the overlay can show, and what the player can do: <see cref="Tools"/>,
/// formulas cast as they are, or <see cref="Channels"/> composed from their trigrams.
/// </summary>
internal sealed record SceneContent(
    string Pack,
    string Layout,
    ulong Seed,
    Dictionary<string, MaterialLook> Materials,
    OverlayDefinition[] Overlays,
    Formula[]? Tools = null,
    ChannelContent? Channels = null,
    float WindSpeed = 0f)
{
    internal static (SceneContent Scene, ChemistryPack Pack) Read(ProductContent content, string path)
    {
        SceneContent scene = JsonSerializer.Deserialize(content.ReadBytes(path).Span, SandboxJson.Default.SceneContent)
            ?? throw new InvalidDataException($"{path}: empty");
        ChemistryPack pack = ChemistryPack.Read(content.ReadText(scene.Pack));
        scene.Check(pack, path);
        return (scene, pack);
    }

    /// <summary>Every id the scene names must exist in its pack, so a typo fails at load, not on a click.</summary>
    private void Check(ChemistryPack pack, string path)
    {
        foreach (string material in Materials.Keys)
        {
            pack.Material(material);
        }

        foreach (OverlayDefinition overlay in Overlays)
        {
            pack.Quantity(overlay.Quantity);
        }

        if ((Tools is null) == (Channels is null))
        {
            throw new InvalidDataException($"{path}: a scene has either tools or channels");
        }

        foreach (Formula tool in Tools ?? [])
        {
            tool.Check(pack, path);
        }

        Channels?.Check(pack, path);
    }
}

/// <summary>How a material draws as a voxel; a material with no look (air) draws nothing.</summary>
internal sealed record MaterialLook(string Color, float Alpha = 1f, string? Emission = null, float EmissionIntensity = 0f,
    bool Passable = false)
{
    internal EngineColor Base => Linear(Color, Alpha);

    internal Vector3 Glow
    {
        get
        {
            EngineColor glow = Emission is null ? default : Linear(Emission, 1f);
            return new(glow.R, glow.G, glow.B);
        }
    }

    /// <summary>A <c>#rrggbb</c> sRGB colour as the linear colour materials take.</summary>
    internal static EngineColor Linear(string hex, float alpha) =>
        EngineColor.FromSrgbHex(int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture), alpha);
}

/// <summary>
/// A quantity the overlay can show: invisible below <see cref="Low"/>, full strength at <see cref="High"/>. With
/// <see cref="Below"/>, it shows how far the value falls under its ambient instead.
/// </summary>
internal sealed record OverlayDefinition(string Quantity, string Label, string Color, float Low, float High, bool Below = false);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(SceneList))]
[JsonSerializable(typeof(SceneContent))]
internal sealed partial class SandboxJson : JsonSerializerContext;
