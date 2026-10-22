using System.Numerics;

namespace RustyChemistry.Simulation;

/// <summary>A cell of the field's grid. Cell (x, y, z) spans [x, x + 1) on each axis of cell space.</summary>
public readonly record struct Cell(int X, int Y, int Z)
{
    public Cell Offset(int x, int y, int z) => new(X + x, Y + y, Z + z);

    /// <summary>The cell's centre in cell space.</summary>
    public Vector3 Center => new(X + 0.5f, Y + 0.5f, Z + 0.5f);
}

/// <summary>
/// How a field runs: the material of a newly loaded chunk's other cells, the fixed step, the seed every
/// chance draw is keyed by, and the cell size and origin that place cell space in the product's world.
/// </summary>
public sealed record ChemistryFieldSettings(string DefaultMaterial, float StepSeconds = 0.1f, ulong Seed = 0,
    float CellSize = 1f, Vector3 Origin = default);

public enum EmitterShapeKind : byte
{
    Sphere,
    Box,
}

/// <summary>Where an emitter acts, in cell space: the cells whose centres it contains.</summary>
public readonly record struct EmitterShape(EmitterShapeKind Kind, Vector3 Center, float Radius, Vector3 Min, Vector3 Max)
{
    public static EmitterShape Sphere(Vector3 center, float radius) =>
        new(EmitterShapeKind.Sphere, center, radius, center - new Vector3(radius), center + new Vector3(radius));

    /// <summary>Every cell from <paramref name="min"/> to <paramref name="max"/>, inclusive.</summary>
    public static EmitterShape Cells(Cell min, Cell max)
    {
        Vector3 low = new(min.X, min.Y, min.Z);
        Vector3 high = new(max.X + 1, max.Y + 1, max.Z + 1);
        return new(EmitterShapeKind.Box, (low + high) / 2f, 0f, low, high);
    }

    public bool Contains(Vector3 point) => Kind == EmitterShapeKind.Sphere
        ? Vector3.DistanceSquared(point, Center) <= Radius * Radius
        : point.X >= Min.X && point.Y >= Min.Y && point.Z >= Min.Z && point.X <= Max.X && point.Y <= Max.Y && point.Z <= Max.Z;
}

/// <summary>
/// Something outside the field acting on it: a torch, rain, a chimney, a spell. Every cell inside the shape
/// gains <see cref="Rate"/> of the quantity per second (a negative rate drains it). <see cref="Gather"/>
/// carries the quantity toward the shape's centre at that many cells per second, or away from it when
/// negative. The emitter is removed once <see cref="Lifetime"/> seconds have run.
/// </summary>
public sealed record EmitterSpec(EmitterShape Shape, int Quantity, float Rate, float Gather = 0f, float Lifetime = float.PositiveInfinity);

public readonly record struct EmitterId(int Value);

/// <summary>A transform switched a cell's identity this step.</summary>
public readonly record struct MaterialChange(Cell Cell, int From, int To, int Transform);

/// <summary>A discharge resolved this step: the cells it reached, its origin first, in the order reached.</summary>
public sealed record Discharge(Cell Origin, int Quantity, IReadOnlyList<Cell> Cells);

/// <summary>
/// A front reached a cell this step, arriving from <see cref="From"/> (its origin arrives from itself) after
/// <see cref="Distance"/> cells of path, and deposited <see cref="Amount"/>.
/// </summary>
public readonly record struct FrontArrival(int Front, Cell Cell, Cell From, float Distance, float Amount);
