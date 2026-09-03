using Freista.Model;
using Xunit;

namespace Freista.Generator.Test;

/// <summary>
/// `if`/`else` lowers into guarded nodes plus synthetic merge (phi) nodes. `DependsOn` keeps its
/// all-of meaning throughout: a following statement never depends on an arm's node, only on the
/// condition or on a merge.
/// </summary>
public class ConditionalLoweringTests
{
    private static ScenarioDefinition Lower(string scenario)
    {
        var result = GeneratorHarness.Run(SampleSources.ConditionalDsl + scenario);
        result.AssertCompiles();
        return Assert.Single(result.Definitions());
    }

    [Fact]
    public void If_else_lowers_condition_arms_and_a_merge()
    {
        var def = Lower(SampleSources.IfElseScenario);

        // 0 PatientExists, 1 IsPriority (condition), 2 CreateUrgent, 3 CreateStandard,
        // 4 «merge appointment», 5 AppointmentExists
        Assert.Equal(6, def.Nodes.Count);

        Assert.NotNull(def.Nodes[1].EvaluateCondition);
        Assert.Equal([new Guard(1, true)], def.Nodes[2].Guards);
        Assert.Equal([new Guard(1, false)], def.Nodes[3].Guards);

        Assert.True(def.Nodes[4].IsSynthetic);
        Assert.Equal([2, 3], def.Nodes[4].MergeSources);
        Assert.Empty(def.Nodes[4].DependsOn);

        // The consumer joins on the merge, never on an arm.
        Assert.Equal([4], def.Nodes[5].DependsOn);
        Assert.Empty(def.Nodes[5].Guards);
    }

    [Fact]
    public void Condition_node_is_an_ordinary_discoverable_step()
    {
        var def = Lower(SampleSources.IfElseScenario);

        Assert.False(def.Nodes[1].IsSynthetic);
        Assert.Equal("IsPriority", def.Nodes[1].OperationName);
        Assert.Equal("the patient is priority", def.Nodes[1].DisplayNameTemplate);
        Assert.Equal("Given", def.Nodes[1].Phase);
    }

    [Fact]
    public void Bare_if_guards_the_arm_and_inserts_no_merge()
    {
        var def = Lower(SampleSources.BareIfScenario);

        // 0 PatientExists, 1 IsPriority, 2 Notify — nothing is assigned, so there is no phi.
        Assert.Equal(3, def.Nodes.Count);
        Assert.Equal([new Guard(1, true)], def.Nodes[2].Guards);
        Assert.DoesNotContain(def.Nodes, n => n.IsSynthetic);
    }

    [Fact]
    public void Conditional_overwrite_merges_the_arm_against_a_pass_through_of_the_parent()
    {
        var def = Lower(SampleSources.ConditionalOverwriteScenario);

        // 0 PatientExists, 1 CreateStandard (parent def), 2 IsPriority, 3 CreateUrgent (arm),
        // 4 pass-through of 1 guarded false, 5 «merge appointment», 6 AppointmentExists
        Assert.Equal(7, def.Nodes.Count);

        Assert.Equal([new Guard(2, true)], def.Nodes[3].Guards);

        Assert.True(def.Nodes[4].IsSynthetic);
        Assert.Equal([new Guard(2, false)], def.Nodes[4].Guards);
        Assert.Equal([1], def.Nodes[4].MergeSources);

        Assert.True(def.Nodes[5].IsSynthetic);
        Assert.Equal([3, 4], def.Nodes[5].MergeSources);
        Assert.Equal([5], def.Nodes[6].DependsOn);
    }

    [Fact]
    public void Nested_ifs_stack_guards()
    {
        var def = Lower(SampleSources.NestedIfScenario);

        // 0 PatientExists, 1 IsPriority, 2 HasCapacity, 3 Notify
        Assert.Equal([new Guard(1, true)], def.Nodes[2].Guards);
        Assert.Equal([new Guard(1, true), new Guard(2, true)], def.Nodes[3].Guards);
    }

    [Fact]
    public async Task If_arm_runs_and_else_arm_is_not_taken_end_to_end()
    {
        var result = GeneratorHarness.Run(SampleSources.ConditionalDsl + SampleSources.IfElseScenario);
        result.AssertCompiles();

        var results = await result.Definitions().Single().RunAsync();

        Assert.Equal(StepStatus.Passed, results[2].Status);      // CreateUrgent (IsPriority == true)
        Assert.Equal(StepStatus.NotTaken, results[3].Status);    // CreateStandard
        Assert.Equal(StepStatus.Passed, results[4].Status);      // merge
        Assert.Equal(StepStatus.Passed, results[5].Status);      // AppointmentExists
    }

    [Fact]
    public async Task Conditional_overwrite_executes_and_the_consumer_sees_the_arm_value()
    {
        var result = GeneratorHarness.Run(
            SampleSources.ConditionalDsl + SampleSources.ConditionalOverwriteScenario);
        result.AssertCompiles();

        var results = await result.Definitions().Single().RunAsync();

        Assert.All(results, r => Assert.True(
            r.Status is StepStatus.Passed or StepStatus.NotTaken,
            $"step {r.Node.Index} was {r.Status}: {r.SkipReason}{r.Exception}"));
        Assert.Equal(StepStatus.NotTaken, results[4].Status);   // pass-through (condition was true)
        Assert.Equal(StepStatus.Passed, results[5].Status);     // merge took the arm value
    }

    [Fact]
    public async Task Operator_true_condition_gates_the_arm_end_to_end()
    {
        // Capacity is not bool; it defines `operator true`. The generator emits
        // `static o => ((Capacity)o!) ? true : false`, so Roslyn — not the scheduler — resolves it.
        var result = GeneratorHarness.Run(
            SampleSources.ConditionalDsl + SampleSources.OperatorTrueScenario);
        result.AssertCompiles();

        var results = await result.Definitions().Single().RunAsync();

        Assert.Equal(StepStatus.Passed, results[1].Status);   // HasCapacity
        Assert.Equal(StepStatus.Passed, results[2].Status);   // Notify ran: the guard held
    }

    [Fact]
    public async Task Else_if_chains_give_n_way_routing_without_switch_support()
    {
        // An `else if` is an IfStatementSyntax inside the else arm, so it recurses through ParseIf:
        // guards stack, merges chain, and exactly one arm runs. This is why a switch expression would
        // be ergonomics rather than new capability.
        var result = GeneratorHarness.Run(
            SampleSources.ConditionalDsl + SampleSources.ElseIfChainScenario);
        result.AssertCompiles();
        var def = Assert.Single(result.Definitions());

        var results = await def.RunAsync();

        Assert.All(results, r => Assert.True(
            r.Status is StepStatus.Passed or StepStatus.NotTaken,
            $"step {r.Node.Index} ({r.Node.OperationName}) was {r.Status}: {r.SkipReason}{r.Exception}"));

        // IsPriority is true, so exactly the first arm runs and the consumer still gets a value.
        Assert.Single(results, r => r.Node.OperationName == "CreateUrgent" && r.Status == StepStatus.Passed);
        Assert.Equal(StepStatus.Passed, results[^1].Status);
    }
}
