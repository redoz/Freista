using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Freista.Generator.Test;

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
        var analyzer = new Freista.Generator.Analysis.ScenarioAnalyzer();

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
    public async Task FRST003_loops_are_still_rejected()
    {
        var diagnostics = await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    foreach (var i in new[] { 1, 2 }) { await Given.AvailableSlot(); }
                }
            }
            """);

        AssertHas(diagnostics, "FRST003");
    }

    [Fact]
    public async Task FRST003_while_switch_and_try_are_still_rejected()
    {
        AssertHas(await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    while (true) { await Given.AvailableSlot(); }
                }
            }
            """), "FRST003");

        AssertHas(await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    try { await Given.AvailableSlot(); } catch { }
                }
            }
            """), "FRST003");
    }

    [Fact]
    public async Task FRST003_message_points_at_putting_the_loop_inside_a_step()
    {
        var diagnostics = await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    for (var i = 0; i < 2; i++) { await Given.AvailableSlot(); }
                }
            }
            """);

        var loop = Assert.Single(diagnostics, d => d.Id == "FRST003");
        Assert.Contains(
            "inside a step",
            loop.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FRST003_no_longer_fires_on_a_supported_if()
    {
        var diagnostics = await GeneratorHarness.AnalyzeAsync(
            SampleSources.ConditionalDsl + SampleSources.IfElseScenario);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FRST003");
    }

    [Fact]
    public void FRST011_and_FRST012_are_supported_diagnostics()
    {
        var analyzer = new Freista.Generator.Analysis.ScenarioAnalyzer();

        Assert.Contains(analyzer.SupportedDiagnostics, d => d.Id == "FRST011");
        Assert.Contains(analyzer.SupportedDiagnostics, d => d.Id == "FRST012");
    }

    [Fact]
    public async Task Supported_conditional_scenarios_produce_no_diagnostics()
    {
        Assert.Empty(await GeneratorHarness.AnalyzeAsync(
            SampleSources.ConditionalDsl + SampleSources.IfElseScenario));
        Assert.Empty(await GeneratorHarness.AnalyzeAsync(
            SampleSources.ConditionalDsl + SampleSources.BareIfScenario));
        Assert.Empty(await GeneratorHarness.AnalyzeAsync(
            SampleSources.ConditionalDsl + SampleSources.NestedIfScenario));
        Assert.Empty(await GeneratorHarness.AnalyzeAsync(
            SampleSources.ConditionalDsl + SampleSources.OperatorTrueScenario));
        Assert.Empty(await GeneratorHarness.AnalyzeAsync(
            SampleSources.ConditionalDsl + SampleSources.ConditionalOverwriteScenario));
    }

    [Fact]
    public async Task FRST011_bare_expression_condition()
    {
        var source = SampleSources.ConditionalDsl +
            """

            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    var patient = await Given.PatientExists("Jane");
                    if (patient.Name.Length > 3)
                        await When.Notify(patient);
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST011");
    }

    [Fact]
    public async Task FRST011_awaited_non_dsl_condition()
    {
        var source = SampleSources.ConditionalDsl +
            """

            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    var patient = await Given.PatientExists("Jane");
                    if (await Task.FromResult(true))
                        await When.Notify(patient);
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST011");
    }

    [Fact]
    public async Task FRST011_condition_result_is_not_usable_as_a_condition()
    {
        // Patient has no conversion to bool and no operator true, so it cannot drive an `if`.
        var source = SampleSources.ConditionalDsl +
            """

            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    var patient = await Given.PatientExists("Jane");
                    if (await Given.PatientExists("Bob"))
                        await When.Notify(patient);
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST011");
    }

    [Fact]
    public async Task FRST012_conditional_assignment_to_a_non_step_local()
    {
        // `appointment` is initialized by a non-step expression, so the merge has no parent NODE to
        // merge against — only an initializer.
        var source = SampleSources.ConditionalDsl +
            """

            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    var patient = await Given.PatientExists("Jane");
                    Appointment appointment = null!;
                    if (await Given.IsPriority())
                        appointment = await When.CreateUrgent(patient);

                    await Then.AppointmentExists(appointment);
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST012");
    }

    [Fact]
    public async Task FRST012_does_not_fire_on_reassignment_within_one_arm()
    {
        // Two definitions inside the SAME arm are fine — the definition map keeps the last one.
        var source = SampleSources.ConditionalDsl +
            """

            public static class S
            {
                [Scenario("double assign")] public static async Task Ok()
                {
                    var patient = await Given.PatientExists("Jane");
                    var appointment = await When.CreateStandard(patient);
                    if (await Given.IsPriority())
                    {
                        appointment = await When.CreateUrgent(patient);
                        appointment = await When.CreateUrgent(patient);
                    }

                    await Then.AppointmentExists(appointment);
                }
            }
            """;

        Assert.DoesNotContain(await GeneratorHarness.AnalyzeAsync(source), d => d.Id == "FRST012");
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
            using Freista;
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
            using Freista;
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
        var analyzer = new Freista.Generator.Analysis.ScenarioAnalyzer();

        Assert.Contains(analyzer.SupportedDiagnostics, d => d.Id == "FRST009");
    }

    [Fact]
    public async Task FRST009_unannotated_resource_parameter()
    {
        // A resource-typed parameter with no [Read]/[Edited]/[Deleted] role — there is no default.
        var source =
            """
            using System.Threading.Tasks;
            using Freista;
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
            using Freista;
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
        var analyzer = new Freista.Generator.Analysis.ScenarioAnalyzer();
        Assert.Contains(analyzer.SupportedDiagnostics, d => d.Id == "FRST010");
    }

    private const string LineageDsl =
        """
        using System.Threading.Tasks;
        using Freista;
        namespace Bad;
        public sealed record User(string Email) : IResource<User> { public static ResourceKey KeyFor(User i) => i.Email; }
        public sealed record Account(string Id) : IResource<Account> { public static ResourceKey KeyFor(Account i) => i.Id; }

        """;

    [Fact]
    public async Task FRST010_unknown_target_name()
    {
        var source = LineageDsl +
            """
            public static class BadDsl
            {
                extension(When)
                {
                    [StepName("transfer")]
                    [return: Created(References = [nameof(who), "ghost"])]
                    public static async Task<Account> Transfer(User who) { await Task.Yield(); return new Account("a"); }
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST010");
    }

    [Fact]
    public async Task FRST010_return_target_without_a_subject_return()
    {
        // An [Edited] parameter naming Subject.Return as a target, but the step yields no resource.
        var source = LineageDsl +
            """
            public static class BadDsl
            {
                extension(When)
                {
                    [StepName("look up")]
                    public static async Task LookUp([Edited(References = [Subject.Return])] Account acc) { await Task.Yield(); }
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST010");
    }

    [Fact]
    public async Task FRST010_self_reference()
    {
        // A produced subject may not name itself as a lineage target.
        var source = LineageDsl +
            """
            public static class BadDsl
            {
                extension(When)
                {
                    [StepName("clone")]
                    [return: Created(References = [Subject.Return])]
                    public static async Task<Account> Clone() { await Task.Yield(); return new Account("a"); }
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST010");
    }

    [Fact]
    public async Task FRST010_clean_for_valid_targets()
    {
        var source = LineageDsl +
            """
            public static class GoodDsl
            {
                extension(When)
                {
                    [StepName("assign")]
                    public static async Task Assign([Edited(References = [nameof(who)])] Account acc, User who) { await Task.Yield(); }

                    [StepName("create")]
                    [return: Created(References = [nameof(who)])]
                    public static async Task<Account> Create(User who) { await Task.Yield(); return new Account("a"); }
                }
            }
            """;

        Assert.DoesNotContain(await GeneratorHarness.AnalyzeAsync(source), d => d.Id == "FRST010");
    }

    // A DSL whose steps declare every kind of parameter access on one resource type, so a scenario
    // can put any two of them side by side in a parallel group.
    private const string ConflictDsl =
        """
        using System.Linq;
        using System.Threading.Tasks;
        using Freista;
        namespace Conflicts;
        public sealed record Patient(string Name) : IResource<Patient>
        {
            public static ResourceKey KeyFor(Patient instance) => instance.Name;
        }
        public sealed record Note(string Text) : IResource<Note>
        {
            public static ResourceKey KeyFor(Note instance) => instance.Text;
        }
        public static class ConflictDsl
        {
            extension(Given)
            {
                [StepName("patient {name} exists")]
                [return: Created]
                public static async Task<Patient> PatientExists(string name) { await Task.Yield(); return new Patient(name); }
            }
            extension(When)
            {
                [StepName("renaming the patient")]
                public static async Task Rename([Edited] Patient patient, string name) { await Task.Yield(); }

                [StepName("suspending the patient")]
                public static async Task Suspend([Edited] Patient patient) { await Task.Yield(); }

                [StepName("deleting the patient")]
                public static async Task Delete([Deleted] Patient patient) { await Task.Yield(); }

                [StepName("attaching a note")]
                [return: Created(References = [nameof(patient)])]
                public static async Task<Note> AttachNote(Patient patient, string text) { await Task.Yield(); return new Note(text); }

                [StepName("tagging the patient {tag}")]
                [return: Created]
                public static async Task<Note> Tag([Edited] Patient patient, int tag) { await Task.Yield(); return new Note($"tag-{tag}"); }
            }
            extension(Then)
            {
                [StepName("the patient can sign in")]
                public static Task CanSignIn([Read] Patient patient) => Task.CompletedTask;

                [StepName("the patient has a name")]
                public static Task HasName([Read] Patient patient) => Task.CompletedTask;
            }
        }
        """;

    private static Task<ImmutableArray<Diagnostic>> AnalyzeConflict(string body) =>
        GeneratorHarness.AnalyzeAsync(ConflictDsl +
            $$"""
            public static class S
            {
                [Scenario("s")]
                public static async Task Run()
                {
                    var patient = await Given.PatientExists("Jane");
                    var other = await Given.PatientExists("Bob");
            {{body}}
                }
            }
            """);

    [Fact]
    public void FRST013_is_a_supported_diagnostic()
    {
        var analyzer = new Freista.Generator.Analysis.ScenarioAnalyzer();

        Assert.Contains(analyzer.SupportedDiagnostics, d => d.Id == "FRST013");
    }

    [Fact]
    public async Task FRST013_two_parallel_mutations_of_one_local()
    {
        var diagnostics = await AnalyzeConflict(
            """
                    await (When.Rename(patient, "J"), When.Suspend(patient));
            """);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "FRST013");
        var message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("Rename", message);
        Assert.Contains("Suspend", message);
        Assert.Contains("'patient'", message);
    }

    [Fact]
    public async Task FRST013_parallel_mutation_and_read_of_one_local()
    {
        var diagnostics = await AnalyzeConflict(
            """
                    await (When.Suspend(patient), Then.CanSignIn(patient));
            """);

        AssertHas(diagnostics, "FRST013");
    }

    [Fact]
    public async Task FRST013_lineage_target_conflicts_with_a_parallel_mutation()
    {
        var diagnostics = await AnalyzeConflict(
            """
                    var notes = await new[] { When.AttachNote(patient, "a"), When.Tag(patient, 1) };
            """);

        // AttachNote's References confers a shared role on `patient`; Tag mutates it.
        AssertHas(diagnostics, "FRST013");
    }

    [Fact]
    public async Task FRST013_clean_for_parallel_reads()
    {
        var diagnostics = await AnalyzeConflict(
            """
                    await (Then.CanSignIn(patient), Then.HasName(patient));
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FRST013");
    }

    [Fact]
    public async Task FRST013_clean_for_lineage_target_beside_a_read()
    {
        var diagnostics = await AnalyzeConflict(
            """
                    var notes = await new[] { When.AttachNote(patient, "a"), When.AttachNote(patient, "b") };
                    await Then.CanSignIn(patient);
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FRST013");
    }

    [Fact]
    public async Task FRST013_clean_for_different_locals()
    {
        var diagnostics = await AnalyzeConflict(
            """
                    await (When.Suspend(patient), When.Suspend(other));
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FRST013");
    }

    [Fact]
    public async Task FRST013_clean_for_sequential_mutations()
    {
        var diagnostics = await AnalyzeConflict(
            """
                    await When.Rename(patient, "J");
                    await When.Suspend(patient);
                    await When.Delete(patient);
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FRST013");
    }

    [Fact]
    public async Task FRST013_linq_unroll_mutating_an_outer_local()
    {
        var diagnostics = await AnalyzeConflict(
            """
                    var tags = await Enumerable.Range(1, 2).Select(i => When.Tag(patient, i)).ToArray();
            """);

        AssertHas(diagnostics, "FRST013");
    }

    [Fact]
    public async Task FRST013_linq_unroll_with_one_element_is_clean()
    {
        var diagnostics = await AnalyzeConflict(
            """
                    var tags = await Enumerable.Range(1, 1).Select(i => When.Tag(patient, i)).ToArray();
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FRST013");
    }

    [Fact]
    public async Task FRST013_matches_named_arguments()
    {
        var diagnostics = await AnalyzeConflict(
            """
                    await (When.Rename(name: "J", patient: patient), When.Suspend(patient: patient));
            """);

        AssertHas(diagnostics, "FRST013");
    }

    [Fact]
    public async Task FRST013_does_not_fire_on_the_resource_sample_scenarios()
    {
        foreach (var scenario in new[] { SampleSources.ResourceScenario, SampleSources.BookingScenario, SampleSources.LineageScenario })
        {
            var diagnostics = await GeneratorHarness.AnalyzeAsync(SampleSources.ResourceDsl + scenario);
            Assert.DoesNotContain(diagnostics, d => d.Id == "FRST013");
        }
    }

    /// <summary>A step body around <paramref name="registration"/>, which sees `ctx` (the step's own
    /// nullable context) and `id` (a plain captured value).</summary>
    private static Task<ImmutableArray<Diagnostic>> AnalyzeCleanup(string registration) =>
        GeneratorHarness.AnalyzeAsync(
            $$"""
            using System.Threading.Tasks;
            using Freista;
            namespace Cleanups;
            public static class Db
            {
                public static Task Delete(int id) => Task.CompletedTask;
            }
            public static class CleanupDsl
            {
                extension(Given)
                {
                    [StepName("a row exists")]
                    public static Task<int> RowExists(ScenarioContext? ctx = null)
                    {
                        var id = 42;
            {{registration}}
                        return Task.FromResult(id);
                    }
                }
            }
            """);

    [Fact]
    public void FRST014_is_a_supported_diagnostic()
    {
        var analyzer = new Freista.Generator.Analysis.ScenarioAnalyzer();

        Assert.Contains(analyzer.SupportedDiagnostics, d => d.Id == "FRST014");
    }

    [Fact]
    public async Task FRST014_captured_step_context_in_a_parameterless_cleanup()
    {
        var diagnostics = await AnalyzeCleanup(
            """
                        ctx?.OnTeardown(() =>
                        {
                            ctx.Log("deleting");
                            return Db.Delete(id);
                        });
            """);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "FRST014");
        Assert.Contains("'ctx'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FRST014_captured_step_context_beside_the_teardown_parameter()
    {
        var diagnostics = await AnalyzeCleanup(
            """
                        ctx?.OnTeardown(teardown =>
                        {
                            ctx.Log("deleting");
                            return Db.Delete(id);
                        });
            """);

        AssertHas(diagnostics, "FRST014");
    }

    [Fact]
    public async Task FRST014_flags_the_kind_overload_and_a_captured_context_local()
    {
        var diagnostics = await AnalyzeCleanup(
            """
                        var step = ctx;
                        ctx?.OnTeardown(Cleanup.Required, () =>
                        {
                            step?.AddAttachment("deleted", "row");
                            return Db.Delete(id);
                        });
            """);

        AssertHas(diagnostics, "FRST014");
    }

    [Fact]
    public async Task FRST014_clean_when_the_teardown_context_is_used()
    {
        var diagnostics = await AnalyzeCleanup(
            """
                        ctx?.OnTeardown(teardown =>
                        {
                            teardown.Log("deleting");
                            teardown.AddAttachment("deleted", "row");
                            return Db.Delete(id);
                        });
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FRST014");
    }

    [Fact]
    public async Task FRST014_clean_for_a_cleanup_that_touches_no_context()
    {
        var diagnostics = await AnalyzeCleanup(
            """
                        ctx?.OnTeardown(() => Db.Delete(id));
                        ctx?.OnTeardown(Cleanup.Required, () => Db.Delete(id));
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FRST014");
    }

    [Fact]
    public async Task FRST014_clean_for_the_ambient_context_inside_the_cleanup()
    {
        // ScenarioContext.Current IS the teardown context while cleanups run.
        var diagnostics = await AnalyzeCleanup(
            """
                        ctx?.OnTeardown(() =>
                        {
                            ScenarioContext.Current?.Log("deleting");
                            return Db.Delete(id);
                        });
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FRST014");
    }

    [Fact]
    public async Task FRST014_does_not_fire_on_context_use_outside_the_cleanup()
    {
        var diagnostics = await AnalyzeCleanup(
            """
                        ctx?.Log("created");
                        ctx?.OnTeardown(() => Db.Delete(id));
                        ctx?.Log("registered");
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == "FRST014");
    }
}
