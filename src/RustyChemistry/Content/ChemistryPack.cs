using System.Collections.Immutable;

namespace RustyChemistry.Content;

/// <summary>
/// A content pack: the quantities cells hold, the materials they are, and the local rules that change them.
/// Immutable once read. Everything that makes fire fire lives here; the field only runs the operators.
/// </summary>
public sealed class ChemistryPack
{
    private readonly Dictionary<string, int> quantityIndex;
    private readonly Dictionary<string, int> materialIndex;
    private readonly Dictionary<string, int> modulatorIndex;

    internal ChemistryPack(ImmutableArray<QuantityDefinition> quantities, ImmutableArray<MaterialDefinition> materials,
        ImmutableArray<ReactionRule> reactions, ImmutableArray<TransformRule> transforms, ImmutableArray<string> modulators)
    {
        Quantities = quantities;
        Materials = materials;
        Reactions = reactions;
        Transforms = transforms;
        Modulators = modulators;
        quantityIndex = Index(quantities.Select(quantity => quantity.Id));
        materialIndex = Index(materials.Select(material => material.Id));
        modulatorIndex = Index(modulators);
    }

    public ImmutableArray<QuantityDefinition> Quantities { get; }

    public ImmutableArray<MaterialDefinition> Materials { get; }

    public ImmutableArray<ReactionRule> Reactions { get; }

    public ImmutableArray<TransformRule> Transforms { get; }

    /// <summary>The modulator names the rules use; a field holds one multiplier for each, 1 until set.</summary>
    public ImmutableArray<string> Modulators { get; }

    /// <summary>Reads a pack from its JSON, reporting the first problem with its path.</summary>
    /// <exception cref="ChemistryContentException">The JSON is malformed or does not describe a valid pack.</exception>
    public static ChemistryPack Read(string json) => ChemistryPackReader.Read(json);

    public int Quantity(string id) => Find(quantityIndex, id, "quantity");

    public int Material(string id) => Find(materialIndex, id, "material");

    public int Modulator(string name) => Find(modulatorIndex, name, "modulator");

    private static int Find(Dictionary<string, int> index, string id, string kind) =>
        index.TryGetValue(id, out int found) ? found : throw new ArgumentException($"the pack has no {kind} '{id}'", nameof(id));

    private static Dictionary<string, int> Index(IEnumerable<string> ids) =>
        ids.Select((id, index) => (id, index)).ToDictionary(entry => entry.id, entry => entry.index, StringComparer.Ordinal);
}

/// <summary>A pack's JSON is malformed or invalid at <see cref="Path"/>.</summary>
public sealed class ChemistryContentException(string path, string problem) : Exception($"{path}: {problem}")
{
    public string Path { get; } = path;

    public string Problem { get; } = problem;
}
