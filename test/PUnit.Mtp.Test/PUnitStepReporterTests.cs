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
/// runtime-formatted display name; logs surface as standard output.
/// </summary>
public class PUnitStepReporterTests
{
    static ScenarioNode Node(int index, string stepId, string template, string? file = null, int line = 0) => new()
    {
        Index = index,
        StepId = stepId,
        Phase = "Given",
        OperationName = $"Op{index}",
        DisplayNameTemplate = template,
        SourceFile = file,
        SourceLine = line,
        DependsOn = [],
        Invoke = (_, _) => Task.FromResult<object?>(null),
    };

    static ScenarioDefinition Definition(string id = "scn", string display = "my scenario", params ScenarioNode[] nodes) => new()
    {
        ScenarioId = id,
        DisplayName = display,
        MethodName = "Ns.Scn",
        Nodes = nodes.Length == 0 ? [Node(0, "a", "step a")] : nodes,
    };

    static (PUnitStepReporter Reporter, RecordingMessageBus Bus) NewReporter(ScenarioDefinition definition)
    {
        var bus = new RecordingMessageBus();
        var producer = new StubProducer();
        var reporter = new PUnitStepReporter(definition, new SessionUid("sess"), bus, producer);
        return (reporter, bus);
    }

    [Fact]
    public void Start_publishes_in_progress_update_for_the_step_node()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        reporter.OnStepStarting(def.Nodes[0], "step a");

        var node = Assert.Single(bus.Nodes);
        Assert.Equal("s:a", node.Uid.Value);
        Assert.NotEmpty(node.Properties.OfType<InProgressTestNodeStateProperty>());
    }

    [Fact]
    public void Start_uses_runtime_formatted_display_name_with_scenario_prefix()
    {
        var def = Definition(id: "s", display: "patient booking", nodes: [Node(0, "a", "patient exists")]);
        var (reporter, bus) = NewReporter(def);

        // The scheduler computes the formatted name at run time (placeholders resolved); the
        // reporter must surface that, not the static template.
        reporter.OnStepStarting(def.Nodes[0], "patient Jane exists");

        var node = Assert.Single(bus.Nodes);
        Assert.Equal("patient booking ▸ patient Jane exists", node.DisplayName);
    }

    [Fact]
    public void Passed_step_publishes_passed_state()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        reporter.OnStepFinished(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Passed,
            Duration = TimeSpan.FromMilliseconds(5),
        });

        var node = Assert.Single(bus.Nodes);
        Assert.NotEmpty(node.Properties.OfType<PassedTestNodeStateProperty>());
    }

    [Fact]
    public void Failed_assertion_publishes_failed_state_carrying_the_exception()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        // A genuine xunit.v3.assert failure: its base type is Xunit.Sdk.XunitException, which the
        // reporter must recognize as an assertion (Failed), not a generic Error.
        var ex = Assert.ThrowsAny<Exception>(() => Assert.Equal(1, 2));

        reporter.OnStepFinished(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Failed,
            Exception = ex,
        });

        var node = Assert.Single(bus.Nodes);
        var failed = Assert.Single(node.Properties.OfType<FailedTestNodeStateProperty>());
        Assert.Same(ex, failed.Exception);
        Assert.Empty(node.Properties.OfType<ErrorTestNodeStateProperty>());
        Assert.Empty(node.Properties.OfType<TimeoutTestNodeStateProperty>());
    }

    [Fact]
    public void Failed_timeout_publishes_timeout_state()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);
        var ex = new TimeoutException("step timed out");

        reporter.OnStepFinished(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Failed,
            Exception = ex,
        });

        var node = Assert.Single(bus.Nodes);
        var timeout = Assert.Single(node.Properties.OfType<TimeoutTestNodeStateProperty>());
        Assert.Same(ex, timeout.Exception);
        Assert.Empty(node.Properties.OfType<FailedTestNodeStateProperty>());
    }

    [Fact]
    public void Failed_other_exception_publishes_error_state()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);
        var ex = new InvalidOperationException("boom");

        reporter.OnStepFinished(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Failed,
            Exception = ex,
        });

        var node = Assert.Single(bus.Nodes);
        var error = Assert.Single(node.Properties.OfType<ErrorTestNodeStateProperty>());
        Assert.Same(ex, error.Exception);
        Assert.Empty(node.Properties.OfType<FailedTestNodeStateProperty>());
        Assert.Empty(node.Properties.OfType<TimeoutTestNodeStateProperty>());
    }

    [Fact]
    public void Skipped_step_publishes_skipped_state_with_reason()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        reporter.OnStepFinished(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Skipped,
            SkipReason = "dependency failed: creating an appointment",
        });

        var node = Assert.Single(bus.Nodes);
        var skipped = Assert.Single(node.Properties.OfType<SkippedTestNodeStateProperty>());
        Assert.Equal("dependency failed: creating an appointment", skipped.Explanation);
    }

    [Fact]
    public void Finished_update_carries_timing_property()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        reporter.OnStepStarting(def.Nodes[0], "step a");
        reporter.OnStepFinished(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Passed,
            Duration = TimeSpan.FromMilliseconds(250),
        });

        var finished = bus.Nodes[^1];
        var timing = Assert.Single(finished.Properties.OfType<TimingProperty>());
        Assert.Equal(TimeSpan.FromMilliseconds(250), timing.GlobalTiming.Duration);
    }

    [Fact]
    public void Finished_update_carries_file_location_when_source_is_known()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a", file: @"C:\src\B.cs", line: 12)]);
        var (reporter, bus) = NewReporter(def);

        reporter.OnStepFinished(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Passed,
        });

        var node = Assert.Single(bus.Nodes);
        var location = Assert.Single(node.Properties.OfType<TestFileLocationProperty>());
        Assert.Equal(@"C:\src\B.cs", location.FilePath);
        Assert.Equal(12, location.LineSpan.Start.Line);
    }

    [Fact]
    public void Finished_update_uses_the_results_formatted_display_name()
    {
        var def = Definition(id: "s", display: "booking", nodes: [Node(0, "a", "patient exists")]);
        var (reporter, bus) = NewReporter(def);

        reporter.OnStepFinished(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "patient Jane exists",
            Status = StepStatus.Passed,
        });

        var node = Assert.Single(bus.Nodes);
        Assert.Equal("booking ▸ patient Jane exists", node.DisplayName);
    }

    [Fact]
    public void Logs_surface_as_standard_output_on_the_finished_update()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (reporter, bus) = NewReporter(def);

        reporter.OnStepFinished(new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.Passed,
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
    public void Each_published_update_carries_the_session_uid()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var bus = new RecordingMessageBus();
        var reporter = new PUnitStepReporter(def, new SessionUid("the-session"), bus, new StubProducer());

        reporter.OnStepStarting(def.Nodes[0], "step a");

        var update = Assert.Single(bus.Updates);
        Assert.Equal("the-session", update.SessionUid.Value);
    }

    sealed class StubProducer : IDataProducer
    {
        public Type[] DataTypesProduced => [typeof(TestNodeUpdateMessage)];
        public string Uid => "stub.producer";
        public string Version => "1.0.0";
        public string DisplayName => "stub";
        public string Description => "stub data producer for reporter tests";
        public Task<bool> IsEnabledAsync() => Task.FromResult(true);
    }

    sealed class RecordingMessageBus : IMessageBus
    {
        readonly List<TestNodeUpdateMessage> updates = [];

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
}
