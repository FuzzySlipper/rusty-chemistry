using System.Numerics;
using System.Runtime.CompilerServices;
using RustyChemistry.Content;

namespace RustyChemistry.Simulation;

public sealed partial class ChemistryField
{
    /// <summary>
    /// The most a face may move per step, as a share of a cell's value, for diffusion and drift each. A
    /// cell's six faces then never take more than it holds, so explicit transfer stays stable and
    /// non-negative. Faster movement is a front's job, not transfer's.
    /// </summary>
    private const float FaceLimit = 1f / 12f;

    private void Transfer(float seconds)
    {
        transferring.Clear();
        foreach (FieldChunk chunk in chunks)
        {
            if (chunk.Active)
            {
                chunk.Transferring = true;
                transferring.Add(chunk);
            }
        }

        if (transferring.Count == 0)
        {
            return;
        }

        for (int quantity = 0; quantity < quantityCount; quantity++)
        {
            TransferDefinition? transfer = Pack.Quantities[quantity].Transfer;
            float diffusion = transfer is null ? 0f : transfer.Rate * Modulation(transfer.Modulator) * seconds;
            Vector3 carried = (transfer?.Drift == true ? drift : Vector3.Zero) + (Vector3.UnitY * (transfer?.Rise ?? 0f));
            EmitterSpec[] gathering = Gathering(quantity);
            bool advects = carried != Vector3.Zero || gathering.Length > 0;
            if (diffusion <= 0f && !advects)
            {
                continue;
            }

            TransferPass pass = new(quantity, quantity * CellCount, diffusion, advects, carried, gathering,
                transfer?.BlockedBy, seconds);
            foreach (FieldChunk chunk in transferring)
            {
                TransferChunk(chunk, pass);
            }

            if (gathering.Length > 0)
            {
                // Gathering concentrates; a quantity's maximum still holds.
                foreach (FieldChunk chunk in chunks)
                {
                    if (chunk.NextReady)
                    {
                        foreach (ref float value in chunk.Next.AsSpan(pass.Offset, CellCount))
                        {
                            value = Math.Min(value, max[quantity]);
                        }
                    }
                }
            }
        }

        foreach (FieldChunk chunk in chunks)
        {
            if (chunk.NextReady)
            {
                chunk.SwapBuffers();
            }

            chunk.Transferring = false;
        }
    }

