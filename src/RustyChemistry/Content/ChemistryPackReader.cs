using System.Collections.Immutable;
using System.Text.Json;

namespace RustyChemistry.Content;

/// <summary>
/// Reads a pack's JSON strictly: every property known, every id resolved, every bound in order. Comments
/// and trailing commas are allowed, since packs are hand-authored.
/// </summary>
internal sealed class ChemistryPackReader
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private readonly Dictionary<string, int> quantityIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> materialIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> ruleIds = new(StringComparer.Ordinal);
    private readonly List<string> modulators = [];
    private readonly List<QuantityDefinition> quantities = [];

    private ChemistryPackReader()
    {
    }

    internal static ChemistryPack Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, Options);
        }
        catch (JsonException error)
        {
            throw new ChemistryContentException("$", error.Message);
        }

        using (document)
        {
            return new ChemistryPackReader().Pack(new Node(document.RootElement, "$"));
        }
    }

    private ChemistryPack Pack(Node root)
    {
        Object(root, "quantities", "materials", "reactions", "transforms");

        // Ids first, so a quantity's transfer can name a quantity declared after it.
        Node[] quantityNodes = NonEmpty(Required(root, "quantities"));
        foreach (Node node in quantityNodes)
        {
            Object(node, "id", "ambient", "min", "max", "settle", "epsilon", "transfer");
            Declare(quantityIds, Required(node, "id"), quantityIds.Count, "quantity");
        }

        foreach (Node node in quantityNodes)
        {
            quantities.Add(Quantity(node));
        }

        Node[] materialNodes = NonEmpty(Required(root, "materials"));
        foreach (Node node in materialNodes)
        {
            Object(node, "id", "sky", "conduct", "initial", "rest");
            Declare(materialIds, Required(node, "id"), materialIds.Count, "material");
        }

        ImmutableArray<MaterialDefinition> materials = [.. materialNodes.Select(Material)];
        ImmutableArray<ReactionRule> reactions = Optional(root, "reactions") is Node reactionList
            ? [.. Items(reactionList).Select(Reaction)]
            : [];
        ImmutableArray<TransformRule> transforms = Optional(root, "transforms") is Node transformList
            ? [.. Items(transformList).Select(Transform)]
            : [];
        return new ChemistryPack([.. quantities], materials, reactions, transforms, [.. modulators]);
    }

    private QuantityDefinition Quantity(Node node)
    {
        string id = Text(Required(node, "id"));
        float ambient = OptionalNumber(node, "ambient") ?? 0f;
        float min = OptionalNumber(node, "min") ?? 0f;
        float max = OptionalNumber(node, "max") ?? float.MaxValue;
        if (min > max)
        {
            throw Error(node, $"min {min} is above max {max}");
        }

        if (ambient < min || ambient > max)
        {
            throw Error(node, $"ambient {ambient} is outside {min}..{max}");
        }

        float settle = OptionalNumber(node, "settle") ?? 0f;
        if (settle < 0f)
        {
            throw Error(node, "settle must not be negative");
        }

        float epsilon = OptionalNumber(node, "epsilon") ?? 1e-4f;
        if (epsilon <= 0f)
        {
            throw Error(node, "epsilon must be positive");
        }

        TransferDefinition? transfer = null;
        if (Optional(node, "transfer") is Node transferNode)
        {
            Object(transferNode, "rate", "drift", "rise", "blockedBy", "modulator");
            float rate = Number(Required(transferNode, "rate"));
            if (rate < 0f)
            {
                throw Error(transferNode, "rate must not be negative");
            }

            transfer = new TransferDefinition(rate, Optional(transferNode, "drift") is Node drift && Flag(drift),
                OptionalQuantity(transferNode, "blockedBy"), OptionalModulator(transferNode),
                OptionalNumber(transferNode, "rise") ?? 0f);
        }

        return new QuantityDefinition(id, ambient, min, max, settle, epsilon, transfer);
    }

    private MaterialDefinition Material(Node node)
    {
        string id = Text(Required(node, "id"));
        float[] conductance = new float[quantities.Count];
        float?[] initial = new float?[quantities.Count];
        if (Optional(node, "conduct") is Node conduct)
        {
            foreach ((int quantity, Node value) in QuantityEntries(conduct))
            {
                float amount = Number(value);
                if (amount is < 0f or > 1f)
                {
                    throw Error(value, "conductance must be within 0..1");
                }

                conductance[quantity] = amount;
            }
        }

        if (Optional(node, "initial") is Node initialNode)
        {
            foreach ((int quantity, Node value) in QuantityEntries(initialNode))
            {
                initial[quantity] = InBounds(value, quantity);
            }
        }

        RestDefinition?[] rest = new RestDefinition?[quantities.Count];
        if (Optional(node, "rest") is Node restNode)
        {
            foreach ((int quantity, Node value) in QuantityEntries(restNode))
            {
                Object(value, "to", "settle");
                float settle = OptionalNumber(value, "settle") ?? quantities[quantity].Settle;
                if (settle < 0f)
                {
                    throw Error(value, "settle must not be negative");
                }

                rest[quantity] = new RestDefinition(InBounds(Required(value, "to"), quantity), settle);
            }
        }

        return new MaterialDefinition(id, Optional(node, "sky") is Node sky && Flag(sky), [.. conductance], [.. initial], [.. rest]);
    }

    private ReactionRule Reaction(Node node)
    {
        Object(node, "id", "in", "when", "rate", "per", "consumes", "produces", "modulator");
        string id = RuleId(node);
        ImmutableArray<int> materials = Optional(node, "in") is Node list ? MaterialList(list) : [];
        ImmutableArray<Condition> conditions = Optional(node, "when") is Node when ? [.. Items(when).Select(Condition)] : [];
        float rate = Number(Required(node, "rate"));
        if (rate <= 0f)
        {
            throw Error(node, "rate must be positive");
        }

        ImmutableArray<QuantityAmount> consumes = Optional(node, "consumes") is Node consumed ? Amounts(consumed) : [];
        ImmutableArray<QuantityAmount> produces = Optional(node, "produces") is Node produced ? Amounts(produced) : [];
        if (consumes.IsEmpty && produces.IsEmpty)
        {
            throw Error(node, "a reaction must consume or produce something");
        }

        return new ReactionRule(id, materials, conditions, rate, OptionalQuantity(node, "per"), consumes, produces, OptionalModulator(node));
    }

    private TransformRule Transform(Node node)
    {
        Object(node, "id", "from", "to", "when", "chance", "resistedBy", "modulator", "set");
        string id = RuleId(node);
        ImmutableArray<int> from = MaterialList(Required(node, "from"));
        Node toNode = Required(node, "to");
        int to = MaterialRef(toNode);
        if (from.Contains(to))
        {
            throw Error(toNode, "a transform must change identity: 'to' is also in 'from'");
        }

        ImmutableArray<Condition> conditions = [.. NonEmpty(Required(node, "when")).Select(Condition)];
        float? chance = OptionalNumber(node, "chance");
        if (chance <= 0f)
        {
            throw Error(node, "chance must be positive");
        }

        int? resistedBy = OptionalQuantity(node, "resistedBy");
        int? modulator = OptionalModulator(node);
        if (chance is null && (resistedBy is not null || modulator is not null))
        {
            throw Error(node, "resistedBy and modulator scale a chance; an immediate transform has none");
        }

        ImmutableArray<QuantityAmount> set = [];
        if (Optional(node, "set") is Node setNode)
        {
            set = [.. QuantityEntries(setNode).Select(entry => new QuantityAmount(entry.Quantity, InBounds(entry.Value, entry.Quantity)))];
        }

        return new TransformRule(id, from, to, conditions, chance, resistedBy, modulator, set);
    }

    private Condition Condition(Node node)
    {
        Object(node, "quantity", "atLeast", "atMost", "open");
        if (Optional(node, "open") is Node open)
        {
            if (node.Value.EnumerateObject().Count() != 1)
            {
                throw Error(node, "an 'open' condition stands alone");
            }

            return new Condition(Flag(open) ? ConditionKind.Open : ConditionKind.Closed, 0, 0f);
        }

        int quantity = QuantityRef(Required(node, "quantity"));
        float? atLeast = OptionalNumber(node, "atLeast");
        float? atMost = OptionalNumber(node, "atMost");
        return (atLeast, atMost) switch
        {
            (float threshold, null) => new Condition(ConditionKind.AtLeast, quantity, threshold),
            (null, float threshold) => new Condition(ConditionKind.AtMost, quantity, threshold),
            _ => throw Error(node, "a quantity condition has exactly one of 'atLeast' or 'atMost'"),
        };
    }

    private ImmutableArray<QuantityAmount> Amounts(Node node) =>
    [
        .. QuantityEntries(node).Select(entry =>
        {
            float amount = Number(entry.Value);
            return amount > 0f ? new QuantityAmount(entry.Quantity, amount) : throw Error(entry.Value, "amounts must be positive");
        }),
    ];

    private float InBounds(Node node, int quantity)
    {
        float value = Number(node);
        QuantityDefinition definition = quantities[quantity];
        return value >= definition.Min && value <= definition.Max
            ? value
            : throw Error(node, $"{value} is outside {definition.Id}'s {definition.Min}..{definition.Max}");
    }

    private string RuleId(Node node)
    {
        Node idNode = Required(node, "id");
        string id = Text(idNode);
        return ruleIds.Add(id) ? id : throw Error(idNode, $"a rule '{id}' is already declared");
    }

    private ImmutableArray<int> MaterialList(Node node)
    {
        ImmutableArray<int> materials = [.. NonEmpty(node).Select(MaterialRef)];
        return materials.Distinct().Count() == materials.Length ? materials : throw Error(node, "a material is listed twice");
    }

    private IEnumerable<(int Quantity, Node Value)> QuantityEntries(Node node)
    {
        Object(node);
        foreach (JsonProperty property in node.Value.EnumerateObject())
        {
            Node value = new(property.Value, $"{node.Path}.{property.Name}");
            yield return quantityIds.TryGetValue(property.Name, out int quantity)
                ? (quantity, value)
                : throw Error(value, $"unknown quantity '{property.Name}'");
        }
    }

    private int QuantityRef(Node node) =>
        quantityIds.TryGetValue(Text(node), out int quantity) ? quantity : throw Error(node, $"unknown quantity '{Text(node)}'");

    private int MaterialRef(Node node) =>
        materialIds.TryGetValue(Text(node), out int material) ? material : throw Error(node, $"unknown material '{Text(node)}'");

    private int? OptionalQuantity(Node node, string name) => Optional(node, name) is Node value ? QuantityRef(value) : null;

    private int? OptionalModulator(Node node)
    {
        if (Optional(node, "modulator") is not Node value)
        {
            return null;
        }

        string name = Text(value);
        int index = modulators.IndexOf(name);
        if (index < 0)
        {
            modulators.Add(name);
            index = modulators.Count - 1;
        }

        return index;
    }

    private static void Declare(Dictionary<string, int> ids, Node node, int index, string kind)
    {
        string id = Text(node);
        if (!ids.TryAdd(id, index))
        {
            throw Error(node, $"a {kind} '{id}' is already declared");
        }
    }

    private static void Object(Node node, params string[] known)
    {
        if (node.Value.ValueKind != JsonValueKind.Object)
        {
            throw Error(node, "expected an object");
        }

        if (known.Length == 0)
        {
            return;
        }

        foreach (JsonProperty property in node.Value.EnumerateObject())
        {
            if (Array.IndexOf(known, property.Name) < 0)
            {
                throw new ChemistryContentException($"{node.Path}.{property.Name}",
                    $"unknown property (expected one of: {string.Join(", ", known)})");
            }
        }
    }

    private static Node? Optional(Node node, string name) =>
        node.Value.TryGetProperty(name, out JsonElement value) ? new Node(value, $"{node.Path}.{name}") : null;

    private static Node Required(Node node, string name) => Optional(node, name) ?? throw Error(node, $"missing '{name}'");

    private static float? OptionalNumber(Node node, string name) => Optional(node, name) is Node value ? Number(value) : null;

    private static Node[] Items(Node node)
    {
        if (node.Value.ValueKind != JsonValueKind.Array)
        {
            throw Error(node, "expected an array");
        }

        return [.. node.Value.EnumerateArray().Select((item, index) => new Node(item, $"{node.Path}[{index}]"))];
    }

    private static Node[] NonEmpty(Node node)
    {
        Node[] items = Items(node);
        return items.Length > 0 ? items : throw Error(node, "must not be empty");
    }

    private static float Number(Node node)
    {
        if (node.Value.ValueKind != JsonValueKind.Number)
        {
            throw Error(node, "expected a number");
        }

        double value = node.Value.GetDouble();
        return double.IsFinite(value) && Math.Abs(value) <= float.MaxValue ? (float)value : throw Error(node, "out of range");
    }

    private static string Text(Node node)
    {
        string? text = node.Value.ValueKind == JsonValueKind.String ? node.Value.GetString() : null;
        return !string.IsNullOrWhiteSpace(text) ? text : throw Error(node, "expected a non-empty string");
    }

    private static bool Flag(Node node) => node.Value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Error(node, "expected true or false"),
    };

    private static ChemistryContentException Error(Node node, string problem) => new(node.Path, problem);

    private readonly record struct Node(JsonElement Value, string Path);
}
