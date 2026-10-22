using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using ChemistrySandbox.Arena;
using ChemistrySandbox.Overlay;
using ChemistrySandbox.Setup;
using ChemistrySandbox.Tools;
using ChemistrySandbox.Ui;
using ChemistrySandbox.Viewing;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Input;
using RustyChemistry.Content;
using RustyChemistry.Simulation;

namespace ChemistrySandbox;

/// <summary>
/// A voxel arena for trying the chemistry kernel with the physical pack. The field owns every cell's
/// identity and quantities; the Engine draws them as voxels, the overlay shows one quantity, and clicks use
/// the tool in hand. The field steps on the Engine's admitted simulation time, never a clock of its own.
/// Live-debug commands use tools on exact cells and read a cell's quantities, for agents and playtests.
/// </summary>
public sealed class SandboxProduct : IEngineProduct, IDebugCommandModuleSource, IDebugCommandModule
{
    /// <summary>The most field steps one update runs; time beyond them is dropped, not owed.</summary>
    private const int MaxStepsPerUpdate = 3;

    private const double PickDistance = 400;

    private static readonly Color Sky = Color.FromSrgbHex(0x9cc3e0, 1f);

    private static readonly (string Id, Vector3 Toward)[] Winds =
    [
        ("calm", Vector3.Zero), ("north", -Vector3.UnitZ), ("east", Vector3.UnitX), ("south", Vector3.UnitZ), ("west", -Vector3.UnitX),
    ];

    private static readonly KeyboardControl[] ToolKeys =
        [KeyboardControl.Digit1, KeyboardControl.Digit2, KeyboardControl.Digit3, KeyboardControl.Digit4, KeyboardControl.Digit5,
        KeyboardControl.Digit6, KeyboardControl.Digit7, KeyboardControl.Digit8, KeyboardControl.Digit9];

    private readonly IEngineContext engine;
    private readonly SandboxContent setup;
    private readonly ChemistryPack pack;
    private readonly UiStream ui;
    private readonly VoxelMirror voxels;
    private readonly QuantityOverlay overlay;
    private readonly OrbitCamera camera;
    private readonly ToolBelt tools;
    private readonly PhysicalInputState held = new();
    private readonly int[] counts;
    private ChemistryField field;
    private OverlayDefinition? shown;
    private string wind = "calm";
    private bool running = true;
    private bool stepOnce;
    private double owed;
    private double stepMilliseconds;
    private string last = "Pick a tool, then click the arena.";
    private bool started;
    private bool shutdown;

    public SandboxProduct(ProductCreateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        engine = context.Engine;
        (setup, pack) = SandboxContent.Read(context.Content);
        ui = engine.Ui.OpenStream(new UiStreamRequest(SandboxView.Stream, SandboxView.StreamContract));
        voxels = new VoxelMirror(engine, pack, setup.Materials);
        overlay = new QuantityOverlay(engine);
        camera = new OrbitCamera(engine, new Vector3(ArenaLayout.Width / 2f, ArenaLayout.Surface, ArenaLayout.Depth / 2f));
        tools = new ToolBelt(setup.Tools, pack);
        counts = new int[pack.Materials.Length];
        field = ArenaLayout.Build(pack, setup.Seed);
        shown = setup.Overlays.FirstOrDefault();
    }

    public void Start()
    {
        if (shutdown)
        {
            return;
        }

        engine.CameraView.SetBackgroundColor(new(Sky));
        started = true;
        Reload();
    }

