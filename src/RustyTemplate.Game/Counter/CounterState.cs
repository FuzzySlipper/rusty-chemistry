namespace RustyTemplate.Game.Counter;

public sealed class CounterState
{
    private const ulong MaximumValue = ulong.MaxValue;

    public ulong Value { get; private set; }

    public void Increment()
    {
        if (Value == MaximumValue)
        {
            return;
        }

        Value++;
    }

    public void Reset() => Value = 0;
}
