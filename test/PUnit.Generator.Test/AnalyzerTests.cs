using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Xunit;

namespace PUnit.Generator.Test;

/// <summary>Verifies the analyzer flags code outside the lowerable subset and leaves valid code clean.</summary>
public class AnalyzerTests
{
    private static async Task<ImmutableArray<Diagnostic>> Analyze(string scenario) =>
        await GeneratorHarness.AnalyzeAsync(SampleSources.Dsl + scenario);

    private static void AssertHas(ImmutableArray<Diagnostic> diagnostics, string id) =>
        Assert.Contains(diagnostics, d => d.Id == id);

    [Fact]
    public void FRST000_is_a_supported_diagnostic()
    {
        var analyzer = new PUnit.Generator.Analysis.ScenarioAnalyzer();

        Assert.Contains(analyzer.SupportedDiagnostics, d => d.Id == "FRST000");
    }

    [Fact]
    public async Task Valid_scenarios_produce_no_diagnostics()
    {
        Assert.Empty(await Analyze(SampleSources.LinearScenario));
        Assert.Empty(await Analyze(SampleSources.TupleScenario));
        Assert.Empty(await Analyze(SampleSources.ArrayScenario));
        Assert.Empty(await Analyze(SampleSources.LinqScenario));
    }

    [Fact]
    public async Task FRST001_non_async_task_scenario()
    {
        var diagnostics = await Analyze(
            """
            public static class S { [Scenario] public static void Bad() { } }
            """);

        AssertHas(diagnostics, "FRST001");
    }

    [Fact]
    public async Task FRST002_statement_without_await()
    {
        var diagnostics = await Analyze(
            """
            public static class S { [Scenario] public static async Task Bad() { var x = 5; } }
            """);

        AssertHas(diagnostics, "FRST002");
    }

    [Fact]
    public async Task FRST003_control_flow()
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

        AssertHas(diagnostics, "FRST003");
    }

    [Fact]
    public async Task FRST004_non_dsl_await()
    {
        var diagnostics = await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad() { await Task.Delay(1); }
            }
            """);

        AssertHas(diagnostics, "FRST004");
    }

    [Fact]
    public async Task FRST005_invalid_dsl_return_type()
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

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST005");
    }

    [Fact]
    public async Task FRST006_non_dsl_tuple_element()
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

        AssertHas(diagnostics, "FRST006");
    }

    [Fact]
    public async Task FRST007_argument_is_not_a_step_output()
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

        AssertHas(diagnostics, "FRST007");
    }

    [Fact]
    public async Task FRST007_non_step_local_inside_a_compound_argument()
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

        AssertHas(diagnostics, "FRST007");
    }

    [Fact]
    public async Task FRST008_unbound_display_name_placeholder()
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

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST008");
    }

    [Fact]
    public void FRST009_is_a_supported_diagnostic()
    {
        var analyzer = new PUnit.Generator.Analysis.ScenarioAnalyzer();

        Assert.Contains(analyzer.SupportedDiagnostics, d => d.Id == "FRST009");
    }

    [Fact]
    public async Task FRST009_unannotated_resource_parameter()
    {
        // A resource-typed parameter with no [Reads]/[Edits]/[Deletes] role — there is no default.
        var source =
            """
            using System.Threading.Tasks;
            using PUnit;
            namespace Bad;
            public sealed record User(string Email) : IResource<User>
            {
                public static ResourceKey KeyFor(User instance) => instance.Email;
            }
            public static class BadDsl
            {
                extension(When)
                {
                    [StepName("suspending the user")]
                    public static async Task Suspend(User user) { await Task.Yield(); }
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST009");
    }

    [Fact]
    public async Task FRST009_unannotated_resource_return()
    {
        // A resource-typed return with no return role — there is no default.
        var source =
            """
            using System.Threading.Tasks;
            using PUnit;
            namespace Bad;
            public sealed record User(string Email) : IResource<User>
            {
                public static ResourceKey KeyFor(User instance) => instance.Email;
            }
            public static class BadDsl
            {
                extension(Given)
                {
                    [StepName("a user exists")]
                    public static async Task<User> AUser()
                    {
                        await Task.Yield();
                        return new User("jane@acme.com");
                    }
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST009");
    }

    [Fact]
    public async Task FRST009_clean_when_roles_are_declared()
    {
        // Every resource param/return in the resource DSL carries a role attribute.
        var diagnostics = await GeneratorHarness.AnalyzeAsync(
            SampleSources.ResourceDsl + SampleSources.ResourceScenario);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FRST009");
    }

    [Fact]
    public async Task FRST009_does_not_fire_on_non_resource_types()
    {
        // Plain records carry no resource interface, so role-free params/returns are fine.
        var diagnostics = await Analyze(SampleSources.LinearScenario);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FRST009");
    }

    [Fact]
    public void FRST010_is_a_supported_diagnostic()
    {
        var analyzer = new PUnit.Generator.Analysis.ScenarioAnalyzer();
        Assert.Contains(analyzer.SupportedDiagnostics, d => d.Id == "FRST010");
    }

    private const string LineageDsl =
        """
        using System.Threading.Tasks;
        using PUnit;
        namespace Bad;
        public sealed record User(string Email) : IResource<User> { public static ResourceKey KeyFor(User i) => i.Email; }
        public sealed record Account(string Id) : IResource<Account> { public static ResourceKey KeyFor(Account i) => i.Id; }

        """;

    [Fact]
    public async Task FRST010_unknown_subject_name()
    {
        var source = LineageDsl +
            """
            public static class BadDsl
            {
                extension(When)
                {
                    [StepName("transfer")]
                    public static async Task Transfer([Edits] Account acc, [References("ghost")] User who) { await Task.Yield(); }
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST010");
    }

    [Fact]
    public async Task FRST010_subject_names_a_non_subject_role()
    {
        var source = LineageDsl +
            """
            public static class BadDsl
            {
                extension(When)
                {
                    [StepName("transfer")]
                    public static async Task Transfer([Reads] Account acc, [References(nameof(acc))] User who) { await Task.Yield(); }
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST010");
    }

    [Fact]
    public async Task FRST010_return_sentinel_without_a_creating_return()
    {
        var source = LineageDsl +
            """
            public static class BadDsl
            {
                extension(When)
                {
                    [StepName("look up")]
                    public static async Task LookUp([References(Subject.Return)] User who) { await Task.Yield(); }
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST010");
    }

    [Fact]
    public async Task FRST010_clean_for_valid_subjects()
    {
        var source = LineageDsl +
            """
            public static class GoodDsl
            {
                extension(When)
                {
                    [StepName("assign")]
                    public static async Task Assign([Edits] Account acc, [References(nameof(acc))] User who) { await Task.Yield(); }

                    [StepName("create")]
                    [return: Creates]
                    public static async Task<Account> Create([References(Subject.Return)] User who) { await Task.Yield(); return new Account("a"); }

                    [StepName("note")]
                    public static async Task Note([References] User who) { await Task.Yield(); }
                }
            }
            """;

        Assert.DoesNotContain(await GeneratorHarness.AnalyzeAsync(source), d => d.Id == "FRST010");
    }
}
