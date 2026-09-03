using Freista.Model;
using Freista.Scheduling;
using Xunit;

namespace Freista.Test;

/// <summary>
/// Cleanup is registered by the step that created the thing, so the closure captures both the object
/// and the connection. The log is scenario-scoped and written concurrently by parallel steps.
/// </summary>
public class TeardownTests
{
    private static ScenarioContext Context(string stepId, TeardownLog log, int stepIndex)
    {
        var ctx = new ScenarioContext(stepId, stepId, services: null, CancellationToken.None);
        ctx.AttachTeardown(log, stepIndex);
        return ctx;
    }

    [Fact]
    public void Registrations_record_their_owning_step_and_sequence()
    {
        var log = new TeardownLog();
        var ctx = Context("a", log, stepIndex: 3);

        ctx.OnTeardown(() => Task.CompletedTask);
        ctx.OnTeardown(Cleanup.Required, () => Task.CompletedTask);

        Assert.Equal(2, log.Entries.Count);
        Assert.All(log.Entries, e => Assert.Equal(3, e.OwningStepIndex));
        Assert.Equal(Cleanup.Optional, log.Entries[0].Kind);
        Assert.Equal(Cleanup.Required, log.Entries[1].Kind);
        Assert.True(log.Entries[1].Sequence > log.Entries[0].Sequence);
    }

    [Fact]
    public void A_context_with_no_log_attached_ignores_registration()
    {
        // A context built outside the scheduler (a DSL method under unit test) must not throw.
        var ctx = new ScenarioContext("a", "a", services: null, CancellationToken.None);

        ctx.OnTeardown(() => Task.CompletedTask);   // must not throw
    }

    [Fact]
    public void Concurrent_registration_keeps_every_entry()
    {
        var log = new TeardownLog();

        Parallel.For(0, 200, i =>
        {
            var ctx = Context("s" + i, log, i);
            ctx.OnTeardown(() => Task.CompletedTask);
        });

        Assert.Equal(200, log.Entries.Count);
        Assert.Equal(200, log.Entries.Select(e => e.Sequence).Distinct().Count());
    }

    [Fact]
    public void Node_is_not_a_teardown_node_by_default()
    {
        var node = new ScenarioNode
        {
            Index = 0,
            StepId = "s",
            Phase = "Given",
            OperationName = "Op",
            DisplayNameTemplate = "op",
            DependsOn = [],
            Invoke = (_, _) => Task.FromResult<object?>(null),
        };

        Assert.False(node.IsTeardown);
    }

    [Fact]
    public void Definition_defaults_to_running_teardown_always()
    {
        var def = new ScenarioDefinition
        {
            ScenarioId = "s",
            DisplayName = "s",
            MethodName = "Ns.S",
            Nodes = [],
        };

        Assert.Equal(Run.Always, def.TeardownPolicy);
    }
}
