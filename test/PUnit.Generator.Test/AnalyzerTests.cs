using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Xunit;

namespace PUnit.Generator.Test;

/// <summary>Verifies the analyzer flags code outside the lowerable subset and leaves valid code clean.</summary>
public class AnalyzerTests
{
    static async Task<ImmutableArray<Diagnostic>> Analyze(string scenario) =>
        await GeneratorHarness.AnalyzeAsync(SampleSources.Dsl + scenario);

    static void AssertHas(ImmutableArray<Diagnostic> diagnostics, string id) =>
        Assert.Contains(diagnostics, d => d.Id == id);

    [Fact]
    public async Task Valid_scenarios_produce_no_diagnostics()
    {
        Assert.Empty(await Analyze(SampleSources.LinearScenario));
        Assert.Empty(await Analyze(SampleSources.TupleScenario));
        Assert.Empty(await Analyze(SampleSources.ArrayScenario));
        Assert.Empty(await Analyze(SampleSources.LinqScenario));
    }

    [Fact]
    public async Task PUNIT001_non_async_task_scenario()
    {
        var diagnostics = await Analyze(
            """
            public static class S { [Scenario] public static void Bad() { } }
            """);

        AssertHas(diagnostics, "PUNIT001");
    }

    [Fact]
    public async Task PUNIT002_statement_without_await()
    {
        var diagnostics = await Analyze(
            """
            public static class S { [Scenario] public static async Task Bad() { var x = 5; } }
            """);

        AssertHas(diagnostics, "PUNIT002");
    }

    [Fact]
    public async Task PUNIT003_control_flow()
    {
        var diagnostics = await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    if (1 == 1) { await Given.AvailableSlot(); }
                }
            }
            """);

        AssertHas(diagnostics, "PUNIT003");
    }

    [Fact]
    public async Task PUNIT004_non_dsl_await()
    {
        var diagnostics = await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad() { await Task.Delay(1); }
            }
            """);

        AssertHas(diagnostics, "PUNIT004");
    }

    [Fact]
    public async Task PUNIT005_invalid_dsl_return_type()
    {
        // A DSL member that returns a non-task type.
        var source =
            """
            using System.Threading.Tasks;
            using PUnit;
            namespace Bad;
            public static class BadDsl
            {
                extension(Given)
                {
                    public static int NotATask() => 1;
                }
            }
            public static class S
            {
                [Scenario] public static async Task Bad() { await Given.NotATask(); }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "PUNIT005");
    }

    [Fact]
    public async Task PUNIT006_non_dsl_tuple_element()
    {
        var diagnostics = await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    var (a, b) = await (Given.AvailableSlot(), Task.FromResult(1));
                }
            }
            """);

        AssertHas(diagnostics, "PUNIT006");
    }

    [Fact]
    public async Task PUNIT007_argument_is_not_a_step_output()
    {
        var diagnostics = await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    var name = "Jane";
                    var patient = await Given.PatientExists(name);
                }
            }
            """);

        AssertHas(diagnostics, "PUNIT007");
    }

    [Fact]
    public async Task PUNIT007_non_step_local_inside_a_compound_argument()
    {
        // A bare identifier is flagged, but so must a non-step local buried in an expression —
        // otherwise the generator emits code referencing a local that doesn't exist.
        var diagnostics = await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    var name = "Jane";
                    var patient = await Given.PatientExists(name + "!");
                }
            }
            """);

        AssertHas(diagnostics, "PUNIT007");
    }

    [Fact]
    public async Task PUNIT008_unbound_display_name_placeholder()
    {
        var source =
            """
            using System.Threading.Tasks;
            using PUnit;
            namespace Bad;
            public static class BadDsl
            {
                extension(Given)
                {
                    [StepName("greet {missing}")]
                    public static Task Foo() => Task.CompletedTask;
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "PUNIT008");
    }
}
