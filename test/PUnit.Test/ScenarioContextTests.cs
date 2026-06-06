using PUnit;
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

    private sealed class StubProvider(object value) : IServiceProvider
    {
        public object? GetService(Type serviceType) => value;
    }
}
