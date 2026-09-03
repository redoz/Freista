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

    private static ScenarioNode Step(
        int index,
        Func<IStepInputs, ScenarioContext, Task<object?>> invoke,
        int[]? dependsOn = null) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "Given",
        OperationName = $"Op{index}",
        DisplayNameTemplate = $"op {index}",
        DependsOn = dependsOn ?? [],
        Invoke = invoke,
    };

    private static ScenarioNode TeardownNode(int index) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "Then",
        OperationName = "Teardown",
        DisplayNameTemplate = "Teardown",
        DependsOn = [],
        IsTeardown = true,
        Invoke = (_, _) => Task.FromResult<object?>(null),
    };

    private static ScenarioDefinition Def(Run policy, params ScenarioNode[] nodes) => new()
    {
        ScenarioId = "scn",
        DisplayName = "scenario",
        MethodName = "Ns.Scn",
        TeardownPolicy = policy,
        Nodes = nodes,
    };

    [Fact]
    public async Task Cleanups_run_in_reverse_topological_order()
    {
        var order = new List<string>();
        var def = Def(Run.Always,
            Step(0, (_, ctx) => { ctx.OnTeardown(() => { lock (order) { order.Add("first"); } return Task.CompletedTask; }); return Task.FromResult<object?>(null); }),
            Step(1, (_, ctx) => { ctx.OnTeardown(() => { lock (order) { order.Add("second"); } return Task.CompletedTask; }); return Task.FromResult<object?>(null); }, [0]),
            TeardownNode(2));

        var results = await new ScenarioScheduler().RunAsync(def);

        Assert.Equal(["second", "first"], order);
        Assert.Equal(StepStatus.Passed, results[2].Status);
    }

    [Fact]
    public async Task Within_one_step_cleanups_run_in_reverse_registration_order()
    {
        var order = new List<string>();
        var def = Def(Run.Always,
            Step(0, (_, ctx) =>
            {
                ctx.OnTeardown(() => { order.Add("a"); return Task.CompletedTask; });
                ctx.OnTeardown(() => { order.Add("b"); return Task.CompletedTask; });
                return Task.FromResult<object?>(null);
            }),
            TeardownNode(1));

        await new ScenarioScheduler().RunAsync(def);

        Assert.Equal(["b", "a"], order);
    }

    [Fact]
    public async Task OnSuccess_skips_optional_cleanups_when_a_step_failed()
    {
        var ran = false;
        var def = Def(Run.OnSuccess,
            Step(0, (_, ctx) => { ctx.OnTeardown(() => { ran = true; return Task.CompletedTask; }); return Task.FromResult<object?>(null); }),
            Step(1, (_, _) => throw new InvalidOperationException("boom"), [0]),
            TeardownNode(2));

        var results = await new ScenarioScheduler().RunAsync(def);

        Assert.False(ran);
        Assert.Equal(StepStatus.NotTaken, results[2].Status);
    }

    [Fact]
    public async Task OnSuccess_runs_optional_cleanups_when_every_step_passed()
    {
        var ran = false;
        var def = Def(Run.OnSuccess,
            Step(0, (_, ctx) => { ctx.OnTeardown(() => { ran = true; return Task.CompletedTask; }); return Task.FromResult<object?>(null); }),
            TeardownNode(1));

        await new ScenarioScheduler().RunAsync(def);

        Assert.True(ran);
    }

    [Fact]
    public async Task Required_cleanups_run_even_under_Run_Never()
    {
        var optional = false;
        var required = false;
        var def = Def(Run.Never,
            Step(0, (_, ctx) =>
            {
                ctx.OnTeardown(() => { optional = true; return Task.CompletedTask; });
                ctx.OnTeardown(Cleanup.Required, () => { required = true; return Task.CompletedTask; });
                return Task.FromResult<object?>(null);
            }),
            TeardownNode(1));

        var results = await new ScenarioScheduler().RunAsync(def);

        Assert.False(optional);
        Assert.True(required);
        Assert.Equal(StepStatus.Passed, results[1].Status);
    }

    [Fact]
    public async Task A_throwing_cleanup_does_not_stop_the_rest()
    {
        var later = false;
        var def = Def(Run.Always,
            Step(0, (_, ctx) => { ctx.OnTeardown(() => { later = true; return Task.CompletedTask; }); return Task.FromResult<object?>(null); }),
            Step(1, (_, ctx) => { ctx.OnTeardown(() => throw new InvalidOperationException("cleanup boom")); return Task.FromResult<object?>(null); }, [0]),
            TeardownNode(2));

        var results = await new ScenarioScheduler().RunAsync(def);

        Assert.True(later);
        Assert.Equal(StepStatus.Failed, results[2].Status);
        Assert.Contains("cleanup boom", results[2].Exception!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Teardown_node_is_not_taken_when_nothing_registered()
    {
        var def = Def(Run.Always, Step(0, (_, _) => Task.FromResult<object?>(null)), TeardownNode(1));

        var results = await new ScenarioScheduler().RunAsync(def);

        Assert.Equal(StepStatus.NotTaken, results[1].Status);
    }

    [Fact]
    public async Task A_step_in_an_untaken_branch_registers_nothing()
    {
        var ran = false;
        var cond = new ScenarioNode
        {
            Index = 0,
            StepId = "cond",
            Phase = "Given",
            OperationName = "Cond",
            DisplayNameTemplate = "cond",
            DependsOn = [],
            Invoke = (_, _) => Task.FromResult<object?>(false),
            EvaluateCondition = static o => (bool)o!,
        };
        var arm = new ScenarioNode
        {
            Index = 1,
            StepId = "arm",
            Phase = "When",
            OperationName = "Arm",
            DisplayNameTemplate = "arm",
            DependsOn = [0],
            Guards = [new Guard(0, true)],
            Invoke = (_, ctx) => { ctx.OnTeardown(() => { ran = true; return Task.CompletedTask; }); return Task.FromResult<object?>(null); },
        };

        var results = await new ScenarioScheduler().RunAsync(Def(Run.Always, cond, arm, TeardownNode(2)));

        Assert.Equal(StepStatus.NotTaken, results[1].Status);
        Assert.False(ran);
        Assert.Equal(StepStatus.NotTaken, results[2].Status);
    }

    [Fact]
    public async Task Required_cleanups_run_after_the_scenario_is_cancelled()
    {
        // The case that matters most: a cancelled or timed-out scenario is exactly when a container
        // leaks, so the cancelled token must not suppress the cleanup that prevents it.
        var released = false;
        using var cts = new CancellationTokenSource();
        var def = Def(Run.Always,
            Step(0, (_, ctx) =>
            {
                ctx.OnTeardown(Cleanup.Required, () => { released = true; return Task.CompletedTask; });
                cts.Cancel();
                return Task.FromResult<object?>(null);
            }),
            Step(1, (_, _) => Task.FromResult<object?>(null), [0]),
            TeardownNode(2));

        await new ScenarioScheduler().RunAsync(def, cancellationToken: cts.Token);

        Assert.True(released);
    }
}
