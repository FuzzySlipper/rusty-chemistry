using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using RustyChemistry.Content;
using EngineColor = Rusty.Engine.Color;

namespace ChemistrySandbox.Setup;

/// <summary>
/// The sandbox's authored setup (<c>content/sandbox/sandbox.json</c>) and the chemistry pack it names: how
/// each material looks, which quantities the overlay can show, what each tool does, and the wind's speed.
/// </summary>
internal sealed record SandboxContent(
    string Pack,
    ulong Seed,
    Dictionary<string, MaterialLook> Materials,
    OverlayDefinition[] Overlays,
    ToolDefinition[] Tools,
    float WindSpeed)
{
    internal const string Path = "sandbox/sandbox.json";

    internal static (SandboxContent Setup, ChemistryPack Pack) Read(ProductContent content)
    {
        SandboxContent setup = JsonSerializer.Deserialize(content.ReadBytes(Path).Span, SandboxJson.Default.SandboxContent)
            ?? throw new InvalidDataException($"{Path}: empty");
        ChemistryPack pack = ChemistryPack.Read(content.ReadText(setup.Pack));
        setup.Check(pack);
        return (setup, pack);
    }

    /// <summary>Every id the setup names must exist in the pack, so a typo fails at load, not on a click.</summary>
    private void Check(ChemistryPack pack)
    {
        foreach (string material in Materials.Keys)
        {
            pack.Material(material);
        }

        foreach (OverlayDefinition overlay in Overlays)
        {
            pack.Quantity(overlay.Quantity);
        }

        foreach (ToolDefinition tool in Tools)
        {
            int effects = (tool.Emit is null ? 0 : 1) + (tool.Discharge is null ? 0 : 1) + (tool.Front is null ? 0 : 1);
            if (effects != 1)
            {
                throw new InvalidDataException($"{Path}: tool '{tool.Id}' needs exactly one of emit, discharge or front");
            }

            pack.Quantity(tool.Emit?.Quantity ?? tool.Discharge?.Quantity ?? tool.Front!.Quantity);
        }
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

/// <summary>A quantity the overlay can show: invisible below <see cref="Low"/>, full strength at <see cref="High"/>.</summary>
internal sealed record OverlayDefinition(string Quantity, string Label, string Color, float Low, float High);

/// <summary>
/// What a click does. <see cref="At"/> is <c>hit</c> for the clicked cell or <c>above</c> for the open cell in front
/// of the clicked face. Exactly one effect is set.
/// </summary>
internal sealed record ToolDefinition(string Id, string Label, string At, EmitTool? Emit = null, DischargeTool? Discharge = null,
    FrontTool? Front = null);

internal sealed record EmitTool(string Quantity, float Radius, float Rate, float Seconds);

internal sealed record DischargeTool(string Quantity, float Amount, float MinConductance, int MaxCells);

internal sealed record FrontTool(string Quantity, float Amplitude, float Speed, float Attenuation, float MaxDistance);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(SandboxContent))]
internal sealed partial class SandboxJson : JsonSerializerContext;
