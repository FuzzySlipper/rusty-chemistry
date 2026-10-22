using System.Numerics;
using ChemistrySandbox.Arena;
using ChemistrySandbox.Setup;
using Rusty.Engine;
using RustyChemistry.Simulation;

namespace ChemistrySandbox.Overlay;

/// <summary>
/// Shows one quantity of the field as translucent cubes, one mesh per field chunk: in an open cell a cube
/// grows with the value, in an opaque voxel a tinted shell around it shows the value inside. Under a
/// translucent voxel (water) the cube is opaque, since two blended surfaces in one cell draw in whichever
/// order their objects sort. Values are shown in a few levels; a chunk's mesh is rebuilt only when they change.
/// </summary>
internal sealed class QuantityOverlay : IDisposable
{
    private const int Levels = 8;
    private const int CellsPerChunk = ChemistryField.ChunkSize * ChemistryField.ChunkSize * ChemistryField.ChunkSize;

    /// <summary>Overlay objects are numbered from here, one per chunk, clear of anything else the product draws.</summary>
    private const ulong FirstObjectId = 1_000_000;

    private static readonly Vector3[] Corners =
    [
        new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
        new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1),
    ];

    // Each face as four corners, counter-clockwise from outside, with its normal.
    private static readonly (int A, int B, int C, int D, Vector3 Normal)[] Faces =
    [
        (1, 2, 6, 5, Vector3.UnitX), (4, 7, 3, 0, -Vector3.UnitX),
        (3, 7, 6, 2, Vector3.UnitY), (4, 0, 1, 5, -Vector3.UnitY),
        (5, 6, 7, 4, Vector3.UnitZ), (0, 3, 2, 1, -Vector3.UnitZ),
    ];

    private readonly IEngineContext engine;
    private readonly Material blended;
    private readonly Material solid;
    private readonly Dictionary<Cell, ChunkOverlay> chunks = [];
    private readonly float[] values = new float[CellsPerChunk];
    private readonly ushort[] materials = new ushort[CellsPerChunk];
    private OverlayDefinition? shown;
    private int quantity;
    private Color color;

    internal QuantityOverlay(IEngineContext engine)
    {
        this.engine = engine;
        blended = engine.Graphics.CreateMaterial(new MaterialRequest(new Color(1, 1, 1, 1), default, 1f, new Color(1, 1, 1, 1),
            Vector3.Zero, 0f, true, MaterialAlphaMode.Blend, 0f) with { Unlit = true });
        solid = engine.Graphics.CreateMaterial(new MaterialRequest(new Color(1, 1, 1, 1), default, 1f, new Color(1, 1, 1, 1),
            Vector3.Zero, 0f, false) with { Unlit = true });
    }

    /// <summary>The meshes drawn now, and the cubes in them.</summary>
    internal (int Meshes, int Cubes) Drawn => (chunks.Values.Count(chunk => chunk.Mesh is not null), chunks.Values.Sum(chunk => chunk.Cubes));

    /// <summary>Switches to another quantity, or to nothing; every chunk is redrawn on the next refresh.</summary>
    internal void Show(OverlayDefinition? overlay, int quantityIndex)
    {
        shown = overlay;
        quantity = quantityIndex;
        color = overlay is null ? default : MaterialLook.Linear(overlay.Color, 1f);
        foreach (ChunkOverlay chunk in chunks.Values)
        {
            // No quantized level is this, so every chunk with a mesh rebuilds or removes it.
            Array.Fill(chunk.Levels, byte.MaxValue);
            chunk.Stale = true;
        }
    }

    /// <summary>
    /// Redraws the chunks whose shown levels changed. Only active chunks are read, since a sleeping chunk's
    /// values hold still; <paramref name="everything"/> reads them all, after a reset or a switch.
    /// </summary>
    internal void Refresh(ChemistryField field, Func<int, VoxelDraw> draw, bool everything)
    {
        List<AppearanceFact> upserts = [];
        List<ulong> removals = [];
        List<IDisposable> retired = [];
        foreach (Cell origin in field.LoadedChunks())
        {
            if (!chunks.TryGetValue(origin, out ChunkOverlay? chunk))
            {
                chunk = new ChunkOverlay(FirstObjectId + (ulong)chunks.Count);
                chunks.Add(origin, chunk);
            }

            if (!everything && !chunk.Stale && !field.IsActive(origin))
            {
                continue;
            }

            chunk.Stale = false;
            byte[] levels = Quantize(field, origin);
            if (levels.AsSpan().SequenceEqual(chunk.Levels))
            {
                continue;
            }

            levels.CopyTo(chunk.Levels, 0);
            bool drawn = chunk.Mesh is not null;
            if (chunk.Mesh is MeshResource old && chunk.Appearance is Appearance oldAppearance)
            {
                retired.Add(oldAppearance);
                retired.Add(old);
                chunk.Mesh = null;
                chunk.Appearance = null;
            }

            field.ReadChunkMaterials(origin, materials);
            MeshResourceCreateRequest? request = Build(origin, levels, draw, out int cubes);
            chunk.Cubes = cubes;
            if (request is MeshResourceCreateRequest mesh)
            {
                chunk.Mesh = engine.Graphics.CreateMeshResource(mesh);
                chunk.Appearance = engine.Graphics.CreateMeshAppearance(chunk.Mesh);
                upserts.Add(new(chunk.ObjectId, false, 0, new(Vector3.Zero, Quaternion.Identity, Vector3.One), chunk.Appearance,
                    true, RenderLayer.Scene, ShadowCasting.None));
            }
            else if (drawn)
            {
                removals.Add(chunk.ObjectId);
            }
        }

        if (upserts.Count > 0 || removals.Count > 0)
        {
            engine.Graphics.PublishChanges(new(upserts.ToArray(), removals.ToArray(), ReadOnlyMemory<MeshJointAttachment>.Empty));
        }

        // Released only once the objects that drew them are replaced or removed.
        foreach (IDisposable resource in retired)
        {
            resource.Dispose();
        }
    }

    public void Dispose()
    {
        ulong[] drawn = [.. chunks.Values.Where(chunk => chunk.Mesh is not null).Select(chunk => chunk.ObjectId)];
        if (drawn.Length > 0)
        {
            engine.Graphics.PublishChanges(new(ReadOnlyMemory<AppearanceFact>.Empty, drawn, ReadOnlyMemory<MeshJointAttachment>.Empty));
        }

        foreach (ChunkOverlay chunk in chunks.Values)
        {
            chunk.Appearance?.Dispose();
            chunk.Mesh?.Dispose();
        }

        blended.Dispose();
        solid.Dispose();
    }

    private byte[] Quantize(ChemistryField field, Cell origin)
    {
        byte[] levels = new byte[CellsPerChunk];
        if (shown is null)
        {
            return levels;
        }

        field.ReadChunk(origin, quantity, values);
        float span = shown.High - shown.Low;
        for (int index = 0; index < CellsPerChunk; index++)
        {
            float share = (values[index] - shown.Low) / span;
            levels[index] = share < 0f ? (byte)0 : (byte)(1 + Math.Min(Levels - 1, (int)(share * Levels)));
        }

        return levels;
    }

    private MeshResourceCreateRequest? Build(Cell origin, byte[] levels, Func<int, VoxelDraw> draw, out int cubes)
    {
        const int Size = ChemistryField.ChunkSize;
        List<Vector3> positions = [];
        List<Vector3> normals = [];
        List<Color> colors = [];
        List<uint> blendedIndices = [];
        List<uint> solidIndices = [];
        cubes = 0;
        for (int index = 0; index < CellsPerChunk; index++)
        {
            if (levels[index] == 0)
            {
                continue;
            }

            float strength = levels[index] / (float)Levels;
            Vector3 center = new Vector3(origin.X + (index % Size), origin.Y + (index / Size % Size), origin.Z + (index / (Size * Size)))
                + new Vector3(0.5f);
            // A shell just outside an opaque voxel; elsewhere a cube that grows with the value but stays off the
            // cell's faces, where it would fight a translucent voxel's surface for depth.
            VoxelDraw voxel = draw(materials[index]);
            bool shell = voxel == VoxelDraw.Opaque;
            float half = shell ? 0.52f : 0.12f + (0.33f * strength);
            Color tint = color with { A = shell ? 0.15f + (0.45f * strength) : 0.1f + (0.6f * strength) };
            List<uint> indices = voxel == VoxelDraw.Translucent ? solidIndices : blendedIndices;
            foreach ((int a, int b, int c, int d, Vector3 normal) in Faces)
            {
                uint first = (uint)positions.Count;
                foreach (int corner in (ReadOnlySpan<int>)[a, b, c, d])
                {
                    positions.Add(center + (Corners[corner] * half));
                    normals.Add(normal);
                    colors.Add(tint);
                }

                indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
            }

            cubes++;
        }

        if (cubes == 0)
        {
            return null;
        }

        List<MeshGroup> groups = [];
        List<MeshMaterialBinding> bindings = [];
        if (blendedIndices.Count > 0)
        {
            groups.Add(new(0, 0, (uint)blendedIndices.Count));
            bindings.Add(new(0, blended));
        }

        if (solidIndices.Count > 0)
        {
            groups.Add(new(1, (uint)blendedIndices.Count, (uint)solidIndices.Count));
            bindings.Add(new(1, solid));
        }

        uint[] indexed = [.. blendedIndices, .. solidIndices];
        return new MeshResourceCreateRequest(positions.ToArray(), normals.ToArray(), new Vector2[positions.Count], colors.ToArray(), indexed,
            groups.ToArray(), bindings.ToArray());
    }

    private sealed class ChunkOverlay(ulong objectId)
    {
        internal ulong ObjectId { get; } = objectId;

        internal byte[] Levels { get; } = new byte[CellsPerChunk];

        internal bool Stale { get; set; } = true;

        internal MeshResource? Mesh { get; set; }

        internal Appearance? Appearance { get; set; }

        internal int Cubes { get; set; }
    }
}
