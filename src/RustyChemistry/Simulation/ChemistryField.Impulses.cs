namespace RustyChemistry.Simulation;

public sealed partial class ChemistryField
{
    /// <summary>
    /// Queues a discharge for the next step: the origin cell takes <paramref name="amount"/>, and if it
    /// conducts the quantity at <paramref name="minConductance"/> or better, so does every face-connected cell
    /// that conducts as well, up to <paramref name="maxCells"/>. Lightning into a puddle charges the whole
    /// puddle; into dry ground, only where it lands.
    /// </summary>
    public void QueueDischarge(Cell origin, int quantity, float amount, float minConductance, int maxCells)
    {
        CheckQuantity(quantity);
        if (!float.IsFinite(amount) || !(minConductance > 0f) || maxCells < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "a discharge needs a finite amount, a positive conductance and at least one cell");
        }

        pendingDischarges.Add(new PendingDischarge(origin, quantity, amount, minConductance, maxCells));
    }

    /// <summary>
    /// Starts a front from a cell: it advances <paramref name="speed"/> cells of path per second, entering a
    /// cell costs one over that cell's conductance of the quantity (so it bends around what does not conduct
    /// and slows through what barely does), and each cell it reaches takes
    /// <paramref name="amplitude"/> × e^(−<paramref name="attenuation"/> × distance). It ends at
    /// <paramref name="maxDistance"/> or where its deposit falls below the quantity's epsilon.
    /// </summary>
    public int StartFront(Cell origin, int quantity, float amplitude, float speed, float attenuation, float maxDistance)
    {
        CheckQuantity(quantity);
        if (!float.IsFinite(amplitude) || amplitude == 0f || !(speed > 0f) || !(attenuation >= 0f) || !(maxDistance > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude), "a front needs a non-zero amplitude, a positive speed and reach, and attenuation not below zero");
        }

        Front front = new(nextFront++, quantity, amplitude, speed, attenuation, maxDistance);
        front.Frontier.Enqueue((origin, origin), (0f, front.Order++));
        fronts.Add(front);
        return front.Id;
    }

    private void ResolveDischarges()
    {
        foreach (PendingDischarge pending in pendingDischarges)
        {
            List<Cell> reached = [];
            if (IsLoaded(pending.Origin))
            {
                HashSet<Cell> seen = [pending.Origin];
                Queue<Cell> queue = new([pending.Origin]);
                while (queue.TryDequeue(out Cell cell))
                {
                    TryLocate(cell, out FieldChunk? chunk, out int index);
                    reached.Add(cell);
                    Change(chunk!, pending.Quantity, index, pending.Amount);
                    chunk!.Wake();
                    if (Conductance(chunk, index, pending.Quantity) < pending.MinConductance)
                    {
                        // A cell that does not conduct takes the charge and passes none on.
                        continue;
                    }

                    foreach (Cell offset in FaceOffsets)
                    {
                        Cell next = cell.Offset(offset.X, offset.Y, offset.Z);
                        if (seen.Count < pending.MaxCells && !seen.Contains(next) && TryLocate(next, out FieldChunk? nextChunk, out int nextIndex)
                            && Conductance(nextChunk, nextIndex, pending.Quantity) * Hold(chunk, index, nextChunk, nextIndex, pending.Quantity) >= pending.MinConductance)
                        {
                            seen.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            discharges.Add(new Discharge(pending.Origin, pending.Quantity, reached));
        }

        pendingDischarges.Clear();
    }

    private void AdvanceFronts(float seconds)
    {
        foreach (Front front in fronts)
        {
            Advance(front, seconds);
        }

        fronts.RemoveAll(front => front.Frontier.Count == 0);
    }

    private void Advance(Front front, float seconds)
    {
        front.Radius += front.Speed * seconds;
        int quantity = front.Quantity;
        while (front.Frontier.TryPeek(out (Cell Cell, Cell From) entry, out (float Distance, long Order) priority) && priority.Distance <= front.Radius)
        {
            front.Frontier.Dequeue();
            if (!TryLocate(entry.Cell, out FieldChunk? chunk, out int index) || !front.Reached.Add(entry.Cell))
            {
                continue;
            }

            float amount = front.Amplitude * MathF.Exp(-front.Attenuation * priority.Distance);
            Change(chunk, quantity, index, amount);
            chunk.Wake();
            arrivals.Add(new FrontArrival(front.Id, entry.Cell, entry.From, priority.Distance, amount));
            foreach (Cell offset in FaceOffsets)
            {
                Cell next = entry.Cell.Offset(offset.X, offset.Y, offset.Z);
                if (front.Reached.Contains(next) || !TryLocate(next, out FieldChunk? nextChunk, out int nextIndex))
                {
                    continue;
                }

                float permeability = Conductance(nextChunk, nextIndex, quantity) * Hold(chunk, index, nextChunk, nextIndex, quantity);
                if (permeability <= 0f)
                {
                    continue;
                }

                float distance = priority.Distance + (1f / permeability);
                if (distance <= front.MaxDistance && Math.Abs(front.Amplitude * MathF.Exp(-front.Attenuation * distance)) >= epsilon[quantity])
                {
                    front.Frontier.Enqueue((next, entry.Cell), (distance, front.Order++));
                }
            }
        }
    }

    /// <summary>How open the face between two cells is to a quantity, by the difference in its blocking hold.</summary>
    private float Hold(FieldChunk a, int aIndex, FieldChunk b, int bIndex, int quantity) =>
        Pack.Quantities[quantity].Transfer?.BlockedBy is int blocking
            ? HoldFactor(Value(a, blocking, aIndex), Value(b, blocking, bIndex))
            : 1f;

    private sealed record PendingDischarge(Cell Origin, int Quantity, float Amount, float MinConductance, int MaxCells);

    private sealed class Front(int id, int quantity, float amplitude, float speed, float attenuation, float maxDistance)
    {
        internal int Id { get; } = id;

        internal int Quantity { get; } = quantity;

        internal float Amplitude { get; } = amplitude;

        internal float Speed { get; } = speed;

        internal float Attenuation { get; } = attenuation;

        internal float MaxDistance { get; } = maxDistance;

        internal float Radius { get; set; }

        internal long Order { get; set; }

        internal PriorityQueue<(Cell Cell, Cell From), (float Distance, long Order)> Frontier { get; } = new();

        internal HashSet<Cell> Reached { get; } = [];
    }
}
