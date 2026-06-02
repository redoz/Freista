using PUnit.Model;
using PUnit.Scheduling;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace PUnit.Xunit.Test;

/// <summary>
/// Deterministically verifies the step-reporter maps scheduler outcomes onto the xUnit message
/// stream: each step emits start → result → finish, with pass/fail/skip mapped correctly and skip
/// reasons preserved. Uses a collecting message bus so no live failing run is needed.
/// </summary>
public class ScenarioReporterTests
{
    static ScenarioNode Node(int index, Func<IStepInputs, ScenarioContext, Task<object?>> invoke, int[]? deps = null) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "Given",
        OperationName = $"Op{index}",
        DisplayNameTemplate = $"op {index}",
        DependsOn = deps ?? [],
        Invoke = invoke,
    };

    [Fact]
    public async Task Emits_pass_fail_skip_per_step()
    {
        var definition = new ScenarioDefinition
        {
            ScenarioId = "scn",
            DisplayName = "scenario",
            MethodName = "Ns.Scn",
            Nodes =
            [
                Node(0, (_, _) => Task.FromResult<object?>(null)),
                Node(1, (_, _) => throw new InvalidOperationException("boom"), [0]),
                Node(2, (_, _) => Task.FromResult<object?>(null), [1]),     // depends on the failure -> skipped
                Node(3, (_, _) => Task.FromResult<object?>(null), [0]),     // independent -> passes
            ],
        };

        var bus = new CollectingBus();
        var reporter = new ScenarioStepReporter(bus, "asm", "col", "cls", "meth", "case", "scenario");

        await new ScenarioScheduler().RunAsync(definition, observer: reporter);

        Assert.Equal(4, bus.Messages.OfType<ITestStarting>().Count());
        Assert.Equal(4, bus.Messages.OfType<ITestFinished>().Count());
        Assert.Equal(2, bus.Messages.OfType<ITestPassed>().Count());
        Assert.Single(bus.Messages.OfType<ITestFailed>());

        var skipped = Assert.Single(bus.Messages.OfType<ITestSkipped>());
        Assert.Contains("dependency failed", skipped.Reason);
    }

    [Fact]
    public async Task Step_display_name_includes_step_and_scenario()
    {
        var definition = new ScenarioDefinition
        {
            ScenarioId = "scn",
            DisplayName = "scenario",
            MethodName = "Ns.Scn",
            Nodes = [Node(0, (_, _) => Task.FromResult<object?>(null))],
        };

        var bus = new CollectingBus();
        var reporter = new ScenarioStepReporter(bus, "asm", "col", "cls", "meth", "case", "my scenario");

        await new ScenarioScheduler().RunAsync(definition, observer: reporter);

        var starting = Assert.Single(bus.Messages.OfType<ITestStarting>());
        Assert.Contains("my scenario", starting.TestDisplayName);
        Assert.Contains("op 0", starting.TestDisplayName);
    }

    sealed class CollectingBus : IMessageBus
    {
        public List<IMessageSinkMessage> Messages { get; } = [];

        public bool QueueMessage(IMessageSinkMessage message)
        {
            lock (Messages)
            {
                Messages.Add(message);
            }

            return true;
        }

        public void Dispose()
        {
        }
    }
}
