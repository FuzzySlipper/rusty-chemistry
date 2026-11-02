using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RustyChemistry.Content;
using RustyChemistry.Simulation;

namespace RustyChemistry.Casting;

/// <summary>
/// A compiled formula: what a cast does, as timed steps through the kernel's operators, naming quantities by id.
/// It needs nothing it was composed from to be cast again, so it is what a relic can carry, and a plain tool is
/// a formula of one step. <see cref="Delay"/> is how long it takes to land; each step's <c>at</c> follows that.
/// </summary>
public sealed record Formula(string Id, string Label, FormulaStep[] Steps, float Delay = 0f, string? Detail = null,
    string[]? Trigrams = null)
{
    private static readonly JsonTypeInfo<Formula> Indented =
        (JsonTypeInfo<Formula>)new JsonSerializerOptions(FormulaJson.Default.Options) { WriteIndented = true }.GetTypeInfo(typeof(Formula));

    public static Formula Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize(json, FormulaJson.Default.Formula) ?? throw new ChemistryContentException("$", "empty formula");
    }

    public string Write() => JsonSerializer.Serialize(this, FormulaJson.Default.Formula);

    /// <summary>The formula as indented JSON, for a person to read or keep as content.</summary>
    public string WriteIndented() => JsonSerializer.Serialize(this, Indented);

    /// <summary>Every step has one effect and an anchor, and every quantity it names is in the pack.</summary>
    public void Check(ChemistryPack pack, string path)
    {
        ArgumentNullException.ThrowIfNull(pack);
        Require(Steps.Length > 0, path, $"formula '{Id}' has no steps");
        foreach (FormulaStep step in Steps)
        {
            int effects = (step.Emit is null ? 0 : 1) + (step.Discharge is null ? 0 : 1) + (step.Front is null ? 0 : 1);
            Require(effects == 1, path, $"formula '{Id}': a step needs exactly one of emit, discharge or front");
            Require(step.Anchor is FormulaStep.Hit or FormulaStep.Above, path, $"formula '{Id}': anchor is hit or above");
            Require(step.At >= 0f, path, $"formula '{Id}': a step's at is not negative");
            pack.Quantity(step.Emit?.Quantity ?? step.Discharge?.Quantity ?? step.Front!.Quantity);
        }
    }

    internal static void Require(bool holds, string path, string problem)
    {
        if (!holds)
        {
            throw new ChemistryContentException(path, problem);
        }
    }
}

/// <summary>
/// One effect of a formula, <see cref="At"/> seconds after it lands, at its <see cref="Anchor"/>: <c>hit</c> for the
/// struck cell, <c>above</c> for the open cell in front of the struck face. Exactly one effect is set.
/// </summary>
public sealed record FormulaStep(string Anchor, float At = 0f, EmitEffect? Emit = null, DischargeEffect? Discharge = null,
    FrontEffect? Front = null)
{
    public const string Hit = "hit";
    public const string Above = "above";
}

/// <summary>
/// Holds a sphere for <see cref="Seconds"/>: adds <see cref="Rate"/> a second (drains when negative) and draws the
/// quantity toward its centre at <see cref="Gather"/> cells a second (pushes it out when negative).
/// </summary>
public sealed record EmitEffect(string Quantity, float Radius, float Rate, float Seconds, float Gather = 0f);

public sealed record DischargeEffect(string Quantity, float Amount, float MinConductance, int MaxCells);

public sealed record FrontEffect(string Quantity, float Amplitude, float Speed, float Attenuation, float MaxDistance);

/// <summary>A formula cast as it is: its strength and radius scale with the feel.</summary>
public sealed class FormulaTool(Formula formula, ChemistryPack pack) : ICastTool
{
    public Formula Formula { get; } = formula;

    public string Id => Formula.Id;

    public string Label => Formula.Label;

    public string? Detail => Formula.Detail;

    public IReadOnlyList<string> Trigrams => Formula.Trigrams ?? [];

    public CastPlan Plan(ChemistryField field, CastTarget target, CastFeel feel)
    {
        ArgumentNullException.ThrowIfNull(field);
        List<(float, CastOperation)> operations = [];
        foreach (FormulaStep step in Formula.Steps)
        {
            Cell at = step.Anchor == FormulaStep.Above ? target.Above : target.Hit;
            CastOperation operation = step switch
            {
                { Emit: EmitEffect emit } => new EmitOperation(new EmitterSpec(EmitterShape.Sphere(at.Center, emit.Radius * feel.Radius),
                    pack.Quantity(emit.Quantity), emit.Rate * feel.Strength, emit.Gather, emit.Seconds)),
                { Discharge: DischargeEffect discharge } => new DischargeOperation(at, pack.Quantity(discharge.Quantity),
                    discharge.Amount * feel.Strength, discharge.MinConductance, (int)(discharge.MaxCells * feel.Radius)),
                { Front: FrontEffect front } => new FrontOperation(at, pack.Quantity(front.Quantity), front.Amplitude * feel.Strength,
                    front.Speed, front.Attenuation, front.MaxDistance * feel.Radius),
                _ => throw new InvalidOperationException($"formula '{Formula.Id}' has a step with no effect"),
            };
            operations.Add((step.At, operation));
        }

        Cell hit = target.Hit;
        string where = field.IsLoaded(hit)
            ? string.Create(CultureInfo.InvariantCulture, $"{pack.Materials[field.MaterialAt(hit)].Id} at {hit.X},{hit.Y},{hit.Z}")
            : "outside the field";
        return new CastPlan(operations, Formula.Delay, $"on {where}: {Describe(Formula)}");
    }

    /// <summary>One line per step of what the formula does, for a readout or a panel.</summary>
    public static string Describe(Formula formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        return string.Join("; ", formula.Steps.Select(Line));
    }

    private static string Line(FormulaStep step)
    {
        CultureInfo culture = CultureInfo.InvariantCulture;
        string line = step switch
        {
            { Emit: EmitEffect emit } when emit.Rate == 0f => string.Create(culture,
                $"{(emit.Gather >= 0 ? "draws" : "scatters")} {emit.Quantity} at {Math.Abs(emit.Gather):0.##} cells/s over r{emit.Radius:0.#} for {emit.Seconds:0.#}s"),
            { Emit: EmitEffect emit } => string.Create(culture,
                $"{(emit.Rate >= 0 ? "adds" : "drains")} {Math.Abs(emit.Rate):0.##} {emit.Quantity}/s over r{emit.Radius:0.#} for {emit.Seconds:0.#}s{Drawn(emit.Gather)}"),
            { Discharge: DischargeEffect discharge } => string.Create(culture,
                $"discharges {discharge.Amount:0.#} {discharge.Quantity} through up to {discharge.MaxCells} cells"),
            { Front: FrontEffect front } => string.Create(culture,
                $"sends a {front.Quantity} front of {front.Amplitude:0.#} out to {front.MaxDistance:0.#} cells"),
            _ => "nothing",
        };
        return step.At > 0f ? string.Create(culture, $"{line} (+{step.At:0.#}s)") : line;
    }

    private static string Drawn(float gather) => gather == 0f
        ? ""
        : string.Create(CultureInfo.InvariantCulture, $", {(gather > 0 ? "drawn in" : "pushed out")} {Math.Abs(gather):0.##} cells/s");
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true, RespectRequiredConstructorParameters = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Formula))]
[JsonSerializable(typeof(Formula[]))]
internal sealed partial class FormulaJson : JsonSerializerContext;
