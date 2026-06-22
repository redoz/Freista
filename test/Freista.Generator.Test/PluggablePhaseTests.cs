using Freista.Model;
using Xunit;

namespace Freista.Generator.Test;

/// <summary>A custom type implementing Freista.IPhase is recognised as a phase marker, just like the
/// built-in Given/When/Then, and its type name becomes the step's phase label.</summary>
public class PluggablePhaseTests
{
    private const string CustomPhaseSource =
        """
        using System.Threading.Tasks;
        using Freista;

        namespace Demo;

        public sealed class Arrange : IPhase { private Arrange() { } }

        public sealed record Widget(int Id);

        public static class CustomDsl
        {
            extension(Arrange)
            {
                [StepName("a widget exists")]
                public static async Task<Widget> WidgetExists()
                {
                    await Task.Yield();
                    return new Widget(1);
                }
            }
        }

        public static class CustomScenarios
        {
            [Scenario("custom phase")]
            public static async Task S()
            {
                await Arrange.WidgetExists();
            }
        }
        """;

    [Fact]
    public void Custom_IPhase_marker_is_recognised_and_names_the_phase()
    {
        var result = GeneratorHarness.Run(CustomPhaseSource);
        result.AssertCompiles();

        var def = Assert.Single(result.Definitions());
        var node = Assert.Single(def.Nodes);
        Assert.Equal("Arrange", node.Phase);
        Assert.Equal("a widget exists", node.DisplayNameTemplate);
    }
}
