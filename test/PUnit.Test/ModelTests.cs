using PUnit;
using PUnit.Model;
using Xunit;

namespace PUnit.Test;

/// <summary>
/// The runner-neutral scenario graph model and stable identity scheme. Validation guards the DAG
/// invariants the scheduler relies on; ids must be deterministic and never derived from line
/// numbers so reports stay stable across edits above the step.
/// </summary>
public class ModelTests
{
    private static ScenarioNode Node(int index, params int[] dependsOn) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "Given",
        OperationName = $"Op{index}",
        DisplayNameTemplate = $"op {index}",
        DependsOn = dependsOn,
        Invoke = (_, _) => Task.FromResult<object?>(null),
    };

    private static ScenarioDefinition Def(params ScenarioNode[] nodes) => new()
    {
        ScenarioId = "scn",
        DisplayName = "scenario",
        MethodName = "Ns.Scn",
        Nodes = nodes,
    };

    [Fact]
    public void Validate_accepts_a_linear_graph()
    {
        var def = Def(Node(0), Node(1, 0), Node(2, 1));

        def.Validate(); // does not throw
    }

    [Fact]
    public void Validate_rejects_a_dependency_cycle()
    {
        var def = Def(Node(0, 1), Node(1, 0));

        var ex = Assert.Throws<InvalidOperationException>(def.Validate);
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_rejects_out_of_range_dependency()
    {
        var def = Def(Node(0), Node(1, 5));

        Assert.Throws<InvalidOperationException>(def.Validate);
    }

    [Fact]
    public void Scenario_id_is_deterministic_for_the_same_method()
    {
        Assert.Equal(StableId.ForScenario("Ns.Booking"), StableId.ForScenario("Ns.Booking"));
    }

    [Fact]
    public void Scenario_id_differs_by_method()
    {
        Assert.NotEqual(StableId.ForScenario("Ns.Booking"), StableId.ForScenario("Ns.Import"));
    }

    [Fact]
    public void Step_id_is_deterministic_and_keyed()
    {
        var a = StableId.ForStep("scn", "PatientExists:0");
        var b = StableId.ForStep("scn", "PatientExists:0");
        var c = StableId.ForStep("scn", "AvailableSlot:1");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEmpty(a);
    }
}
