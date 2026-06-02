using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace PUnit.Generator.Test;

/// <summary>
/// Snapshots of the generated source so the lowering output stays easy to review and changes are
/// caught. Behavioral correctness is covered by the *LoweringTests; these guard the exact shape.
/// </summary>
public class GeneratorSnapshotTests
{
    [Fact]
    public Task Linear_scenario() =>
        Verify(GeneratorHarness.RunDriver(SampleSources.Dsl + SampleSources.LinearScenario))
            .UseDirectory("Snapshots");

    [Fact]
    public Task Tuple_scenario() =>
        Verify(GeneratorHarness.RunDriver(SampleSources.Dsl + SampleSources.TupleScenario))
            .UseDirectory("Snapshots");

    [Fact]
    public Task Array_scenario() =>
        Verify(GeneratorHarness.RunDriver(SampleSources.Dsl + SampleSources.ArrayScenario))
            .UseDirectory("Snapshots");
}
