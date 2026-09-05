namespace Raun.Test;

/// <summary>A deterministic <see cref="TimeProvider"/> for tests: returns a fixed base instant,
/// advanced by a fixed step on every <see cref="GetUtcNow"/> call so concurrently-stamped steps get
/// distinct, ordered <c>StartedAt</c> values without real time.</summary>
internal sealed class TestTimeProvider(DateTimeOffset start, TimeSpan? perCall = null) : TimeProvider
{
    private readonly TimeSpan _step = perCall ?? TimeSpan.FromMilliseconds(10);
    private long _ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow()
    {
        var ticks = Interlocked.Add(ref _ticks, _step.Ticks) - _step.Ticks;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
