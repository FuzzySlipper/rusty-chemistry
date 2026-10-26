namespace RustyChemistry.Simulation;

internal readonly record struct ChunkKey(int X, int Y, int Z) : IComparable<ChunkKey>
{
    public static ChunkKey Of(Cell cell) => new(cell.X >> FieldChunk.Shift, cell.Y >> FieldChunk.Shift, cell.Z >> FieldChunk.Shift);

    public int CompareTo(ChunkKey other)
    {
        int byX = X.CompareTo(other.X);
        if (byX != 0)
        {
            return byX;
        }

        int byY = Y.CompareTo(other.Y);
        return byY != 0 ? byY : Z.CompareTo(other.Z);
    }
}

/// <summary>
/// A 16³ block of loaded cells: identities, sky exposure, and every quantity's values in a current and a
/// next buffer (the next is only written during transfer). Values are laid out quantity-major.
/// </summary>
internal sealed class FieldChunk
{
    internal const int Shift = 4;
    internal const int Size = 1 << Shift;
    internal const int Mask = Size - 1;
    internal const int CellCount = Size * Size * Size;

    internal FieldChunk(ChunkKey key, int quantities)
    {
        Key = key;
        Values = new float[quantities * CellCount];
        Next = new float[quantities * CellCount];
    }

    internal ChunkKey Key { get; }

    internal ushort[] Materials { get; } = new ushort[CellCount];

    internal bool[] Open { get; } = new bool[CellCount];

    internal float[] Values { get; private set; }

    internal float[] Next { get; private set; }

    /// <summary>Whether the field processes this chunk; it sleeps after a few steps of no change.</summary>
    internal bool Active { get; set; }

    /// <summary>Whether anything in the chunk changed (or a transform was pending) this step.</summary>
    internal bool Touched { get; set; }

    internal int QuietSteps { get; set; }

    /// <summary>Whether <see cref="Next"/> holds this step's copy of <see cref="Values"/>.</summary>
    internal bool NextReady { get; set; }

    /// <summary>Whether the chunk was active when this step's transfer began, so it owns its border faces.</summary>
    internal bool Transferring { get; set; }

    /// <summary>
    /// Whether transfer swapped the buffers this step: <see cref="Next"/> then holds the values from before
    /// transfer, which the step's end compares against to tell whether the chunk changed.
    /// </summary>
    internal bool Swapped { get; set; }

    internal static int Index(int x, int y, int z) => x | (y << Shift) | (z << (2 * Shift));

    internal static int Index(Cell cell) => Index(cell.X & Mask, cell.Y & Mask, cell.Z & Mask);

    internal Cell CellAt(int index) => new(
        (Key.X << Shift) + (index & Mask),
        (Key.Y << Shift) + ((index >> Shift) & Mask),
        (Key.Z << Shift) + (index >> (2 * Shift)));

    internal void Wake()
    {
        Active = true;
        Touched = true;
        QuietSteps = 0;
    }

    internal void PrepareNext()
    {
        if (!NextReady)
        {
            Array.Copy(Values, Next, Values.Length);
            NextReady = true;
        }
    }

    internal void SwapBuffers()
    {
        (Values, Next) = (Next, Values);
        NextReady = false;
        Swapped = true;
    }
}
