using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RustyChemistry.Content;

namespace RustyChemistry.Casting;

/// <summary>
/// A formula compiler shaped by the eight trigrams. A formula's source is a hexagram, one trigram over another, and
/// as in the classical reading the lower trigram is the inner one, what acts, and the upper the outer one, how it
/// reaches the world:
/// <list type="bullet">
/// <item>the lower trigram's <see cref="TrigramDefinition.Inner"/> effects name the operators and quantities;</item>
/// <item>the upper trigram's <see cref="TrigramDefinition.Outer"/> delivery sets anchor, radius, strength, delay, reach and tempo;</item>
/// <item>the hexagram's yin/yang balance, its count of yang lines, scales and signs them through <see cref="Balance"/>.</item>
/// </list>
/// All 64 hexagrams compile by that one rule; none is authored on its own. What each trigram does and every number
/// are the grammar's content, written against one pack.
/// </summary>
public sealed record BaguaGrammar(Dictionary<string, TrigramDefinition> Trigrams, BalanceDefinition[] Balance,
    HexagramDefinition[] Hexagrams)
{
    public const int TrigramCount = 8;
    public const int HexagramCount = TrigramCount * TrigramCount;

    /// <summary>Balance has an entry for each count of yang lines, none to six.</summary>
    public const int BalanceCount = 7;

    public static readonly string[] Operators = [Emit, Transfer, Discharge, Front];

    public const string Emit = "emit";
    public const string Transfer = "transfer";
    public const string Discharge = "discharge";
    public const string Front = "front";

    public static BaguaGrammar Read(string json, ChemistryPack pack, string path)
    {
        ArgumentNullException.ThrowIfNull(json);
        BaguaGrammar grammar;
        try
        {
            grammar = JsonSerializer.Deserialize(json, BaguaJson.Default.BaguaGrammar) ?? throw new ChemistryContentException(path, "empty grammar");
        }
        catch (JsonException error)
        {
            throw new ChemistryContentException(path, error.Message);
        }

        grammar.Check(pack, path);
        return grammar;
    }

    /// <summary>
    /// Eight trigrams with distinct lines, an inner effect each naming the pack's quantities, a balance per yang count,
    /// and each of the 64 pairs named once.
    /// </summary>
    private void Check(ChemistryPack pack, string path)
    {
        Formula.Require(Trigrams.Count == TrigramCount, path, $"the grammar needs {TrigramCount} trigrams");
        Formula.Require(Trigrams.Values.Select(trigram => trigram.Lines).Distinct().Count() == TrigramCount, path, "two trigrams share their lines");
        foreach ((string id, TrigramDefinition trigram) in Trigrams)
        {
            Formula.Require(trigram.Lines.Length == 3 && trigram.Lines.All(line => line is '0' or '1'), path,
                $"trigram '{id}': lines are three of 0 (yin) and 1 (yang), bottom first");
            Formula.Require(trigram.Inner.Length > 0, path, $"trigram '{id}' has no inner effect");
            foreach (InnerEffect effect in trigram.Inner)
            {
                Formula.Require(Operators.Contains(effect.Operator), path, $"trigram '{id}': unknown operator '{effect.Operator}'");
                pack.Quantity(effect.Quantity);
            }

            OuterDelivery outer = trigram.Outer;
            Formula.Require(outer.Anchor is FormulaStep.Hit or FormulaStep.Above, path, $"trigram '{id}': anchor is hit or above");
            Formula.Require(outer.Radius > 0f && outer.Reach > 0f && outer.Seconds > 0f && outer.Repeats > 0 && outer.FrontSpeed > 0f, path,
                $"trigram '{id}': radius, reach, seconds, repeats and frontSpeed are positive");
        }

        Formula.Require(Balance.Length == BalanceCount, path, $"balance needs {BalanceCount} entries, for none to six yang lines");
        Formula.Require(Hexagrams.Length == HexagramCount, path, $"the grammar names all {HexagramCount} hexagrams");
        HashSet<(string, string)> pairs = [];
        HashSet<int> numbers = [];
        foreach (HexagramDefinition hexagram in Hexagrams)
        {
            Formula.Require(Trigrams.ContainsKey(hexagram.Upper) && Trigrams.ContainsKey(hexagram.Lower), path,
                $"hexagram {hexagram.Number}: unknown trigram");
            Formula.Require(pairs.Add((hexagram.Upper, hexagram.Lower)), path, $"hexagram {hexagram.Number}: its pair is named twice");
            Formula.Require(hexagram.Number is >= 1 and <= HexagramCount && numbers.Add(hexagram.Number), path,
                $"hexagram {hexagram.Number}: numbers run 1 to {HexagramCount}, once each");
        }
    }

    public HexagramDefinition Hexagram(string upper, string lower) =>
        Hexagrams.FirstOrDefault(hexagram => hexagram.Upper == upper && hexagram.Lower == lower)
        ?? throw new ArgumentException($"no hexagram of '{upper}' over '{lower}'");

    public HexagramDefinition Hexagram(int number) =>
        Hexagrams.FirstOrDefault(hexagram => hexagram.Number == number) ?? throw new ArgumentException($"no hexagram {number}");

    /// <summary>Compiles a hexagram into a formula that casts without the grammar.</summary>
    public Formula Compile(HexagramDefinition hexagram)
    {
        ArgumentNullException.ThrowIfNull(hexagram);
        TrigramDefinition upper = Trigrams[hexagram.Upper], lower = Trigrams[hexagram.Lower];
        OuterDelivery outer = upper.Outer;
        int yang = (upper.Lines + lower.Lines).Count(line => line == '1');
        BalanceDefinition balance = Balance[yang];
        List<FormulaStep> steps = [];
        foreach (InnerEffect effect in lower.Inner)
        {
            float amount = effect.Amount * outer.Strength;
            switch (effect.Operator)
            {
                case Emit:
                    steps.Add(new FormulaStep(outer.Anchor, Emit: new EmitEffect(effect.Quantity, outer.Radius, amount * balance.Rate,
                        outer.Seconds, outer.Gather + balance.Gather)));
                    break;
                case Transfer:
                    // A transfer adds nothing: it moves what is there, as fast as its amount, inward for yin and outward for yang.
                    steps.Add(new FormulaStep(outer.Anchor, Emit: new EmitEffect(effect.Quantity, outer.Radius, 0f, outer.Seconds,
                        outer.Gather + (amount * balance.Gather))));
                    break;
                case Discharge:
                    int cells = (int)(outer.Reach * outer.Reach * outer.Reach);
                    Repeat(steps, outer, () => new FormulaStep(FormulaStep.Hit, Discharge: new DischargeEffect(effect.Quantity,
                        amount * balance.Strength, effect.MinConductance, cells)));
                    break;
                case Front:
                    Repeat(steps, outer, () => new FormulaStep(FormulaStep.Above, Front: new FrontEffect(effect.Quantity,
                        amount * balance.Strength, outer.FrontSpeed, 2f / outer.Reach, outer.Reach)));
                    break;
            }
        }

        return new Formula(hexagram.Number.ToString(CultureInfo.InvariantCulture), $"{hexagram.Number} {hexagram.Name}", [.. steps],
            outer.Delay, $"{upper.Glyph} {hexagram.Upper} over {lower.Glyph} {hexagram.Lower}, {yang} yang: {balance.Label}",
            [hexagram.Upper, hexagram.Lower]);
    }

    private static void Repeat(List<FormulaStep> steps, OuterDelivery outer, Func<FormulaStep> make)
    {
        for (int repeat = 0; repeat < outer.Repeats; repeat++)
        {
            steps.Add(make() with { At = repeat * outer.Seconds / outer.Repeats });
        }
    }
}

