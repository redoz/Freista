using Freista.Reporting;
using Xunit;

namespace Freista.Test.Reporting;

public class RunEventBusTests
{
    private sealed class RecordingSink : IRunEventSink
    {
        public List<RunEvent> Seen { get; } = [];
        public ValueTask PublishAsync(RunEvent evt) { Seen.Add(evt); return default; }
    }

    private sealed class ThrowingSink : IRunEventSink
    {
        public int Calls { get; private set; }
        public ValueTask PublishAsync(RunEvent evt) { Calls++; throw new InvalidOperationException("boom"); }
    }

    [Fact]
    public async Task Fans_out_to_each_sink_in_registration_order()
    {
        var order = new List<string>();
        var a = new DelegateSink(_ => order.Add("a"));
        var b = new DelegateSink(_ => order.Add("b"));
        var bus = new RunEventBus([a, b]);

        await bus.PublishAsync(new RunStarted(1));

        Assert.Equal(["a", "b"], order);
    }

    [Fact]
    public async Task A_throwing_sink_is_isolated_and_siblings_still_receive_every_event()
    {
        var bad = new ThrowingSink();
        var good = new RecordingSink();
        var bus = new RunEventBus([bad, good]);

        await bus.PublishAsync(new RunStarted(1));
        await bus.PublishAsync(new RunFinished());

        Assert.Equal(2, good.Seen.Count);          // sibling got both events
        Assert.Equal(2, bad.Calls);                 // bus kept calling the bad sink too
        var failure = Assert.Single(bus.Failures);  // first error per sink recorded
        Assert.IsType<InvalidOperationException>(failure);
    }

    [Fact]
    public async Task Records_one_failure_per_sink_not_per_event()
    {
        var bad = new ThrowingSink();
        var bus = new RunEventBus([bad]);

        await bus.PublishAsync(new RunStarted(1));
        await bus.PublishAsync(new RunFinished());

        Assert.Single(bus.Failures); // first error only; the sink is not re-reported each event
    }

    private sealed class DelegateSink(Action<RunEvent> onEvent) : IRunEventSink
    {
        public ValueTask PublishAsync(RunEvent evt) { onEvent(evt); return default; }
    }
}
