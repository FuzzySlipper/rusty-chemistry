using System.Globalization;
using ChemistrySandbox.Casting;
using RustyChemistry.Content;
using RustyChemistry.Simulation;

namespace ChemistrySandbox.Channels;

/// <summary>
/// A hexagram cast as its three layers compose (spec §7.6), one rule for every channel:
/// <list type="bullet">
/// <item>the element trigram picks the operator and the quantity it acts through (Layer 1);</item>
/// <item>the gravity trigram and its position set radius, strength, delay, reach and inward or outward draw (Layer 2);</item>
/// <item>the governance slots set motion, which medium is targeted, and the tempo (Layer 3).</item>
/// </list>
/// Only the transfer operator reads the target slot, since it is the only one acting on a medium; the plan
/// summary says which slots each cast used.
/// </summary>
internal sealed class ChannelTool(ChannelDefinition channel, ChannelContent content, ChemistryPack pack) : ICastTool
{
    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["heaven"] = "☰", ["earth"] = "☷", ["thunder"] = "☳", ["water"] = "☵",
        ["mountain"] = "☶", ["wind"] = "☴", ["fire"] = "☲", ["lake"] = "☱",
    };

    public string Id { get; } = channel.Hex.ToString(CultureInfo.InvariantCulture);

    public string Label { get; } = $"{channel.Hex} {channel.Name}";

    public string? Detail { get; } = $"{Glyphs[channel.Upper]} {channel.Upper} over {Glyphs[channel.Lower]} {channel.Lower}";

    public IReadOnlyList<string> Trigrams { get; } = [channel.Upper, channel.Lower];

    public CastPlan Plan(ChemistryField field, Cell hit, Cell above, CastFeel feel)
    {
        (string deliveryId, string elementId) = channel.Composition;
        ElementProfile element = content.Elements[elementId];
        DeliveryProfile delivery = content.Deliveries[deliveryId];
        Governance governance = channel.Governance;
        TempoProfile tempo = content.Tempos[governance.Tempo];
        float direction = governance.Motion == "toward" ? 1f : -1f;
        float radius = delivery.Radius * feel.Radius;
        float strength = delivery.Strength * feel.Strength * tempo.Level;
        // What the cast moves is drawn inward or outward by its delivery and pushed by its motion.
        float draw = delivery.Gather + (direction * content.MotionSpeed);
        EmitterShape shape = EmitterShape.Sphere(above.Center, radius);
        List<(float, CastOperation)> operations = [];
        string acts;
        switch (element.Operator)
        {
            case "transfer":
                string medium = Target(field, above, radius, governance.Target);
                float current = (direction * element.Amount * strength) + delivery.Gather;
                operations.Add((0f, new EmitOperation(new EmitterSpec(shape, pack.Quantity(medium), 0f, current, tempo.Seconds))));
                acts = $"moves {governance.Target} {medium} {(current >= 0 ? "inward" : "outward")} at {Math.Abs(current):0.#} cells/s";
                break;
            case "discharge":
                int cells = (int)(delivery.Reach * delivery.Reach * delivery.Reach * feel.Radius);
                Repeat(operations, tempo, () => new DischargeOperation(hit, pack.Quantity(element.Quantity!), element.Amount * strength, 0.5f, cells));
                acts = $"discharges {element.Amount * strength:0.#} {element.Quantity} through up to {cells} conducting cells";
                break;
            case "propagate":
                float reach = delivery.Reach * feel.Radius;
                Repeat(operations, tempo, () => new FrontOperation(above, pack.Quantity(element.Quantity!), element.Amount * strength,
                    tempo.FrontSpeed, 2f / delivery.Reach, reach));
                acts = $"sends a {element.Quantity} front of {element.Amount * strength:0.#} out to {reach:0.#} cells";
                break;
            default:
                // transform, confine and bind each hold their quantity over the delivery's shape.
                float rate = 2f * element.Amount * strength;
                operations.Add((0f, new EmitOperation(new EmitterSpec(shape, pack.Quantity(element.Quantity!), rate, draw, tempo.Seconds))));
                acts = $"{element.Operator}s with {element.Quantity} at {rate:0.#}/s, drawn {(draw >= 0 ? "inward" : "outward")} {Math.Abs(draw):0.#} cells/s";
                break;
        }

        string summary = string.Create(CultureInfo.InvariantCulture,
            $"{elementId} via {deliveryId}, r{radius:0.#} ×{strength:0.##}, {governance.Motion}, {governance.Tempo} {tempo.Seconds:0.#}s: {acts}");
        return new CastPlan(operations, delivery.Delay, summary);
    }

    /// <summary>The medium furthest above (excess) or below (deficit) its ambient around the cell, summed over the sphere.</summary>
    private string Target(ChemistryField field, Cell center, float radius, string target)
    {
        int reach = (int)MathF.Ceiling(radius);
        Dictionary<string, float> deviation = content.Media.ToDictionary(medium => medium, _ => 0f);
        for (int z = -reach; z <= reach; z++)
        {
            for (int y = -reach; y <= reach; y++)
            {
                for (int x = -reach; x <= reach; x++)
                {
                    Cell cell = center.Offset(x, y, z);
                    if ((x * x) + (y * y) + (z * z) > radius * radius || !field.IsLoaded(cell))
                    {
                        continue;
                    }

                    foreach (string medium in content.Media)
                    {
                        int quantity = pack.Quantity(medium);
                        deviation[medium] += field.Quantity(cell, quantity) - field.Ambient(quantity);
                    }
                }
            }
        }

        return target == "excess"
            ? deviation.MaxBy(pair => pair.Value).Key
            : deviation.MinBy(pair => pair.Value).Key;
    }

    private static void Repeat(List<(float, CastOperation)> operations, TempoProfile tempo, Func<CastOperation> make)
    {
        for (int repeat = 0; repeat < tempo.Repeats; repeat++)
        {
            operations.Add((repeat * tempo.Seconds / tempo.Repeats, make()));
        }
    }
}
