using Xunit;

namespace PUnit.Generator.Test;

public class HarnessPdbTests
{
    [Fact]
    public void EmitWithPdb_linear_compiles_and_yields_visible_sequence_points()
    {
        var (errors, pdb) = GeneratorHarness.EmitWithPdb(
            SampleSources.Dsl + SampleSources.LinearScenario, "Scenario.cs");

        Assert.True(errors.IsEmpty, string.Join("; ", errors));
        var points = GeneratorHarness.ReadSequencePoints(pdb);
        Assert.NotEmpty(points);
        Assert.Contains(points, p => !p.IsHidden);     // user code alone produces visible points
    }
}
