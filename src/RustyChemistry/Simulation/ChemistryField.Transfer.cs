using System.Numerics;
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
        List<FieldChunk> transferring = [.. chunks.Where(chunk => chunk.Active)];
        if (transferring.Count == 0)
        {
            return;
        }

        foreach (FieldChunk chunk in transferring)
        {
            chunk.Transferring = true;
        }

        for (int quantity = 0; quantity < quantityCount; quantity++)
        {
            TransferDefinition? transfer = Pack.Quantities[quantity].Transfer;
            float diffusion = transfer is null ? 0f : transfer.Rate * Modulation(transfer.Modulator) * seconds;
            Vector3 carried = (transfer?.Drift == true ? drift : Vector3.Zero) + (Vector3.UnitY * (transfer?.Rise ?? 0f));
            int current = quantity;
            EmitterSpec[] gathering = [.. emitters.Values.Select(emitter => emitter.Spec)
                .Where(spec => spec.Quantity == current && spec.Gather != 0f)];
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

    private void TransferChunk(FieldChunk chunk, TransferPass pass)
    {
        ChunkKey key = chunk.Key;
        FieldChunk? east = chunkMap.GetValueOrDefault(key with { X = key.X + 1 });
        FieldChunk? west = chunkMap.GetValueOrDefault(key with { X = key.X - 1 });
        FieldChunk? up = chunkMap.GetValueOrDefault(key with { Y = key.Y + 1 });
        FieldChunk? down = chunkMap.GetValueOrDefault(key with { Y = key.Y - 1 });
        FieldChunk? north = chunkMap.GetValueOrDefault(key with { Z = key.Z + 1 });
        FieldChunk? south = chunkMap.GetValueOrDefault(key with { Z = key.Z - 1 });
        const int Mask = FieldChunk.Mask;
        const int Row = FieldChunk.Size;
        const int Layer = FieldChunk.Size * FieldChunk.Size;

        for (int z = 0; z < FieldChunk.Size; z++)
        {
            for (int y = 0; y < FieldChunk.Size; y++)
            {
                for (int x = 0; x < FieldChunk.Size; x++)
                {
                    int index = FieldChunk.Index(x, y, z);
                    if (Conductance(chunk, index, pass.Quantity) <= 0f)
                    {
                        // Every face of a closed cell is closed.
                        continue;
                    }

                    // A chunk owns the faces on its positive sides, and those on its negative sides whose
                    // neighbour is not transferring this step (sleeping, or outside the loaded region).
                    Face(chunk, index, x < Mask ? chunk : east, x < Mask ? index + 1 : FieldChunk.Index(0, y, z), 0, pass);
                    Face(chunk, index, y < Mask ? chunk : up, y < Mask ? index + Row : FieldChunk.Index(x, 0, z), 1, pass);
                    Face(chunk, index, z < Mask ? chunk : north, z < Mask ? index + Layer : FieldChunk.Index(x, y, 0), 2, pass);
                    if (x == 0 && west?.Transferring != true)
                    {
                        Face(west, FieldChunk.Index(Mask, y, z), chunk, index, 0, pass);
                    }

                    if (y == 0 && down?.Transferring != true)
                    {
                        Face(down, FieldChunk.Index(x, Mask, z), chunk, index, 1, pass);
                    }

                    if (z == 0 && south?.Transferring != true)
                    {
                        Face(south, FieldChunk.Index(x, y, Mask), chunk, index, 2, pass);
                    }
                }
            }
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

        if (pass.Advects)
        {
            Vector3 point = lower is not null ? lower.CellAt(lowerIndex).Center : upper!.CellAt(upperIndex).Center - Axis(axis);
            float courant = Math.Clamp(FaceVelocity(point + (Axis(axis) * 0.5f), axis, pass) * pass.Seconds, -FaceLimit, FaceLimit) * open;
            flux += courant > 0f ? courant * lowerValue : courant * upperValue;
        }

        if (flux == 0f)
        {
            return;
        }

        if (lower is not null)
        {
            lower.PrepareNext();
            lower.Next[pass.Offset + lowerIndex] -= flux;
        }

        if (upper is not null)
        {
            upper.PrepareNext();
            upper.Next[pass.Offset + upperIndex] += flux;
        }

        if (Math.Abs(flux) > epsilon[pass.Quantity])
        {
            lower?.Wake();
            upper?.Wake();
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
