using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using ChemistrySandbox.Arena;
using ChemistrySandbox.Casting;
using ChemistrySandbox.Channels;
using ChemistrySandbox.Overlay;
using ChemistrySandbox.Setup;
using ChemistrySandbox.Ui;
using ChemistrySandbox.Viewing;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Input;
using RustyChemistry.Simulation;

namespace ChemistrySandbox;

/// <summary>
/// A voxel sandbox for trying the chemistry kernel, one scene at a time: the physical arena, or the machine
/// room for wardens-circuit's Q90 channels. The field owns every cell's identity and quantities; the Engine
/// draws them as voxels, the overlay shows one quantity, and clicks cast the tool in hand. The field and
/// the casts in flight advance on the Engine's admitted simulation time, never a clock of their own.
/// Live-debug commands cast on exact cells and read cells and symptoms, for agents and playtests.
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
    private readonly IReadOnlyList<Scene> scenes;
    private readonly UiStream ui;
    private readonly OrbitCamera camera;
    private readonly PhysicalInputState held = new();
    private Scene scene;
    private VoxelMirror voxels;
    private QuantityOverlay overlay;
    private Caster caster;
    private ChemistryField field;
    private IReadOnlyList<Symptom> symptoms;
    private int[] counts;
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
        // Every scene loads now, so a content mistake in any of them fails at start, not on a switch.
        scenes = [.. SceneList.Read(context.Content).Scenes.Select(entry => new Scene(entry, context.Content))];
        scene = scenes[0];
        ui = engine.Ui.OpenStream(new UiStreamRequest(SandboxView.Stream, SandboxView.StreamContract));
        camera = new OrbitCamera(engine, scene.CameraHome);
        voxels = new VoxelMirror(engine, scene.Pack, scene.Content.Materials);
        overlay = new QuantityOverlay(engine);
        caster = new Caster(scene.Tools, scene.Resonance);
        (field, symptoms) = scene.Build();
        counts = new int[scene.Pack.Materials.Length];
        shown = scene.Content.Overlays.FirstOrDefault();
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
                changed |= CastAt(new Vector2(input.X, input.Y));
            }
        }

        for (int key = 0; key < Math.Min(ToolKeys.Length, caster.Tools.Count); key++)
        {
            if (held.Pressed(ToolKeys[key]))
            {
                changed |= caster.Select(caster.Tools[key].Id);
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

    public void Dispose()
    {
        shutdown = true;
        overlay.Dispose();
        voxels.Dispose();
        camera.Dispose();
        ui.Dispose();
    }

    public void RegisterDebugCommands(IDebugCommandModuleRegistrar registrar) => registrar.Register(this);

    [DebugCommand("sandbox.use", Description = "Cast a tool or channel on a cell, as a click on its top face would: sandbox.use 33 8 4 15")]
    public string UseAt(string tool, int x, int y, int z)
    {
        if (!caster.Select(tool))
        {
            return $"unknown tool '{tool}'; tools: {string.Join(", ", caster.Tools.Select(known => known.Id))}";
        }

        Cell cell = new(x, y, z);
        last = caster.Cast(field, cell, cell.Offset(0, 1, 0));
        Publish();
        return last;
    }

    [DebugCommand("sandbox.plan", Description = "What a tool or channel would compose on a cell, without casting: sandbox.plan 44 8 4 15")]
    public string PlanAt(string tool, int x, int y, int z)
    {
        ICastTool? found = caster.Tools.FirstOrDefault(known => known.Id == tool);
        if (found is null)
        {
            return $"unknown tool '{tool}'";
        }

        Cell cell = new(x, y, z);
        CastPlan plan = found.Plan(field, cell, cell.Offset(0, 1, 0), CastFeel.Plain);
        return string.Create(CultureInfo.InvariantCulture,
            $"{found.Label} ({(caster.IsVivid(found) ? "vivid" : "muted")}): {plan.Summary}; lands after {plan.Delay:0.##}s, {plan.Operations.Count} operation(s)");
    }

    [DebugCommand("sandbox.scene", Description = "Switch scene: sandbox.scene q90")]
    public string SceneCommand(string id) => Apply(new SandboxCommand("scene", id)) ? $"scene {scene.Entry.Id}" : $"unknown scene '{id}'";

    [DebugCommand("sandbox.emperor", Description = "Make an Emperor active, in a scene with channels: sandbox.emperor shun")]
    public string EmperorCommand(string id) => Apply(new SandboxCommand("emperor", id))
        ? $"{scene.Resonance!.Emperor.Label}: vivid {string.Join(", ", caster.Tools.Where(caster.IsVivid).Select(tool => tool.Id))}"
        : $"unknown emperor '{id}', or no channels here";

    [DebugCommand("sandbox.cell", Description = "A cell's material, sky exposure and every quantity: sandbox.cell 13 4 43")]
    public string CellFacts(int x, int y, int z)
    {
        Cell cell = new(x, y, z);
        if (!field.IsLoaded(cell))
        {
            return "outside the field";
        }

        IEnumerable<string> quantities = scene.Pack.Quantities.Select((quantity, index) =>
            string.Create(CultureInfo.InvariantCulture, $"{quantity.Id}={field.Quantity(cell, index):0.###}"));
        return $"{scene.Pack.Materials[field.MaterialAt(cell)].Id} open={field.IsOpen(cell)} {string.Join(" ", quantities)}";
    }

    [DebugCommand("sandbox.symptoms", Description = "Each symptom's medium level and jammed gears")]
    public string Symptoms() => symptoms.Count == 0
        ? "no symptoms in this scene"
        : string.Join("; ", symptoms.Select(symptom => symptom.Read(field, scene.Pack).ToString()));

    [DebugCommand("sandbox.stats", Description = "The field's step, activity, cost and material counts")]
    public string Stats() => string.Create(CultureInfo.InvariantCulture,
        $"scene={scene.Entry.Id} step={field.StepIndex} active={field.ActiveChunkCount}/{field.ChunkCount} fronts={field.ActiveFrontCount} pending={caster.Pending} stepMs={stepMilliseconds:0.00} ")
        + string.Join(" ", scene.Content.Materials.Keys.Select(id => $"{id}={counts[scene.Pack.Material(id)]}"));

    private bool Apply(SandboxCommand command)
    {
        switch (command.Name)
        {
            case "tool" when command.Id is string id:
                return caster.Select(id);
            case "overlay":
                shown = scene.Content.Overlays.FirstOrDefault(overlay => overlay.Quantity == command.Id);
                overlay.Show(shown, shown is null ? 0 : scene.Pack.Quantity(shown.Quantity));
                overlay.Refresh(field, voxels.Draw, everything: true);
                return true;
            case "wind" when Winds.Any(choice => choice.Id == command.Id):
                wind = command.Id!;
                field.Drift = Drift();
                return true;
            case "scene" when scenes.FirstOrDefault(known => known.Entry.Id == command.Id) is Scene next:
                Switch(next);
                return true;
            case "emperor" when command.Id is string id:
                return caster.Resonance?.SelectEmperor(id) == true;
            case "muted" when command.Id is string id:
                return caster.Resonance?.SelectMuted(id) == true;
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

    private bool CastAt(Vector2 cursor)
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
        last = caster.Cast(field, cell, above);
        return true;
    }

    private void StepField()
    {
        long began = Stopwatch.GetTimestamp();
        caster.Advance(field, field.Settings.StepSeconds);
        field.Step();
        stepMilliseconds = (stepMilliseconds * 0.9) + (Stopwatch.GetElapsedTime(began).TotalMilliseconds * 0.1);
        IReadOnlyList<MaterialChange> changes = field.MaterialChanges;
        voxels.Apply(changes);
        foreach (MaterialChange change in changes)
        {
            counts[change.From]--;
            counts[change.To]++;
            if (scene.Pack.Materials[change.From].Sky != scene.Pack.Materials[change.To].Sky)
            {
                field.MarkOpenColumn(change.Cell.X, change.Cell.Z);
            }
        }
    }

    private void Switch(Scene next)
    {
        overlay.Dispose();
        voxels.Dispose();
        scene = next;
        voxels = new VoxelMirror(engine, scene.Pack, scene.Content.Materials);
        overlay = new QuantityOverlay(engine);
        counts = new int[scene.Pack.Materials.Length];
        shown = scene.Content.Overlays.FirstOrDefault();
        wind = "calm";
        camera.Reset(scene.CameraHome);
        caster = new Caster(scene.Tools, scene.Resonance);
        Reset();
    }

    private void Reset()
    {
        (field, symptoms) = scene.Build();
        field.Drift = Drift();
        string tool = caster.Current.Id;
        caster = new Caster(scene.Tools, scene.Resonance);
        caster.Select(tool);
        owed = 0;
        last = caster.Resonance is null ? "Pick a tool, then click the arena." : "Pick a channel, then click a symptom.";
        Reload();
    }

    private Vector3 Drift() => Winds.Single(choice => choice.Id == wind).Toward * scene.Content.WindSpeed;

    /// <summary>Draws a fresh field: every voxel, the overlay from scratch, and the material counts.</summary>
    private void Reload()
    {
        voxels.Load(field);
        overlay.Show(shown, shown is null ? 0 : scene.Pack.Quantity(shown.Quantity));
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
        Resonance? resonance = caster.Resonance;
        SandboxView view = new(
            [.. scenes.Select(known => new SandboxChoice(known.Entry.Id, known.Entry.Label))],
            scene.Entry.Id,
            [.. caster.Tools.Select(tool => new SandboxChoice(tool.Id, tool.Label, tool.Detail, resonance is null ? null : caster.IsVivid(tool)))],
            caster.Current.Id,
            [.. scene.Content.Overlays.Select(overlay => new SandboxChoice(overlay.Quantity, overlay.Label))],
            shown?.Quantity ?? "none",
            scene.Content.WindSpeed > 0 ? [.. Winds.Select(choice => new SandboxChoice(choice.Id, choice.Id))] : [],
            wind,
            resonance is null ? [] : [.. resonance.Emperors.Select(emperor => new SandboxChoice(emperor.Id, emperor.Label))],
            resonance?.Emperor.Id,
            resonance is null ? [] : [.. resonance.MutedCandidates.Select(muted => new SandboxChoice(muted.Id, muted.Label))],
            resonance?.Muted.Id,
            running,
            field.StepIndex,
            field.ActiveChunkCount,
            field.ChunkCount,
            field.ActiveFrontCount,
            caster.Pending,
            stepMilliseconds,
            overlay.Drawn.Cubes,
            [.. scene.Content.Materials.Keys.Select(id => (id, counts[scene.Pack.Material(id)]))],
            [.. symptoms.Select(symptom => symptom.Read(field, scene.Pack))],
            last);
        engine.Ui.PublishProjection(new UiProjection(ui, view.ToUiValue()));
    }
}
