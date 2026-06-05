using PUnit.Model;
using PUnit.Scheduling;
using Xunit;

namespace PUnit.Test;

/// <summary>
/// The DAG scheduler is the heart of the runtime and is tested entirely independently of xUnit:
/// source-order sequencing, fork/join parallelism, the max-parallelism gate, dataflow between
/// steps, fail-then-skip-dependents, cancellation, per-step timeout, and observer callbacks.
/// </summary>
public class SchedulerTests
{
    static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

    static ScenarioNode Node(
        int index,
        Func<IStepInputs, ScenarioContext, Task<object?>> invoke,
        int[]? dependsOn = null,
        TimeSpan? timeout = null) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "Given",
        OperationName = $"Op{index}",
        DisplayNameTemplate = $"op {index}",
        DependsOn = dependsOn ?? [],
        Timeout = timeout,
        Invoke = invoke,
    };

    static ScenarioDefinition Def(params ScenarioNode[] nodes) => new()
    {
        ScenarioId = "scn",
        DisplayName = "scenario",
        MethodName = "Ns.Scn",
        Nodes = nodes,
    };

    static Func<IStepInputs, ScenarioContext, Task<object?>> Pass(object? output = null)
        => (_, _) => Task.FromResult(output);

    static async Task<T> WithTimeout<T>(Task<T> task)
    {
        var done = await Task.WhenAny(task, Task.Delay(Generous));
        Assert.True(done == task, "operation did not complete within the test timeout");
        return await task;
    }

    [Fact]
    public async Task Runs_chained_nodes_in_source_order()
    {
        var order = new List<int>();
        Func<int, Func<IStepInputs, ScenarioContext, Task<object?>>> rec =
            i => (_, _) => { lock (order) order.Add(i); return Task.FromResult<object?>(null); };

        var def = Def(Node(0, rec(0)), Node(1, rec(1), [0]), Node(2, rec(2), [1]));

        await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.Equal([0, 1, 2], order);
    }

    [Fact]
    public async Task Runs_independent_ready_nodes_concurrently_and_joins()
    {
        var entered = 0;
        var bothEntered = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        Func<IStepInputs, ScenarioContext, Task<object?>> parallel = async (_, _) =>
        {
            if (Interlocked.Increment(ref entered) == 2) bothEntered.SetResult();
            await release.Task;
            return null;
        };

        var joined = false;
        var def = Def(
            Node(0, Pass()),
            Node(1, parallel, [0]),
            Node(2, parallel, [0]),
            Node(3, (_, _) => { joined = true; return Task.FromResult<object?>(null); }, [1, 2]));

        var run = new ScenarioScheduler().RunAsync(def);

        // Both siblings must be in-flight together before we let either finish.
        await WithTimeout(Task.WhenAny(bothEntered.Task, Task.Delay(Generous))
            .ContinueWith(_ => bothEntered.Task.IsCompleted));
        Assert.True(bothEntered.Task.IsCompleted, "siblings did not run concurrently");

        release.SetResult();
        var results = await WithTimeout(run);

        Assert.True(joined);
        Assert.All(results, r => Assert.Equal(StepStatus.Passed, r.Status));
    }

    [Fact]
    public async Task Max_parallelism_one_serializes_siblings()
    {
        var sync = new object();
        var current = 0;
        var peak = 0;

        Func<IStepInputs, ScenarioContext, Task<object?>> tracked = async (_, _) =>
        {
            lock (sync) { current++; peak = Math.Max(peak, current); }
            await Task.Delay(25);
            lock (sync) { current--; }
            return null;
        };

        var def = Def(Node(0, Pass()), Node(1, tracked, [0]), Node(2, tracked, [0]));

        await WithTimeout(new ScenarioScheduler(maxParallelism: 1).RunAsync(def));

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task Flows_typed_output_to_dependent_step()
    {
        var def = Def(
            Node(0, Pass(42)),
            Node(1, (inputs, _) => Task.FromResult<object?>(inputs.Get<int>(0) + 1), [0]));

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.All(results, r => Assert.Equal(StepStatus.Passed, r.Status));
    }

    [Fact]
    public async Task Failure_skips_dependents_but_independent_branch_continues()
    {
        var independentRan = false;
        var def = Def(
            Node(0, Pass()),
            Node(1, (_, _) => throw new InvalidOperationException("boom"), [0]),
            Node(2, Pass(), [1]),                                   // depends on the failure
            Node(3, (_, _) => { independentRan = true; return Task.FromResult<object?>(null); }, [0]));

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.Equal(StepStatus.Passed, results[0].Status);
        Assert.Equal(StepStatus.Failed, results[1].Status);
        Assert.Equal(StepStatus.Skipped, results[2].Status);
        Assert.Contains("dependency failed", results[2].SkipReason);
        Assert.Contains("Op1", results[2].SkipReason);
        Assert.Equal(StepStatus.Passed, results[3].Status);
        Assert.True(independentRan);
    }

    [Fact]
    public async Task Multiple_dependency_failures_are_summarized()
    {
        var def = Def(
            Node(0, Pass()),
            Node(1, (_, _) => throw new InvalidOperationException(), [0]),
            Node(2, (_, _) => throw new InvalidOperationException(), [0]),
            Node(3, Pass(), [1, 2]));

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.Equal(StepStatus.Skipped, results[3].Status);
        Assert.Contains("Op1", results[3].SkipReason);
        Assert.Contains("Op2", results[3].SkipReason);
    }

    [Fact]
    public async Task Transitive_dependents_are_skipped()
    {
        var def = Def(
            Node(0, (_, _) => throw new InvalidOperationException("boom")),
            Node(1, Pass(), [0]),
            Node(2, Pass(), [1]));

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.Equal(StepStatus.Failed, results[0].Status);
        Assert.Equal(StepStatus.Skipped, results[1].Status);
        Assert.Equal(StepStatus.Skipped, results[2].Status);
    }

    [Fact]
    public async Task Pre_canceled_scenario_skips_all_steps()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ran = false;
        var def = Def(Node(0, (_, _) => { ran = true; return Task.FromResult<object?>(null); }));

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def, cancellationToken: cts.Token));

        Assert.False(ran);
        Assert.Equal(StepStatus.Skipped, results[0].Status);
        Assert.Contains("cancel", results[0].SkipReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_mid_run_stops_pending_steps()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource();

        var def = Def(
            Node(0, async (_, ctx) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ctx.CancellationToken);
                return null;
            }),
            Node(1, Pass(), [0]));

        var run = new ScenarioScheduler().RunAsync(def, cancellationToken: cts.Token);
        await started.Task;
        cts.Cancel();

        var results = await WithTimeout(run);

        Assert.NotEqual(StepStatus.Passed, results[0].Status);
        Assert.Equal(StepStatus.Skipped, results[1].Status);
    }

    [Fact]
    public async Task Step_exceeding_its_timeout_fails_with_timeout_exception()
    {
        var def = Def(Node(0,
            async (_, _) => { await Task.Delay(Generous); return null; },
            timeout: TimeSpan.FromMilliseconds(40)));

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.Equal(StepStatus.Failed, results[0].Status);
        Assert.IsType<TimeoutException>(results[0].Exception);
    }

    [Fact]
    public async Task Observer_sees_one_start_and_finish_per_node_with_status()
    {
        var observer = new RecordingObserver();
        var def = Def(
            Node(0, Pass()),
            Node(1, (_, _) => throw new InvalidOperationException(), [0]),
            Node(2, Pass(), [1]));

        await WithTimeout(new ScenarioScheduler().RunAsync(def, observer: observer));

        Assert.Equal(3, observer.Started.Count);
        Assert.Equal(
            [StepStatus.Passed, StepStatus.Failed, StepStatus.Skipped],
            observer.Finished.OrderBy(r => r.Node.Index).Select(r => r.Status));
    }

    [Fact]
    public async Task Run_validates_the_graph()
    {
        var def = Def(Node(0, Pass(), [1]), Node(1, Pass(), [0]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ScenarioScheduler().RunAsync(def));
    }

    sealed class RecordingObserver : IStepObserver
    {
        readonly object _sync = new();
        public List<(ScenarioNode Node, string Name)> Started { get; } = [];
        public List<StepResult> Finished { get; } = [];

        public Task OnStepStartingAsync(StepContext context)
        {
            lock (_sync) Started.Add((context.Node, context.DisplayName));
            return Task.CompletedTask;
        }

        public Task OnStepFinishedAsync(StepResult result)
        {
            lock (_sync) Finished.Add(result);
            return Task.CompletedTask;
        }
    }
}
