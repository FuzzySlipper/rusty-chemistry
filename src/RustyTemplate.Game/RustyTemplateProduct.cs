using Rusty.Engine;
using RustyTemplate.Game.Counter;

namespace RustyTemplate.Game;

public sealed class RustyTemplateProduct : IEngineProduct
{
    private const string IncrementIntent = "increment";
    private const string UiStreamId = "rusty-template";
    private const string UiContract = "rusty.template.counter";
    private const float DigitalIntentActiveThreshold = 0.5f;
    private const uint RootNodeIndex = 0;
    private const uint ValueNodeIndex = 1;
    private const uint ValueKeyLength = 5;
    private const uint ValueChildCount = 1;

    private readonly IEngineContext _engine;
    private readonly CounterState _counter = new();
    private readonly UiStream _uiStream;
    private ulong _uiSequence;
    private bool _started;
    private bool _paused;
    private bool _shutdown;

    public RustyTemplateProduct(ProductCreateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _engine = context.Engine;
        _uiStream = _engine.Ui.OpenStream(new UiStreamRequest(UiStreamId, UiContract));
    }

    public void Start()
    {
        if (_shutdown)
        {
            return;
        }

        _started = true;
        _paused = false;
        PublishCounter();
    }

    public ProductUpdateResult Update(ProductUpdate update)
    {
        if (!_started || _paused || _shutdown)
        {
            return ProductUpdateResult.None;
        }

        foreach (ProductInputEvent input in update.Input)
        {
            if (input.Kind != InputEventKind.DirectDigital
                || !input.Intent.Span.SequenceEqual(IncrementIntentBytes)
                || input.X <= DigitalIntentActiveThreshold)
            {
                continue;
            }

            _counter.Increment();
        }

        PublishCounter();
        return ProductUpdateResult.None;
    }

    public void Pause()
    {
        if (_started && !_shutdown)
        {
            _paused = true;
        }
    }

    public void Resume()
    {
        if (_started && !_shutdown)
        {
            _paused = false;
        }
    }

    public void Restart()
    {
        if (_shutdown)
        {
            return;
        }

        _counter.Reset();
        _started = true;
        _paused = false;
        PublishCounter();
    }

    public void Shutdown() => _shutdown = true;

    public void Dispose()
    {
        _shutdown = true;
        _uiStream.Dispose();
    }

    private void PublishCounter()
    {
        _engine.Ui.PublishProjection(new UiProjection(_uiStream, ++_uiSequence, BuildUiValue()));
    }

    private UiValue BuildUiValue()
    {
        StructuredValueNode[] nodes =
        [
            new(StructuredValueKind.Object, 0, 0, 0, 0, 0, 0, 0, ValueChildCount),
            new(StructuredValueKind.Number, 0, _counter.Value, 0, ValueKeyLength, 0, 0, 0, 0),
        ];
        return new UiValue(nodes, new uint[] { ValueNodeIndex }, RootNodeIndex, "value"u8.ToArray());
    }

    private static ReadOnlySpan<byte> IncrementIntentBytes => "increment"u8;
}
