namespace RustyChemistry.Simulation;

/// <summary>
/// A uniform draw keyed by seed, step, cell and rule (SplitMix64 mixing), so a field replays identically
/// whatever order its chunks are visited in.
/// </summary>
internal static class KeyedDraw
{
    internal static double Unit(ulong seed, long step, Cell cell, int rule)
    {
        ulong hash = Mix(seed);
        hash = Mix(hash ^ (ulong)step);
        hash = Mix(hash ^ (uint)cell.X);
        hash = Mix(hash ^ ((ulong)(uint)cell.Y << 32));
        hash = Mix(hash ^ (uint)cell.Z);
        hash = Mix(hash ^ ((ulong)(uint)rule << 32));
        return (hash >> 11) * (1.0 / (1UL << 53));
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
