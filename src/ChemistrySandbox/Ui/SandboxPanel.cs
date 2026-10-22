using System.Text.Json;
using System.Text.Json.Nodes;
using ChemistrySandbox.Arena;
using Rusty.Engine;

namespace ChemistrySandbox.Ui;

/// <summary>
/// A command from the DOM panel, claimed as the <c>sandbox.command</c> intent with a
/// <c>rusty.chemistry.command.v1</c> payload: <see cref="Name"/> and the id it chose, if any.
/// </summary>
internal readonly record struct SandboxCommand(string Name, string? Id)
{
    /// <summary>Reads a panel command, or nothing for any other input.</summary>
    internal static SandboxCommand? From(in ProductInputEvent input)
    {
        if (input.Kind != InputEventKind.DirectProductPayload || !input.Intent.Span.SequenceEqual("sandbox.command"u8)
            || !input.PayloadContract.Span.SequenceEqual("rusty.chemistry.command.v1"u8))
        {
            return null;
        }

        using JsonDocument payload = JsonDocument.Parse(input.PayloadData);
        JsonElement root = payload.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("command", out JsonElement name)
            || name.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? id = root.TryGetProperty("id", out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return new SandboxCommand(name.GetString()!, id);
    }
}

/// <summary>One choice the panel offers; a channel also carries its trigrams and whether it is vivid now.</summary>
internal readonly record struct SandboxChoice(string Id, string Label, string? Detail = null, bool? Vivid = null);

/// <summary>What the panel shows: the choices, what is chosen, the symptoms, and how the field is doing.</summary>
internal sealed record SandboxView(
    IReadOnlyList<SandboxChoice> Scenes,
    string Scene,
    IReadOnlyList<SandboxChoice> Tools,
    string Tool,
    IReadOnlyList<SandboxChoice> Overlays,
    string Overlay,
    IReadOnlyList<SandboxChoice> Winds,
    string Wind,
    IReadOnlyList<SandboxChoice> Emperors,
    string? Emperor,
    IReadOnlyList<SandboxChoice> Muted,
    string? MutedChoice,
    bool Running,
    long Step,
    int ActiveChunks,
    int Chunks,
    int Fronts,
    int Pending,
    double StepMilliseconds,
    int OverlayCubes,
    IReadOnlyList<(string Material, int Cells)> Counts,
    IReadOnlyList<SymptomReading> Symptoms,
    string Last)
{
    internal const string Stream = "chemistry-sandbox";
    internal const string StreamContract = "rusty.chemistry.sandbox";

    internal UiValue ToUiValue()
    {
        JsonObject view = new()
        {
            ["scenes"] = Choices(Scenes),
            ["scene"] = Scene,
            ["tools"] = Choices(Tools),
            ["tool"] = Tool,
            ["overlays"] = Choices(Overlays),
            ["overlay"] = Overlay,
            ["winds"] = Choices(Winds),
            ["wind"] = Wind,
            ["emperors"] = Choices(Emperors),
            ["emperor"] = Emperor,
            ["muteds"] = Choices(Muted),
            ["muted"] = MutedChoice,
            ["running"] = Running,
            ["step"] = Step,
            ["activeChunks"] = ActiveChunks,
            ["chunks"] = Chunks,
            ["fronts"] = Fronts,
            ["pending"] = Pending,
            ["stepMs"] = Math.Round(StepMilliseconds, 2),
            ["overlayCubes"] = OverlayCubes,
            ["counts"] = new JsonArray([.. Counts.Select(count => (JsonNode?)new JsonObject { ["material"] = count.Material, ["cells"] = count.Cells })]),
            ["symptoms"] = new JsonArray([.. Symptoms.Select(symptom => (JsonNode?)new JsonObject
            {
                ["name"] = symptom.Name,
                ["medium"] = symptom.Medium,
                ["level"] = Math.Round(symptom.Level, 2),
                ["healthy"] = symptom.Healthy,
                ["jammed"] = symptom.Jammed,
                ["gears"] = symptom.Gears,
            })]),
            ["last"] = Last,
        };
        return UiValues.FromJson(view);
    }

    private static JsonArray Choices(IReadOnlyList<SandboxChoice> choices) => new([.. choices.Select(choice =>
    {
        JsonObject item = new() { ["id"] = choice.Id, ["label"] = choice.Label };
        if (choice.Detail is string detail)
        {
            item["detail"] = detail;
        }

        if (choice.Vivid is bool vivid)
        {
            item["vivid"] = vivid;
        }

        return (JsonNode?)item;
    })]);
}
