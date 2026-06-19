using PUnit;
using PUnit.Scheduling;
using Xunit;

namespace PUnit.Test;

/// <summary>
/// <see cref="ScenarioContext"/> is the per-step handle a DSL operation may accept for
/// cancellation, logging, attachments, and resolving services. Each step gets its own context so
/// logs and attachments stay associated with the right step even under parallel execution.
/// </summary>
public class ScenarioContextTests
{
    [Fact]
    public void Exposes_step_identity_and_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var ctx = new ScenarioContext("step-1", "patient Jane exists", services: null, cts.Token);

        Assert.Equal("step-1", ctx.StepId);
        Assert.Equal("patient Jane exists", ctx.StepDisplayName);
        Assert.Equal(cts.Token, ctx.CancellationToken);
        Assert.Null(ctx.Services);
    }

    [Fact]
    public void Accumulates_logs_in_order()
    {
        var ctx = new ScenarioContext("s", "n", services: null, CancellationToken.None);

        ctx.Log("first");
        ctx.Log("second");

        Assert.Equal(new[] { "first", "second" }, ctx.Logs);
    }

    [Fact]
    public void Records_attachments_by_name()
    {
        var ctx = new ScenarioContext("s", "n", services: null, CancellationToken.None);

        ctx.AddAttachment("request", "{ }");

        Assert.Equal("{ }", ctx.Attachments["request"]);
    }

    [Fact]
    public void Resolves_services_from_provider()
    {
        var provider = new StubProvider("hello");
        var ctx = new ScenarioContext("s", "n", provider, CancellationToken.None);

        Assert.Same("hello", ctx.Services!.GetService(typeof(string)));
    }

    [Fact]
    public void Logging_is_safe_under_concurrency()
    {
        var ctx = new ScenarioContext("s", "n", services: null, CancellationToken.None);

        Parallel.For(0, 1000, i => ctx.Log($"m{i}"));

        Assert.Equal(1000, ctx.Logs.Count);
    }

    /// <summary>
    /// When a <see cref="ResourceIdentityResolver"/> is registered in the service provider,
    /// <see cref="ScenarioContext.Resources"/> must use it so that selectors registered on that
    /// resolver (chain link 2) are honoured.
    /// </summary>
    [Fact]
    public async Task Resources_uses_resolver_from_Services_when_registered()
    {
        // A plain record that is NOT IResource<> / IResourceIdentity — without a registered
        // selector the resolver falls back to whole-record value-equality (chain link 4).
        var resolver = new ResourceIdentityResolver();
        resolver.Identify<PlainWidget>(w => w.Name);

        var provider = new ResolverStubProvider(resolver);
        var ctx = new ScenarioContext("s", "n", provider, CancellationToken.None);

        await ctx.Resources.Read(new PlainWidget("widget"));

        var effect = Assert.Single(ctx.Resources.Effects);
        Assert.Equal(new ResourceIdentity(typeof(PlainWidget), "widget"), effect.Identity);
    }

    /// <summary>
    /// When built over a <see cref="SimulatedClock"/> (the per-step clock the scheduler injects in
    /// simulated-time mode), <see cref="ScenarioContext.SimulateElapsed"/> advances that exact clock,
    /// observable through the context's own <see cref="ScenarioContext.TimeProvider"/>.
    /// </summary>
    [Fact]
    public void SimulateElapsed_advances_a_simulated_clock_via_TimeProvider()
    {
        var baseInstant = new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero);
        var clock = new SimulatedClock(baseInstant);
        var ctx = new ScenarioContext(
            "s", "n", services: null, resolver: null, timeProvider: clock, CancellationToken.None);

        Assert.Same(clock, ctx.TimeProvider);

        ctx.SimulateElapsed(TimeSpan.FromMilliseconds(250));

        Assert.Equal(baseInstant + TimeSpan.FromMilliseconds(250), ctx.TimeProvider.GetUtcNow());
    }

    /// <summary>
    /// Because the resource tracer shares the same per-step <see cref="TimeProvider"/>, a simulated
    /// advance shifts the timestamp of effects recorded afterwards onto the same timeline.
    /// </summary>
    [Fact]
    public async Task SimulateElapsed_shifts_subsequent_resource_effect_timestamps()
    {
        var baseInstant = new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero);
        var clock = new SimulatedClock(baseInstant);
        var ctx = new ScenarioContext(
            "s", "n", services: null, resolver: null, timeProvider: clock, CancellationToken.None);

        ctx.SimulateElapsed(TimeSpan.FromMilliseconds(500));
        await ctx.Resources.Create(new PlainWidget("w"));

        var effect = Assert.Single(ctx.Resources.Effects);
        Assert.Equal(baseInstant + TimeSpan.FromMilliseconds(500), effect.Timestamp);
    }

    /// <summary>
    /// On a real run the context is built over <see cref="TimeProvider.System"/> (via either
    /// constructor); <see cref="ScenarioContext.SimulateElapsed"/> must be an inert no-op that never
    /// throws and never swaps out the provider.
    /// </summary>
    [Fact]
    public void SimulateElapsed_is_a_noop_over_the_system_time_provider()
    {
        var ctx = new ScenarioContext("s", "n", services: null, CancellationToken.None);

        Assert.Same(TimeProvider.System, ctx.TimeProvider);

        ctx.SimulateElapsed(TimeSpan.FromSeconds(5));

        Assert.Same(TimeProvider.System, ctx.TimeProvider);
    }

    private sealed record PlainWidget(string Name);

    private sealed class StubProvider(object value) : IServiceProvider
    {
        public object? GetService(Type serviceType) => value;
    }

    /// <summary>Returns a <see cref="ResourceIdentityResolver"/> for that exact type, null otherwise.</summary>
    private sealed class ResolverStubProvider(ResourceIdentityResolver resolver) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(ResourceIdentityResolver) ? resolver : null;
    }
}
