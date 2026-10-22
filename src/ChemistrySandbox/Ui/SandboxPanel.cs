using System.Text.Json;
using System.Text.Json.Nodes;
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

/// <summary>What the panel shows: the choices, what is chosen, and how the field is doing.</summary>
internal sealed record SandboxView(
    IReadOnlyList<(string Id, string Label)> Tools,
    string Tool,
    IReadOnlyList<(string Id, string Label)> Overlays,
    string Overlay,
    IReadOnlyList<string> Winds,
    string Wind,
    bool Running,
    long Step,
    int ActiveChunks,
    int Chunks,
    int Fronts,
    double StepMilliseconds,
    int OverlayCubes,
    IReadOnlyList<(string Material, int Cells)> Counts,
    string Last)
{
    internal const string Stream = "chemistry-sandbox";
    internal const string StreamContract = "rusty.chemistry.sandbox";

    internal UiValue ToUiValue()
    {
        JsonObject view = new()
        {
            ["tools"] = Choices(Tools),
            ["tool"] = Tool,
            ["overlays"] = Choices(Overlays),
            ["overlay"] = Overlay,
            ["winds"] = new JsonArray([.. Winds.Select(wind => (JsonNode?)JsonValue.Create(wind))]),
            ["wind"] = Wind,
            ["running"] = Running,
            ["step"] = Step,
            ["activeChunks"] = ActiveChunks,
            ["chunks"] = Chunks,
            ["fronts"] = Fronts,
            ["stepMs"] = Math.Round(StepMilliseconds, 2),
            ["overlayCubes"] = OverlayCubes,
            ["counts"] = new JsonArray([.. Counts.Select(count => (JsonNode?)new JsonObject { ["material"] = count.Material, ["cells"] = count.Cells })]),
            ["last"] = Last,
        };
        return UiValues.FromJson(view);
    }

    private static JsonArray Choices(IReadOnlyList<(string Id, string Label)> choices) =>
        new([.. choices.Select(choice => (JsonNode?)new JsonObject { ["id"] = choice.Id, ["label"] = choice.Label })]);
}