/// <summary>A trigram: its three lines (bottom first, 1 yang, 0 yin), what it does as the inner trigram and how it delivers as the outer.</summary>
public sealed record TrigramDefinition(string Lines, string Glyph, string Name, InnerEffect[] Inner, OuterDelivery Outer);

/// <summary>
/// What an inner trigram does through one operator: <c>emit</c> adds <see cref="Amount"/> a second of a quantity,
/// <c>transfer</c> moves what is there at that many cells a second, <c>discharge</c> bursts it through cells conducting
/// at least <see cref="MinConductance"/>, and <c>front</c> sends it out as a wave.
/// </summary>
public sealed record InnerEffect(string Operator, string Quantity, float Amount, float MinConductance = 0.5f);

/// <summary>
/// How an outer trigram delivers: at the struck cell or in front of it, over what radius and reach, how strongly,
/// after what delay, for how long in how many repeats, how fast its fronts run, and its own inward (positive) draw.
/// </summary>
public sealed record OuterDelivery(string Anchor, float Radius, float Strength, float Delay, float Reach, float Seconds, int Repeats,
    float FrontSpeed, float Gather = 0f);

/// <summary>
/// The yin/yang balance for one count of yang lines: <see cref="Rate"/> scales and signs what an emit adds (negative
/// drains), <see cref="Gather"/> signs and scales a transfer and adds to an emit's draw (positive gathers inward), and
/// <see cref="Strength"/> scales discharges and fronts.
/// </summary>
public sealed record BalanceDefinition(string Label, float Rate, float Gather, float Strength);

public sealed record HexagramDefinition(int Number, string Name, string Upper, string Lower);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(BaguaGrammar))]
internal sealed partial class BaguaJson : JsonSerializerContext;
