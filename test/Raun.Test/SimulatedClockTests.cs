using Raun.Scheduling;
using Xunit;

namespace Raun.Test;

/// <summary>
/// <see cref="SimulatedClock"/> is a thread-safe <see cref="TimeProvider"/> whose only motion comes
/// from explicit <see cref="SimulatedClock.Advance(TimeSpan)"/> calls. It backs per-step simulated
/// timing, so <see cref="TimeProvider.GetUtcNow"/>, <see cref="TimeProvider.GetTimestamp"/>, and
/// <see cref="TimeProvider.GetElapsedTime(long)"/> must all stay mutually consistent across advances,
/// and many concurrent advances must sum exactly.
/// </summary>
public class SimulatedClockTests
{
    private static readonly DateTimeOffset Seed =
        new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetUtcNow_BeforeAnyAdvance_ReturnsSeed()
    {
        var clock = new SimulatedClock(Seed);

        Assert.Equal(Seed, clock.GetUtcNow());
        Assert.Equal(TimeSpan.Zero, clock.Advanced);
    }

    [Fact]
    public void Advance_MovesGetUtcNowByExactlyDelta()
    {
        var clock = new SimulatedClock(Seed);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(Seed + TimeSpan.FromMilliseconds(250), clock.GetUtcNow());
        Assert.Equal(TimeSpan.FromMilliseconds(250), clock.Advanced);

        clock.Advance(TimeSpan.FromMilliseconds(750));
        Assert.Equal(Seed + TimeSpan.FromSeconds(1), clock.GetUtcNow());
        Assert.Equal(TimeSpan.FromSeconds(1), clock.Advanced);
    }

    [Fact]
    public void GetElapsedTime_StaysConsistentWithAdvances()
    {
        var clock = new SimulatedClock(Seed);
        var start = clock.GetTimestamp();

        clock.Advance(TimeSpan.FromMilliseconds(125));
        clock.Advance(TimeSpan.FromMilliseconds(375));

        Assert.Equal(TimeSpan.FromMilliseconds(500), clock.GetElapsedTime(start));
    }

    [Fact]
    public void GetTimestamp_AndFrequency_ConvertBackToElapsedExactly()
    {
        var clock = new SimulatedClock(Seed);
        var t0 = clock.GetTimestamp();

        clock.Advance(TimeSpan.FromMilliseconds(640));
        var t1 = clock.GetTimestamp();

        var seconds = (t1 - t0) / (double)clock.TimestampFrequency;
        Assert.Equal(0.640, seconds, 6);
        Assert.Equal(TimeSpan.FromMilliseconds(640), clock.GetElapsedTime(t0, t1));
    }

    [Fact]
    public async Task Advance_FromManyThreads_SumsExactly()
    {
        var clock = new SimulatedClock(Seed);
        const int threads = 32;
        const int perThread = 1000;

        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < perThread; i++)
            {
                clock.Advance(TimeSpan.FromTicks(1));
            }
        }));
        await Task.WhenAll(tasks);

        var expected = TimeSpan.FromTicks(threads * perThread);
        Assert.Equal(expected, clock.Advanced);
        Assert.Equal(Seed + expected, clock.GetUtcNow());
    }

    [Fact]
    public void IsTimeProvider()
    {
        Assert.IsAssignableFrom<TimeProvider>(new SimulatedClock(Seed));
    }
}
