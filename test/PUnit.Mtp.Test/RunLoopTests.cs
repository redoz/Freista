using System.Collections.Concurrent;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Messages;
using Microsoft.Testing.Platform.TestHost;
using PUnit.Model;
using PUnit.Scheduling;
using Xunit;

namespace PUnit.Mtp.Test;

/// <summary>
/// Phase 5 behavioral tests for the run loop. On a run request the loop reads the filter
/// (a <c>TestNodeUidListFilter</c> uid set, or null = run everything), maps each requested step-uid
/// onto its owning scenario, and runs each <em>distinct</em> scenario exactly once via the
/// <see cref="ScenarioScheduler"/> with the Phase-4 reporter and a per-run
/// <see cref="CancellationTokenSource"/> owned by the loop. Because MTP's publish path has no
/// filter/lifecycle gate, every step the scheduler executes lights up — including the dependency
/// siblings of a single-step run.
/// </summary>
public class RunLoopTests
{
    private static ScenarioNode Node(
        int index,
        string stepId,
        string template,
        int[]? dependsOn = null,
        Func<IStepInputs, ScenarioContext, Task<object?>>? invoke = null) => new()
        {
            Index = index,
            StepId = stepId,
            Phase = "Given",
            OperationName = $"Op{index}",
            DisplayNameTemplate = template,
            SourceFile = @"C:\src\S.cs",
            SourceLine = index + 1,
            DependsOn = dependsOn ?? [],
            Invoke = invoke ?? ((_, _) => Task.FromResult<object?>(null)),
        };

    private static ScenarioDefinition Definition(string id, string display, params ScenarioNode[] nodes) => new()
    {
        ScenarioId = id,
        DisplayName = display,
        MethodName = $"Ns.{id}",
        Nodes = nodes,
    };

    private static string Uid(string scenarioId, string stepId) => scenarioId + ":" + stepId;

    // -- Scenario selection (filter -> distinct scenarios) ---------------------------------------

    [Fact]
    public void Null_filter_selects_every_registered_scenario()
    {
        var a = Definition("a", "A", Node(0, "x", "x"));
        var b = Definition("b", "B", Node(0, "y", "y"));

        var selected = PUnitRunLoop.SelectScenarios([a, b], uids: null);

        Assert.Equal(["a", "b"], selected.Select(d => d.ScenarioId).Order());
    }

