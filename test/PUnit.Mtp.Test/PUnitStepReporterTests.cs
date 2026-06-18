using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Messages;
using Microsoft.Testing.Platform.TestHost;
using PUnit.Model;
using PUnit.Scheduling;
using Xunit;

namespace PUnit.Mtp.Test;

/// <summary>
/// Phase 4 behavioral tests for the reporter: a <see cref="PUnitStepReporter"/> implements
/// <see cref="IStepObserver"/> and maps each step lifecycle event onto a Microsoft.Testing.Platform
/// <see cref="TestNodeUpdateMessage"/>. start -> <see cref="InProgressTestNodeStateProperty"/>;
/// <see cref="StepStatus.Passed"/> -> <see cref="PassedTestNodeStateProperty"/>;
/// <see cref="StepStatus.Failed"/> splits by exception kind into Failed/Timeout/Error;
/// <see cref="StepStatus.Skipped"/> -> <see cref="SkippedTestNodeStateProperty"/>. Finished updates
/// carry <see cref="TimingProperty"/>, the step's <see cref="TestFileLocationProperty"/>, and the
/// runtime-formatted display name; logs surface as standard output. The observer contract is async:
/// the reporter awaits the platform's <see cref="IMessageBus.PublishAsync"/> directly rather than
/// blocking on it.
/// </summary>
public class PUnitStepReporterTests
{
    private static ScenarioNode Node(int index, string stepId, string template, string? file = null, int line = 0, string? group = null) => new()
    {
        Index = index,
        StepId = stepId,
        Phase = "Given",
        OperationName = $"Op{index}",
        DisplayNameTemplate = template,
        SourceFile = file,
        SourceLine = line,
        DependsOn = [],
        GroupId = group,
        Invoke = (_, _) => Task.FromResult<object?>(null),
    };

    private static ScenarioDefinition Definition(string id = "scn", string display = "my scenario", params ScenarioNode[] nodes) => new()
    {
        ScenarioId = id,
        DisplayName = display,
        MethodName = "Ns.Scn",
        Nodes = nodes.Length == 0 ? [Node(0, "a", "step a")] : nodes,
    };

    private static (PUnitStepReporter Reporter, RecordingMessageBus Bus) NewReporter(ScenarioDefinition definition)
    {
        var bus = new RecordingMessageBus();
        var producer = new StubProducer();
        var reporter = new PUnitStepReporter(definition, new SessionUid("sess"), bus, producer);
        return (reporter, bus);
    }

    [Fact]
    public async Task Start_publishes_in_progress_update_for_the_step_node()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        await reporter.OnStepStartingAsync(new StepContext { Node = def.Nodes[0], DisplayName = "step a" });

