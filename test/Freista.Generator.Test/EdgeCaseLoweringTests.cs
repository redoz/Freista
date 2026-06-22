using Freista.Model;
using Xunit;

namespace Freista.Generator.Test;

/// <summary>Regression tests for lowering edge cases surfaced in review.</summary>
public class EdgeCaseLoweringTests
{
    [Fact]
    public async Task Named_arguments_rewrite_only_the_value_not_the_label()
    {
        var result = GeneratorHarness.Run(SampleSources.Dsl + SampleSources.NamedArgScenario);

        // Before the fix, the rewriter replaced the `patient:` label too, producing uncompilable code.
        result.AssertCompiles();

        var results = await result.Definitions().Single().RunAsync();
        Assert.All(results, r => Assert.Equal(StepStatus.Passed, r.Status));
    }
}