    public ProductUpdateResult Update(ProductUpdate update)
    {
        if (!started || shutdown)
        {
            return ProductUpdateResult.None;
        }

        ProductUpdateFacts facts = update.Facts;
        double seconds = facts.FixedDeltaSeconds * facts.AdmittedStepCount;
        held.Consume(update.Input);
        camera.Control(update.Input, held, (float)seconds);
        bool changed = false;
        foreach (ProductInputEvent input in update.Input)
        {
            if (SandboxCommand.From(input) is SandboxCommand command)
            {
                changed |= Apply(command);
            }
            else if (input.Kind == InputEventKind.PointerButton && input.Edge == InputEdge.Pressed
                && input.PointerButton == PointerButton.Primary && input.HasPosition)
            {
                changed |= UseTool(new Vector2(input.X, input.Y));
            }
        }

        for (int key = 0; key < Math.Min(ToolKeys.Length, tools.Tools.Count); key++)
        {
            if (held.Pressed(ToolKeys[key]))
            {
                changed |= tools.Select(tools.Tools[key].Id);
            }
        }

        if (running)
        {
            owed += seconds;
        }

        int steps = 0;
        while ((stepOnce || owed >= field.Settings.StepSeconds) && steps < MaxStepsPerUpdate)
        {
            if (!stepOnce)
            {
                owed -= field.Settings.StepSeconds;
            }

            stepOnce = false;
            StepField();
            steps++;
        }

        owed = Math.Min(owed, field.Settings.StepSeconds);
        if (steps > 0)
        {
            overlay.Refresh(field, voxels.Draw, everything: false);
        }

        if (steps > 0 || changed)
        {
            Publish();
        }

        return ProductUpdateResult.None;
    }

    public void Pause()
    {
    }

    public void Resume()
    {
    }

    public void Restart()
    {
        if (shutdown)
        {
            return;
        }

        started = true;
        camera.Reset();
        Reset();
    }

    public void Shutdown() => shutdown = true;

    public void RegisterDebugCommands(IDebugCommandModuleRegistrar registrar) => registrar.Register(this);

    [DebugCommand("sandbox.use", Description = "Use a tool on a cell, as a click on its top face would: sandbox.use torch 13 4 43")]
    public string UseAt(string tool, int x, int y, int z)
    {
        if (!tools.Select(tool))
        {
            return $"unknown tool '{tool}'; tools: {string.Join(", ", tools.Tools.Select(known => known.Id))}";
        }

        Cell cell = new(x, y, z);
        last = tools.Use(field, cell, cell.Offset(0, 1, 0));
        Publish();
        return last;
    }

    [DebugCommand("sandbox.cell", Description = "A cell's material, sky exposure and every quantity: sandbox.cell 13 4 43")]
    public string CellFacts(int x, int y, int z)
    {
        Cell cell = new(x, y, z);
        if (!field.IsLoaded(cell))
        {
            return "outside the arena";
        }

        IEnumerable<string> quantities = pack.Quantities.Select((quantity, index) =>
            string.Create(CultureInfo.InvariantCulture, $"{quantity.Id}={field.Quantity(cell, index):0.###}"));
        return $"{pack.Materials[field.MaterialAt(cell)].Id} open={field.IsOpen(cell)} {string.Join(" ", quantities)}";
    }

    [DebugCommand("sandbox.stats", Description = "The field's step, activity, cost and material counts")]
    public string Stats() => string.Create(CultureInfo.InvariantCulture,
        $"step={field.StepIndex} active={field.ActiveChunkCount}/{field.ChunkCount} fronts={field.ActiveFrontCount} stepMs={stepMilliseconds:0.00} ")
        + string.Join(" ", setup.Materials.Keys.Select(id => $"{id}={counts[pack.Material(id)]}"));

    public void Dispose()
    {
        shutdown = true;
        overlay.Dispose();
        voxels.Dispose();
        camera.Dispose();
        ui.Dispose();
    }