        var node = Assert.Single(bus.Nodes);
        Assert.Equal("s:a", node.Uid.Value);
        Assert.NotEmpty(node.Properties.OfType<InProgressTestNodeStateProperty>());
    }

    [Fact]
    public async Task Published_node_carries_method_identity_for_grouping()
    {
        // Run updates must carry the same namespace/class/method identity discovery emits, so the
        // runner keeps the step nodes grouped under their scenario method rather than re-bucketing
        // them under "<Empty Namespace>" when results arrive.
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        await reporter.OnStepStartingAsync(new StepContext { Node = def.Nodes[0], DisplayName = "step a" });

        var node = Assert.Single(bus.Nodes);
        Assert.NotEmpty(node.Properties.OfType<TestMethodIdentifierProperty>());
    }

    [Fact]
    public async Task Finished_node_carries_method_identity_for_grouping()
    {
        // The finish update is what the runner settles on when a step completes. If it lacked the
        // method identity, the node would collapse back under "<Empty Namespace>" the instant it
        // passed — grouped while running, nameless once done. So the terminal node must carry it too.
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        await reporter.OnStepFinishedAsync(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Passed,
            StartedAt = default,
            Duration = TimeSpan.FromMilliseconds(1),
        });

        var node = Assert.Single(bus.Nodes);
        Assert.NotEmpty(node.Properties.OfType<TestMethodIdentifierProperty>());
    }

    [Fact]
    public async Task Start_uses_numbered_runtime_formatted_display_name_without_prefix()
    {
        var def = Definition(id: "s", display: "patient booking", nodes: [Node(0, "a", "patient exists")]);
        var (reporter, bus) = NewReporter(def);

        // The scheduler computes the formatted name at run time (placeholders resolved); the reporter
        // surfaces that, numbered and without the old scenario prefix.
        await reporter.OnStepStartingAsync(new StepContext { Node = def.Nodes[0], DisplayName = "patient Jane exists" });

        var node = Assert.Single(bus.Nodes);
        Assert.Equal("1. patient Jane exists", node.DisplayName);
    }

    [Fact]
    public async Task Passed_step_publishes_passed_state()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        await reporter.OnStepFinishedAsync(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Passed,
            StartedAt = default,
            Duration = TimeSpan.FromMilliseconds(5),
        });

        var node = Assert.Single(bus.Nodes);
        Assert.NotEmpty(node.Properties.OfType<PassedTestNodeStateProperty>());
    }

    [Fact]
    public async Task Failed_assertion_publishes_failed_state_carrying_the_exception()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        // A genuine xunit.v3.assert failure: its base type is Xunit.Sdk.XunitException, which the
        // reporter must recognize as an assertion (Failed), not a generic Error.
        var ex = Assert.ThrowsAny<Exception>(() => Assert.Equal(1, 2));

        await reporter.OnStepFinishedAsync(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Failed,
            StartedAt = default,
            Exception = ex,
        });

        var node = Assert.Single(bus.Nodes);
        var failed = Assert.Single(node.Properties.OfType<FailedTestNodeStateProperty>());
        Assert.Same(ex, failed.Exception);
        Assert.Empty(node.Properties.OfType<ErrorTestNodeStateProperty>());
        Assert.Empty(node.Properties.OfType<TimeoutTestNodeStateProperty>());
    }

    [Fact]
    public async Task Failed_timeout_publishes_timeout_state()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);
        var ex = new TimeoutException("step timed out");

        await reporter.OnStepFinishedAsync(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Failed,
            StartedAt = default,
            Exception = ex,
        });

        var node = Assert.Single(bus.Nodes);
        var timeout = Assert.Single(node.Properties.OfType<TimeoutTestNodeStateProperty>());
        Assert.Same(ex, timeout.Exception);
        Assert.Empty(node.Properties.OfType<FailedTestNodeStateProperty>());
    }

    [Fact]
    public async Task Failed_other_exception_publishes_error_state()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);
        var ex = new InvalidOperationException("boom");

        await reporter.OnStepFinishedAsync(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Failed,
            StartedAt = default,
            Exception = ex,
        });

        var node = Assert.Single(bus.Nodes);
        var error = Assert.Single(node.Properties.OfType<ErrorTestNodeStateProperty>());
        Assert.Same(ex, error.Exception);
        Assert.Empty(node.Properties.OfType<FailedTestNodeStateProperty>());
        Assert.Empty(node.Properties.OfType<TimeoutTestNodeStateProperty>());
    }

    [Fact]
    public async Task Skipped_step_publishes_skipped_state_with_reason()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        await reporter.OnStepFinishedAsync(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Skipped,
            StartedAt = default,
            SkipReason = "dependency failed: creating an appointment",
        });

        var node = Assert.Single(bus.Nodes);
        var skipped = Assert.Single(node.Properties.OfType<SkippedTestNodeStateProperty>());
        Assert.Equal("dependency failed: creating an appointment", skipped.Explanation);
    }

    [Fact]
    public async Task Finished_update_carries_timing_property()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        await reporter.OnStepStartingAsync(new StepContext { Node = def.Nodes[0], DisplayName = "step a" });
        await reporter.OnStepFinishedAsync(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Passed,
            StartedAt = default,
            Duration = TimeSpan.FromMilliseconds(250),
        });

        var finished = bus.Nodes[^1];
        var timing = Assert.Single(finished.Properties.OfType<TimingProperty>());
        Assert.Equal(TimeSpan.FromMilliseconds(250), timing.GlobalTiming.Duration);
    }

    [Fact]
    public async Task Finished_update_carries_file_location_when_source_is_known()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a", file: @"C:\src\B.cs", line: 12)]);
        var (reporter, bus) = NewReporter(def);

        await reporter.OnStepFinishedAsync(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Passed,
            StartedAt = default,
        });

        var node = Assert.Single(bus.Nodes);
        var location = Assert.Single(node.Properties.OfType<TestFileLocationProperty>());
        Assert.Equal(@"C:\src\B.cs", location.FilePath);
        Assert.Equal(12, location.LineSpan.Start.Line);
    }

    [Fact]
    public async Task Finished_update_uses_the_numbered_results_formatted_display_name()
    {
        var def = Definition(id: "s", display: "booking", nodes: [Node(0, "a", "patient exists")]);
        var (reporter, bus) = NewReporter(def);

        await reporter.OnStepFinishedAsync(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "patient Jane exists",
            Status = StepStatus.Passed,
            StartedAt = default,
        });

        var node = Assert.Single(bus.Nodes);
        Assert.Equal("1. patient Jane exists", node.DisplayName);
    }

    [Fact]
    public async Task Group_member_step_is_numbered_with_sub_index()
    {
        var def = Definition(
            id: "s",
            nodes:
            [
                Node(0, "clean", "the database is clean"),
                Node(1, "p", "patient exists", group: "g1"),
                Node(2, "slot", "slot exists", group: "g1"),
            ]);
        var (reporter, bus) = NewReporter(def);

        await reporter.OnStepStartingAsync(new StepContext { Node = def.Nodes[1], DisplayName = "patient Jane exists" });

        var node = Assert.Single(bus.Nodes);
        Assert.Equal("2.1 patient Jane exists", node.DisplayName);
    }

    [Fact]
    public async Task Logs_surface_as_standard_output_on_the_finished_update()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        await reporter.OnStepFinishedAsync(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Passed,
            StartedAt = default,
            Logs = ["first line", "second line"],
        });