    [Fact]
    public void Empty_uid_set_selects_nothing()
    {
        var a = Definition("a", "A", Node(0, "x", "x"));

        var selected = PUnitRunLoop.SelectScenarios([a], uids: new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Empty(selected);
    }

    [Fact]
    public void Multi_step_filter_for_one_scenario_selects_that_scenario_once()
    {
        var a = Definition("a", "A", Node(0, "x", "x"), Node(1, "y", "y"), Node(2, "z", "z"));
        var b = Definition("b", "B", Node(0, "x", "x"));

        // Three step uids of the SAME scenario must map to a single distinct scenario.
        var uids = new HashSet<string>([Uid("a", "x"), Uid("a", "y"), Uid("a", "z")], StringComparer.OrdinalIgnoreCase);
        var selected = PUnitRunLoop.SelectScenarios([a, b], uids);

        var only = Assert.Single(selected);
        Assert.Equal("a", only.ScenarioId);
    }

    [Fact]
    public void Filter_spanning_two_scenarios_selects_both()
    {
        var a = Definition("a", "A", Node(0, "x", "x"));
        var b = Definition("b", "B", Node(0, "y", "y"));
        var c = Definition("c", "C", Node(0, "z", "z"));

        var uids = new HashSet<string>([Uid("a", "x"), Uid("c", "z")], StringComparer.OrdinalIgnoreCase);
        var selected = PUnitRunLoop.SelectScenarios([a, b, c], uids);

        Assert.Equal(["a", "c"], selected.Select(d => d.ScenarioId).Order());
    }

    // -- Run loop end-to-end (over a real ScenarioScheduler) -------------------------------------

    [Fact]
    public async Task Multi_step_filter_for_one_scenario_runs_the_scheduler_exactly_once()
    {
        var def = Definition("a", "A",
            Node(0, "x", "x"),
            Node(1, "y", "y", dependsOn: [0]),
            Node(2, "z", "z", dependsOn: [1]));

        var runs = 0;
        var loop = new PUnitRunLoop(
            () => [def],
            runScenario: (_, _, _) => { Interlocked.Increment(ref runs); return Task.CompletedTask; });

        var uids = new HashSet<string>([Uid("a", "x"), Uid("a", "z")], StringComparer.OrdinalIgnoreCase);
        await loop.RunAsync(new SessionUid("s"), uids, new RecordingBus(), new StubProducer(), CancellationToken.None);

        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Single_step_filter_lights_up_all_executed_siblings()
    {
        // z depends on y depends on x. A filter naming only the last step must still publish the
        // whole chain, because running z requires running x and y first.
        var def = Definition("chain", "chain",
            Node(0, "x", "x"),
            Node(1, "y", "y", dependsOn: [0]),
            Node(2, "z", "z", dependsOn: [1]));

        var loop = new PUnitRunLoop(() => [def]);

        var bus = new RecordingBus();
        var uids = new HashSet<string>([Uid("chain", "z")], StringComparer.OrdinalIgnoreCase);
        await loop.RunAsync(new SessionUid("s"), uids, bus, new StubProducer(), CancellationToken.None);

        // All three siblings reported (each at least one finished Passed update).
        Assert.Contains(Uid("chain", "x"), bus.PassedUids);
        Assert.Contains(Uid("chain", "y"), bus.PassedUids);
        Assert.Contains(Uid("chain", "z"), bus.PassedUids);
    }

    [Fact]
    public async Task Null_filter_runs_all_registered_scenarios()
    {
        var a = Definition("a", "A", Node(0, "x", "x"));
        var b = Definition("b", "B", Node(0, "y", "y"));

        var loop = new PUnitRunLoop(() => [a, b]);

        var bus = new RecordingBus();
        await loop.RunAsync(new SessionUid("s"), uids: null, bus, new StubProducer(), CancellationToken.None);

        Assert.Contains(Uid("a", "x"), bus.PassedUids);
        Assert.Contains(Uid("b", "y"), bus.PassedUids);
    }

    [Fact]
    public async Task Distinct_scenarios_run_sequentially_for_v1()
    {
        // Records the max observed concurrency across scenario runs; sequential => never exceeds 1.
        var current = 0;
        var max = 0;
        var sync = new object();

        async Task<object?> Body(IStepInputs _, ScenarioContext __)
        {
            lock (sync)
            {
                current++;
                max = Math.Max(max, current);
            }

            await Task.Delay(20);

            lock (sync)
            {
                current--;
            }

            return null;
        }

        var a = Definition("a", "A", Node(0, "x", "x", invoke: Body));
        var b = Definition("b", "B", Node(0, "y", "y", invoke: Body));
        var c = Definition("c", "C", Node(0, "z", "z", invoke: Body));

        var loop = new PUnitRunLoop(() => [a, b, c]);
        await loop.RunAsync(new SessionUid("s"), uids: null, new RecordingBus(), new StubProducer(), CancellationToken.None);

        Assert.Equal(1, max);
    }

    [Fact]
    public async Task One_steps_cancellation_does_not_kill_the_shared_run_for_siblings()
    {
        // The loop owns ONE CTS per scenario run, linked to the platform token — it is never tied to
        // a single step node's lifecycle. So a step that itself throws OperationCanceledException
        // (a step that observed cancellation locally / timed out internally) must NOT abort the
        // independent siblings sharing that run: the loop's run token stays live, and the scheduler
        // keeps launching the other ready steps. Here "lonely" and "sibling" have no dependency
        // between them, so the scheduler runs both regardless of "lonely"'s outcome.
        var siblingRan = false;

        var def = Definition("scn", "scn",
            Node(0, "lonely", "lonely", invoke: (_, _) =>
                // An OCE NOT tied to the run token surfaces as an ordinary failure, not a scenario
                // cancel — proving the run token was untouched.
                throw new OperationCanceledException("this step canceled itself")),
            Node(1, "sibling", "sibling", invoke: (_, _) =>
            {
                siblingRan = true;
                return Task.FromResult<object?>(null);
            }));

        var loop = new PUnitRunLoop(() => [def]);

        var bus = new RecordingBus();
        var uids = new HashSet<string>([Uid("scn", "lonely")], StringComparer.OrdinalIgnoreCase);
        await loop.RunAsync(new SessionUid("s"), uids, bus, new StubProducer(), CancellationToken.None);

        Assert.True(siblingRan);
        // The sibling completes successfully; nothing was skipped (the run token never canceled).
        Assert.Contains(Uid("scn", "sibling"), bus.PassedUids);
        Assert.Empty(bus.SkippedUids);
    }

    [Fact]
    public async Task Honors_an_already_cancelled_platform_token_by_skipping_steps()
    {
        var bodyRan = false;
        var def = Definition("scn", "scn",
            Node(0, "x", "x", invoke: (_, _) => { bodyRan = true; return Task.FromResult<object?>(null); }));

        var loop = new PUnitRunLoop(() => [def]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var bus = new RecordingBus();
        await loop.RunAsync(new SessionUid("s"), uids: null, bus, new StubProducer(), cts.Token);

        // The scheduler skips steps when the run token is already canceled; the body never runs.
        Assert.False(bodyRan);
        Assert.Contains(Uid("scn", "x"), bus.SkippedUids);
    }

    [Fact]
    public async Task Cancellation_mid_run_stops_launching_further_scenarios()
    {
        // When the platform cancels mid-run, the loop must stop launching scenarios it has not yet
        // started — rather than iterating every remaining scenario only to report all-skipped (which
        // would flood the runner with skipped updates for work the user never started). The first
        // scenario cancels the platform token from inside its body; the second must never run.
        using var cts = new CancellationTokenSource();
        var secondRan = false;

        var first = Definition("first", "first",
            Node(0, "a", "a", invoke: (_, _) => { cts.Cancel(); return Task.FromResult<object?>(null); }));
        var second = Definition("second", "second",
            Node(0, "b", "b", invoke: (_, _) => { secondRan = true; return Task.FromResult<object?>(null); }));

        var loop = new PUnitRunLoop(() => [first, second]);

        var bus = new RecordingBus();
        await loop.RunAsync(new SessionUid("s"), uids: null, bus, new StubProducer(), cts.Token);

        Assert.False(secondRan);
        // The second scenario's node was never published at all (not even as skipped).
        Assert.DoesNotContain(Uid("second", "b"), bus.Nodes.Select(n => n.Uid.Value));
    }

    [Fact]
    public async Task Through_the_framework_run_request_executes_the_registered_scenario()
    {
        // End-to-end through PUnitTestFramework.OnExecute (registry-backed), proving the framework
        // wires the run loop into the run request path.
        var method = $"PUnit.Mtp.Test.RunLoop.{Guid.NewGuid():N}";
        ScenarioRegistry.Register(method, () => Definition("fw-scn", "fw scenario",
            Node(0, "a", "a"),
            Node(1, "b", "b", dependsOn: [0])));

        var framework = new PUnitTestFramework();
        var uid = new SessionUid("fw-run");
        await framework.CreateTestSession(uid);

        var bus = new RecordingBus();
        var completed = false;
        await framework.OnExecute(uid, filter: null, bus, () => completed = true, CancellationToken.None);

        Assert.True(completed);
        Assert.Contains(Uid("fw-scn", "a"), bus.PassedUids);
        Assert.Contains(Uid("fw-scn", "b"), bus.PassedUids);
    }

    private sealed class StubProducer : IDataProducer
    {
        public Type[] DataTypesProduced => [typeof(TestNodeUpdateMessage)];
        public string Uid => "stub.producer";
        public string Version => "1.0.0";
        public string DisplayName => "stub";
        public string Description => "stub data producer for run-loop tests";
        public Task<bool> IsEnabledAsync() => Task.FromResult(true);
    }

    private sealed class RecordingBus : IMessageBus
    {
        private readonly ConcurrentQueue<TestNodeUpdateMessage> updates = new();

        public IReadOnlyList<TestNode> Nodes => updates.Select(u => u.TestNode).ToList();

        public IReadOnlyList<string> PassedUids => UidsWithState<PassedTestNodeStateProperty>();

        public IReadOnlyList<string> SkippedUids => UidsWithState<SkippedTestNodeStateProperty>();

        private List<string> UidsWithState<TState>() where TState : IProperty => Nodes
            .Where(n => n.Properties.OfType<TState>().Length != 0)
            .Select(n => n.Uid.Value)
            .ToList();

        public Task PublishAsync(IDataProducer dataProducer, IData data)
        {
            if (data is TestNodeUpdateMessage update)
            {
                updates.Enqueue(update);
            }

            return Task.CompletedTask;
        }
    }
}
