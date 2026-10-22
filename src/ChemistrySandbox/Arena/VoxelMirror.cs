using ChemistrySandbox.Setup;
using Rusty.Engine;
using RustyChemistry.Content;
using RustyChemistry.Simulation;

namespace ChemistrySandbox.Arena;

/// <summary>
/// Draws the field's materials as Engine voxels, one voxel per cell. The field owns identity; this keeps the
/// Engine voxel scene matching it, writing back only the cells a step changed. Each material with a look gets
/// a voxel slot; air has none and clears its voxel.
/// </summary>
internal sealed class VoxelMirror : IDisposable
{
    private const int CellsPerChunk = ChemistryField.ChunkSize * ChemistryField.ChunkSize * ChemistryField.ChunkSize;

    private readonly IEngineContext engine;
    private readonly uint[] slots;
    private readonly VoxelDraw[] draws;
    private readonly Material[] materials;
    private VoxelScenePresentation? presentation;

    internal VoxelMirror(IEngineContext engine, ChemistryPack pack, IReadOnlyDictionary<string, MaterialLook> looks)
    {
        this.engine = engine;
        Session = engine.Spatial.CreateSession(new(1, ChemistryField.ChunkSize, VoxelSurfaceMode.GreedyCubes));
        slots = new uint[pack.Materials.Length];
        draws = new VoxelDraw[pack.Materials.Length];
        List<Material> created = [];
        List<VoxelMaterialCollision> collision = [];
        List<VoxelMaterialOcclusion> occlusion = [];
        foreach ((string id, MaterialLook look) in looks)
        {
            int material = pack.Material(id);
            uint slot = (uint)created.Count + 1;
            slots[material] = slot;
            bool opaque = look.Alpha >= 1f;
            draws[material] = opaque ? VoxelDraw.Opaque : VoxelDraw.Translucent;
            created.Add(engine.Graphics.CreateMaterial(new MaterialRequest(look.Base, default, 1f, new Color(1, 1, 1, 1),
                look.Glow, look.EmissionIntensity, false, opaque ? MaterialAlphaMode.Opaque : MaterialAlphaMode.Blend, 0.5f)));
            collision.Add(new(slot, !look.Passable));
            occlusion.Add(new(slot, opaque));
        }

        materials = [.. created];
        engine.Voxel.ConfigureMaterialCollision(new(Session, collision.ToArray()));
        engine.Voxel.ConfigureMaterialOcclusion(new(Session, occlusion.ToArray()));
    }

    /// <summary>The voxel world the arena is drawn and picked in.</summary>
    internal SpatialSession Session { get; }

    /// <summary>How a material's voxel draws: not at all, hiding its cell, or showing through.</summary>
    internal VoxelDraw Draw(int material) => draws[material];

    /// <summary>Writes every loaded cell, for a fresh or reset field.</summary>
    internal void Load(ChemistryField field)
    {
        List<VoxelEdit> edits = [];
        ushort[] chunk = new ushort[CellsPerChunk];
        foreach (Cell origin in field.LoadedChunks())
        {
            field.ReadChunkMaterials(origin, chunk);
            for (int index = 0; index < CellsPerChunk; index++)
            {
                edits.Add(Edit(Local(origin, index), chunk[index]));
            }
        }

        engine.Voxel.ApplyEdits(new(Session, edits.ToArray()));
        Present();
    }

    /// <summary>Writes the identity changes of the field's last step.</summary>
    internal void Apply(IReadOnlyList<MaterialChange> changes)
    {
        if (changes.Count == 0)
        {
            return;
        }

        VoxelEdit[] edits = [.. changes.Where(change => slots[change.From] != slots[change.To])
            .Select(change => Edit(change.Cell, change.To))];
        if (edits.Length > 0)
        {
            engine.Voxel.ApplyEdits(new(Session, edits));
            Present();
        }
    }

    public void Dispose()
    {
        presentation?.Dispose();
        foreach (Material material in materials)
        {
            material.Dispose();
        }

        Session.Dispose();
    }

    private void Present()
    {
        if (presentation is null)
        {
            VoxelSceneMaterialBinding[] bindings = [.. materials.Select((material, index) => new VoxelSceneMaterialBinding((uint)index + 1, material))];
            presentation = engine.VoxelScenePresentation.ProjectScene(new(Session, bindings));
        }
        else
        {
            engine.VoxelScenePresentation.RefreshScene(presentation);
        }
    }

    private VoxelEdit Edit(Cell cell, int material) => slots[material] == 0
        ? new(VoxelEditKind.Clear, new(cell.X, cell.Y, cell.Z), 0)
        : new(VoxelEditKind.Set, new(cell.X, cell.Y, cell.Z), slots[material]);

    private static Cell Local(Cell origin, int index)
    {
        const int Size = ChemistryField.ChunkSize;
        return origin.Offset(index % Size, index / Size % Size, index / (Size * Size));
    }
}

internal enum VoxelDraw : byte
{
    None,
    Opaque,
    Translucent,
}