#pragma warning disable TPEXP // StandardOutputProperty is experimental in MTP 1.9.1.
        var node = Assert.Single(bus.Nodes);
        var output = Assert.Single(node.Properties.OfType<StandardOutputProperty>());
        Assert.Contains("first line", output.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("second line", output.StandardOutput, StringComparison.Ordinal);
#pragma warning restore TPEXP
    }

    [Fact]
    public async Task Resource_effects_surface_as_standard_output_on_the_finished_update()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        await reporter.OnStepFinishedAsync(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Passed,
            StartedAt = default,
            Effects =
            [
                new ResourceEffect
                {
                    Verb = LifecycleVerb.Create,
                    Identity = new ResourceIdentity(typeof(string), "jane"),
                    StepId = "a",
                    StepDisplayName = "step a",
                },
            ],
        });

#pragma warning disable TPEXP // StandardOutputProperty is experimental in MTP 1.9.1.
        var node = Assert.Single(bus.Nodes);
        var output = Assert.Single(node.Properties.OfType<StandardOutputProperty>());
        Assert.Contains("[resource] Create String:jane", output.StandardOutput, StringComparison.Ordinal);
#pragma warning restore TPEXP
    }

    [Fact]
    public async Task Each_published_update_carries_the_session_uid()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var bus = new RecordingMessageBus();
        var reporter = new PUnitStepReporter(def, new SessionUid("the-session"), bus, new StubProducer());

        await reporter.OnStepStartingAsync(new StepContext { Node = def.Nodes[0], DisplayName = "step a" });

        var update = Assert.Single(bus.Updates);
        Assert.Equal("the-session", update.SessionUid.Value);
    }

    [Fact]
    public async Task OnStepFinishedAsync_awaits_the_publish_instead_of_blocking()
    {
        // Proves the reporter is genuinely async rather than sync-over-async: against a publish that
        // has not completed yet, the returned task must still be pending. A
        // `.GetAwaiter().GetResult()` implementation would block the calling thread at the call and
        // never return this task (hanging the test) — which is exactly the hazard this guards against.
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var gate = new TaskCompletionSource();
        var reporter = new PUnitStepReporter(def, new SessionUid("sess"), new GatedMessageBus(gate.Task), new StubProducer());

        var finished = reporter.OnStepFinishedAsync(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Passed,
            StartedAt = default,
        });

        Assert.False(finished.IsCompleted);
        gate.SetResult();
        await finished;
    }

    private sealed class StubProducer : IDataProducer
    {
        public Type[] DataTypesProduced => [typeof(TestNodeUpdateMessage)];
        public string Uid => "stub.producer";
        public string Version => "1.0.0";
        public string DisplayName => "stub";
        public string Description => "stub data producer for reporter tests";
        public Task<bool> IsEnabledAsync() => Task.FromResult(true);
    }

    private sealed class RecordingMessageBus : IMessageBus
    {
        private readonly List<TestNodeUpdateMessage> updates = [];

        public IReadOnlyList<TestNodeUpdateMessage> Updates
        {
            get { lock (updates) { return [.. updates]; } }
        }

        public IReadOnlyList<TestNode> Nodes => Updates.Select(u => u.TestNode).ToList();

        public Task PublishAsync(IDataProducer dataProducer, IData data)
        {
            if (data is TestNodeUpdateMessage update)
            {
                lock (updates)
                {
                    updates.Add(update);
                }
            }

            return Task.CompletedTask;
        }
    }

    // A bus whose publish completes only when the supplied gate task completes — lets a test observe
    // that the reporter awaits the publish rather than blocking on it.
    private sealed class GatedMessageBus(Task gate) : IMessageBus
    {
        public Task PublishAsync(IDataProducer dataProducer, IData data) => gate;
    }
}