    private bool Apply(SandboxCommand command)
    {
        switch (command.Name)
        {
            case "tool" when command.Id is string id:
                return tools.Select(id);
            case "overlay":
                shown = setup.Overlays.FirstOrDefault(overlay => overlay.Quantity == command.Id);
                overlay.Show(shown, shown is null ? 0 : pack.Quantity(shown.Quantity));
                overlay.Refresh(field, voxels.Draw, everything: true);
                return true;
            case "wind" when Winds.Any(choice => choice.Id == command.Id):
                wind = command.Id!;
                field.Drift = Winds.Single(choice => choice.Id == wind).Toward * setup.WindSpeed;
                return true;
            case "run":
                running = !running;
                owed = 0;
                return true;
            case "step":
                stepOnce = true;
                return true;
            case "reset":
                Reset();
                return true;
            default:
                return false;
        }
    }

    private bool UseTool(Vector2 cursor)
    {
        CameraRay ray = camera.Ray(cursor);
        SpatialHit hit = engine.Spatial.PickVoxel(new(voxels.Session, ray.Origin, ray.Direction, PickDistance));
        if (!hit.Present || hit.Kind != SpatialHitKind.Voxel)
        {
            return false;
        }

        Cell cell = new((int)hit.VoxelX, (int)hit.VoxelY, (int)hit.VoxelZ);
        Cell above = hit.Face switch
        {
            SpatialFace.PosX => cell.Offset(1, 0, 0),
            SpatialFace.NegX => cell.Offset(-1, 0, 0),
            SpatialFace.NegY => cell.Offset(0, -1, 0),
            SpatialFace.PosZ => cell.Offset(0, 0, 1),
            SpatialFace.NegZ => cell.Offset(0, 0, -1),
            _ => cell.Offset(0, 1, 0),
        };
        last = tools.Use(field, cell, above);
        return true;
    }

    private void StepField()
    {
        long began = Stopwatch.GetTimestamp();
        field.Step();
        stepMilliseconds = (stepMilliseconds * 0.9) + (Stopwatch.GetElapsedTime(began).TotalMilliseconds * 0.1);
        IReadOnlyList<MaterialChange> changes = field.MaterialChanges;
        voxels.Apply(changes);
        foreach (MaterialChange change in changes)
        {
            counts[change.From]--;
            counts[change.To]++;
            if (pack.Materials[change.From].Sky != pack.Materials[change.To].Sky)
            {
                field.MarkOpenColumn(change.Cell.X, change.Cell.Z);
            }
        }
    }

    private void Reset()
    {
        field = ArenaLayout.Build(pack, setup.Seed);
        field.Drift = Winds.Single(choice => choice.Id == wind).Toward * setup.WindSpeed;
        owed = 0;
        last = "The arena is reset.";
        Reload();
    }

    /// <summary>Draws a fresh field: every voxel, the overlay from scratch, and the material counts.</summary>
    private void Reload()
    {
        voxels.Load(field);
        overlay.Show(shown, shown is null ? 0 : pack.Quantity(shown.Quantity));
        overlay.Refresh(field, voxels.Draw, everything: true);
        Array.Clear(counts);
        ushort[] materials = new ushort[ChemistryField.ChunkSize * ChemistryField.ChunkSize * ChemistryField.ChunkSize];
        foreach (Cell origin in field.LoadedChunks())
        {
            field.ReadChunkMaterials(origin, materials);
            foreach (ushort material in materials)
            {
                counts[material]++;
            }
        }

        Publish();
    }

    private void Publish()
    {
        SandboxView view = new(
            [.. tools.Tools.Select(tool => (tool.Id, tool.Label))],
            tools.Current.Id,
            [.. setup.Overlays.Select(overlay => (overlay.Quantity, overlay.Label))],
            shown?.Quantity ?? "none",
            [.. Winds.Select(choice => choice.Id)],
            wind,
            running,
            field.StepIndex,
            field.ActiveChunkCount,
            field.ChunkCount,
            field.ActiveFrontCount,
            stepMilliseconds,
            overlay.Drawn.Cubes,
            [.. setup.Materials.Keys.Select(id => (id, counts[pack.Material(id)]))],
            last);
        engine.Ui.PublishProjection(new UiProjection(ui, view.ToUiValue()));
    }
}
