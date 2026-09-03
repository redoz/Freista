using Freista.Model;
using Xunit;

namespace Freista.Generator.Test;

/// <summary>
/// Every lowered scenario ends with a discovered teardown node. It is emitted unconditionally: the
/// generator cannot see <c>OnTeardown</c> calls (they are runtime calls inside DSL bodies), so
/// emitting it only when <c>[Teardown]</c> is present would let a registered cleanup fail silently.
/// </summary>
public class TeardownLoweringTests
{
    private static ScenarioDefinition Lower(string scenario)
    {
        var result = GeneratorHarness.Run(SampleSources.Dsl + scenario);
        result.AssertCompiles();
        return Assert.Single(result.Definitions());
    }

    [Fact]
    public void Every_scenario_ends_with_a_teardown_node()
    {
        var def = Lower(SampleSources.LinearScenario);

        var last = def.Nodes[^1];
        Assert.True(last.IsTeardown);
        Assert.False(last.IsSynthetic);
        Assert.Equal("Teardown", last.OperationName);
        Assert.Empty(last.DependsOn);
        Assert.Single(def.Nodes, n => n.IsTeardown);
    }

    [Fact]
    public void Policy_defaults_to_always_without_the_attribute()
    {
        Assert.Equal(Run.Always, Lower(SampleSources.LinearScenario).TeardownPolicy);
    }

    [Fact]
    public void Policy_comes_from_the_attribute()
    {
        Assert.Equal(Run.OnSuccess, Lower(SampleSources.TeardownOnSuccessScenario).TeardownPolicy);
    }

    [Fact]
    public async Task Registered_cleanup_runs_end_to_end()
    {
        var result = GeneratorHarness.Run(SampleSources.TeardownDsl + SampleSources.TeardownScenario);
        result.AssertCompiles();

        var def = Assert.Single(result.Definitions());
        var results = await def.RunAsync();

        Assert.All(results, r => Assert.True(
            r.Status is StepStatus.Passed,
            $"step {r.Node.Index} ({r.Node.OperationName}) was {r.Status}: {r.SkipReason}{r.Exception}"));
        Assert.True(results[^1].Node.IsTeardown);
    }
}