    /// <summary>
    /// Whether the step changed any of a swapped chunk's cells by more than its quantity's epsilon, from the
    /// values before transfer (in <see cref="FieldChunk.Next"/> after the swap) to those after the local rules.
    /// A chunk wakes on what its cells gained or lost over the whole step, not on each face's flux or each
    /// rule's change: under a steady drift a settled region passes on as much as it takes in, and water drawn
    /// up by grass and drained away by soil leaves both as they were.
    /// </summary>
    private bool Changed(FieldChunk chunk)
    {
        for (int quantity = 0; quantity < quantityCount; quantity++)
        {
            ReadOnlySpan<float> before = chunk.Values.AsSpan(quantity * CellCount, CellCount);
            ReadOnlySpan<float> after = chunk.Next.AsSpan(quantity * CellCount, CellCount);
            Vector<float> threshold = new(epsilon[quantity]);
            int index = 0;
            for (; index <= CellCount - Vector<float>.Count; index += Vector<float>.Count)
            {
                if (Vector.GreaterThanAny(Vector.Abs(new Vector<float>(after[index..]) - new Vector<float>(before[index..])), threshold))
                {
                    return true;
                }
            }

            for (; index < CellCount; index++)
            {
                if (Math.Abs(after[index] - before[index]) > epsilon[quantity])
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The emitters gathering or scattering a quantity; most steps have none, and then nothing is allocated.</summary>
    private EmitterSpec[] Gathering(int quantity)
    {
        List<EmitterSpec>? found = null;
        foreach (EmitterState emitter in emitters.Values)
        {
            if (emitter.Spec.Quantity == quantity && emitter.Spec.Gather != 0f)
            {
                (found ??= []).Add(emitter.Spec);
            }
        }

        return found is null ? [] : [.. found];
    }

    /// <summary>
    /// Transfers one quantity across the faces a chunk owns. A chunk owns the faces on its positive sides, and
    /// those on its negative sides whose neighbour is not transferring this step (sleeping, or outside the
    /// loaded region). Faces between two of its own cells take a fast path over its arrays when nothing holds
    /// the quantity and no emitter gathers it; every other face goes through <see cref="Face"/>. Both visit the
    /// faces in the same order and do the same arithmetic, so the result is the same as all going through
    /// <see cref="Face"/>.
    /// </summary>
    private void TransferChunk(FieldChunk chunk, TransferPass pass)
    {
        ChunkKey key = chunk.Key;
        FieldChunk? east = chunkMap.GetValueOrDefault(key with { X = key.X + 1 });
        FieldChunk? west = chunkMap.GetValueOrDefault(key with { X = key.X - 1 });
        FieldChunk? up = chunkMap.GetValueOrDefault(key with { Y = key.Y + 1 });
        FieldChunk? down = chunkMap.GetValueOrDefault(key with { Y = key.Y - 1 });
        FieldChunk? north = chunkMap.GetValueOrDefault(key with { Z = key.Z + 1 });
        FieldChunk? south = chunkMap.GetValueOrDefault(key with { Z = key.Z - 1 });
        bool westOwned = west?.Transferring != true, downOwned = down?.Transferring != true, southOwned = south?.Transferring != true;
        bool fast = pass.Gathering.Length == 0 && pass.BlockedBy is null;
        const int Mask = FieldChunk.Mask;
        const int Row = FieldChunk.Size;
        const int Layer = FieldChunk.Size * FieldChunk.Size;

        // The fast path writes the next buffer directly; preparing it up front costs one copy, and a chunk whose
        // copy nothing changes swaps back the same values.
        chunk.PrepareNext();
        float[] conducts = conductanceOf[pass.Quantity];
        ushort[] materials = chunk.Materials;
        float[] values = chunk.Values, next = chunk.Next;
        int offset = pass.Offset;
        float diffusion = pass.Diffusion;
        bool advects = pass.Advects;

        // Without gathering, the velocity across a face depends only on its axis.
        float courantX = Math.Clamp(pass.Carried.X * pass.Seconds, -FaceLimit, FaceLimit);
        float courantY = Math.Clamp(pass.Carried.Y * pass.Seconds, -FaceLimit, FaceLimit);
        float courantZ = Math.Clamp(pass.Carried.Z * pass.Seconds, -FaceLimit, FaceLimit);

        for (int z = 0; z < FieldChunk.Size; z++)
        {
            for (int y = 0; y < FieldChunk.Size; y++)
            {
                for (int x = 0; x < FieldChunk.Size; x++)
                {
                    int index = FieldChunk.Index(x, y, z);
                    float conductance = conducts[materials[index]];
                    if (conductance <= 0f)
                    {
                        // Every face of a closed cell is closed.
                        continue;
                    }

                    if (!fast)
                    {
                        Face(chunk, index, x < Mask ? chunk : east, x < Mask ? index + 1 : FieldChunk.Index(0, y, z), 0, pass);
                        Face(chunk, index, y < Mask ? chunk : up, y < Mask ? index + Row : FieldChunk.Index(x, 0, z), 1, pass);
                        Face(chunk, index, z < Mask ? chunk : north, z < Mask ? index + Layer : FieldChunk.Index(x, y, 0), 2, pass);
                        if (x == 0 && westOwned)
                        {
                            Face(west, FieldChunk.Index(Mask, y, z), chunk, index, 0, pass);
                        }

                        if (y == 0 && downOwned)
                        {
                            Face(down, FieldChunk.Index(x, Mask, z), chunk, index, 1, pass);
                        }

                        if (z == 0 && southOwned)
                        {
                            Face(south, FieldChunk.Index(x, y, Mask), chunk, index, 2, pass);
                        }

                        continue;
                    }

                    if (x < Mask)
                    {
                        Inner(values, next, offset, index, index + 1, conductance, conducts[materials[index + 1]], diffusion, advects, courantX);
                    }
                    else
                    {
                        Across(chunk, index, conductance, east, FieldChunk.Index(0, y, z), upper: true, pass, courantX);
                    }

                    if (y < Mask)
                    {
                        Inner(values, next, offset, index, index + Row, conductance, conducts[materials[index + Row]], diffusion, advects, courantY);
                    }
                    else
                    {
                        Across(chunk, index, conductance, up, FieldChunk.Index(x, 0, z), upper: true, pass, courantY);
                    }

                    if (z < Mask)
                    {
                        Inner(values, next, offset, index, index + Layer, conductance, conducts[materials[index + Layer]], diffusion, advects, courantZ);
                    }
                    else
                    {
                        Across(chunk, index, conductance, north, FieldChunk.Index(x, y, 0), upper: true, pass, courantZ);
                    }

                    if (x == 0 && westOwned)
                    {
                        Across(chunk, index, conductance, west, FieldChunk.Index(Mask, y, z), upper: false, pass, courantX);
                    }

                    if (y == 0 && downOwned)
                    {
                        Across(chunk, index, conductance, down, FieldChunk.Index(x, Mask, z), upper: false, pass, courantY);
                    }

                    if (z == 0 && southOwned)
                    {
                        Across(chunk, index, conductance, south, FieldChunk.Index(x, y, Mask), upper: false, pass, courantZ);
                    }
                }
            }
        }
    }

    /// <summary>
    /// <see cref="Face"/> between two cells of one chunk, when nothing holds the quantity and no emitter gathers
    /// it: the same arithmetic over the chunk's arrays.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Inner(float[] values, float[] next, int offset, int lowerIndex, int upperIndex,
        float lowerConductance, float upperConductance, float diffusion, bool advects, float courant)
    {
        float lowerValue = values[offset + lowerIndex], upperValue = values[offset + upperIndex];
        if (lowerValue == upperValue && (!advects || lowerValue == 0f))
        {
            // Nothing diffuses between equal cells, and nothing is carried between empty ones: most of a field
            // sits at its ambient, and most of its air holds no smoke.
            return;
        }

        float open = Math.Min(lowerConductance, upperConductance);
        if (open <= 0f)
        {
            return;
        }

        float flux = 0f;
        if (diffusion > 0f)
        {
            flux += Math.Min(diffusion * open, FaceLimit) * (lowerValue - upperValue);
        }

        if (advects)
        {
            float carried = courant * open;
            flux += carried > 0f ? carried * lowerValue : carried * upperValue;
        }

        next[offset + lowerIndex] -= flux;
        next[offset + upperIndex] += flux;
    }

    /// <summary>
    /// <see cref="Face"/> between a cell of a transferring chunk and its neighbour across the chunk's border, when
    /// nothing holds the quantity and no emitter gathers it: the same arithmetic, the neighbour standing above
    /// the cell along the axis when <paramref name="upper"/>, below it otherwise. A neighbour outside the loaded
    /// region is the ambient, as conductive as the cell.
    /// </summary>
    private void Across(FieldChunk chunk, int index, float conductance, FieldChunk? neighbour, int neighbourIndex, bool upper,
        TransferPass pass, float courant)
    {
        int offset = pass.Offset;
        float own = chunk.Values[offset + index];
        float other;
        float open;
        if (neighbour is null)
        {
            other = ambient[pass.Quantity];
            open = conductance;
        }
        else
        {
            other = neighbour.Values[offset + neighbourIndex];
            if (!pass.Advects && own == other)
            {
                return;
            }

            open = Math.Min(conductance, conductanceOf[pass.Quantity][neighbour.Materials[neighbourIndex]]);
        }

        // Face takes min(lower, upper); with no neighbour both sides are the cell's own conductance.
        if (open <= 0f)
        {
            return;
        }

        float lowerValue = upper ? own : other, upperValue = upper ? other : own;
        float flux = 0f;
        if (pass.Diffusion > 0f)
        {
            flux += Math.Min(pass.Diffusion * open, FaceLimit) * (lowerValue - upperValue);
        }

        float steady = 0f;
        if (pass.Advects)
        {
            float carried = courant * open;
            flux += carried > 0f ? carried * lowerValue : carried * upperValue;
            steady = carried * other;
        }

        if (flux == 0f)
        {
            return;
        }

        chunk.Next[offset + index] += upper ? -flux : flux;
        if (neighbour is not null)
        {
            // A sleeping neighbour is settled: it passes on the drift it takes in, as the outside does, so only
            // what differs from that crosses into it.
            float crossing = neighbour.Transferring ? flux : flux - steady;
            neighbour.PrepareNext();
            neighbour.Next[offset + neighbourIndex] += upper ? crossing : -crossing;
        }
    }

    /// <summary>
    /// Exchanges a quantity across the face between a lower cell and the upper cell next to it along an
    /// axis. Either side may lie outside the loaded region: that side is the ambient (its hold included), as
    /// conductive as the cell it faces, so the region's edge is open and a held region still confines there.
    /// </summary>
    private void Face(FieldChunk? lower, int lowerIndex, FieldChunk? upper, int upperIndex, int axis, TransferPass pass)
    {
        if (!pass.Advects && lower is not null && upper is not null
            && lower.Values[pass.Offset + lowerIndex] == upper.Values[pass.Offset + upperIndex])
        {
            // Nothing diffuses between equal cells: most of a field sits at its ambient.
            return;
        }

        FieldChunk inside = lower ?? upper!;
        int insideIndex = lower is null ? upperIndex : lowerIndex;
        (float lowerValue, float lowerConductance, float lowerHold) = Side(lower, lowerIndex, inside, insideIndex, pass);
        (float upperValue, float upperConductance, float upperHold) = Side(upper, upperIndex, inside, insideIndex, pass);
        float open = Math.Min(lowerConductance, upperConductance) * HoldFactor(lowerHold, upperHold);
        if (open <= 0f)
        {
            return;
        }

        float flux = 0f;
        if (pass.Diffusion > 0f)
        {
            flux += Math.Min(pass.Diffusion * open, FaceLimit) * (lowerValue - upperValue);
        }

        float courant = 0f;
        if (pass.Advects)
        {
            Vector3 point = lower is not null ? lower.CellAt(lowerIndex).Center : upper!.CellAt(upperIndex).Center - Axis(axis);
            courant = Math.Clamp(FaceVelocity(point + (Axis(axis) * 0.5f), axis, pass) * pass.Seconds, -FaceLimit, FaceLimit) * open;
            flux += courant > 0f ? courant * lowerValue : courant * upperValue;
        }

        if (flux == 0f)
        {
            return;
        }

        // A sleeping side is settled: it passes on the drift it takes in, as the outside does, so only what
        // differs from that crosses into it (see Across).
        if (lower is not null)
        {
            lower.PrepareNext();
            lower.Next[pass.Offset + lowerIndex] -= lower.Transferring ? flux : flux - (courant * lowerValue);
        }

        if (upper is not null)
        {
            upper.PrepareNext();
            upper.Next[pass.Offset + upperIndex] += upper.Transferring ? flux : flux - (courant * upperValue);
        }
    }

    private (float Value, float Conductance, float Hold) Side(FieldChunk? chunk, int index, FieldChunk inside, int insideIndex, TransferPass pass)
    {
        if (chunk is null)
        {
            float outsideHold = pass.BlockedBy is int outsideBlocking ? ambient[outsideBlocking] : 0f;
            return (ambient[pass.Quantity], Conductance(inside, insideIndex, pass.Quantity), outsideHold);
        }

        float hold = pass.BlockedBy is int blocking ? Value(chunk, blocking, index) : 0f;
        return (chunk.Values[pass.Offset + index], Conductance(chunk, index, pass.Quantity), hold);
    }

    /// <summary>A difference in hold across a face closes it: the edge of a held region confines what is inside.</summary>
    private static float HoldFactor(float a, float b) => 1f - Math.Min(1f, Math.Abs(a - b));

    /// <summary>The velocity across a face along its axis, in cells per second: drift and rise, plus any gathering.</summary>
    private static float FaceVelocity(Vector3 point, int axis, TransferPass pass)
    {
        float velocity = Component(pass.Carried, axis);
        foreach (EmitterSpec emitter in pass.Gathering)
        {
            if (!emitter.Shape.Contains(point))
            {
                continue;
            }

            Vector3 toward = emitter.Shape.Center - point;
            float distance = toward.Length();
            if (distance > 1e-4f)
            {
                velocity += Component(toward, axis) / distance * emitter.Gather;
            }
        }

        return velocity;
    }

    private static Vector3 Axis(int axis) => axis switch
    {
        0 => Vector3.UnitX,
        1 => Vector3.UnitY,
        _ => Vector3.UnitZ,
    };

    private static float Component(Vector3 vector, int axis) => axis switch
    {
        0 => vector.X,
        1 => vector.Y,
        _ => vector.Z,
    };

    private readonly record struct TransferPass(int Quantity, int Offset, float Diffusion, bool Advects, Vector3 Carried,
        EmitterSpec[] Gathering, int? BlockedBy, float Seconds);
}
