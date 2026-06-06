using PUnit.Model;
using Xunit;

namespace PUnit.Generator.Test;

/// <summary>
/// The generator lowers resource role attributes ([Creates]/[Edits]/[Reads] on a step's return value
/// or parameters) into <c>ctx.Resources.*</c> verb calls, so a step's recorded effect stream reflects
/// its declared roles. A role-free scenario must emit no resource calls at all.
/// </summary>
public class ResourceLoweringTests
{
    private static async Task<IReadOnlyList<StepResult>> RunResourceScenario()
    {
        var result = GeneratorHarness.Run(SampleSources.ResourceDsl + SampleSources.ResourceScenario);
        result.AssertCompiles();
        return await result.Definitions().Single().RunAsync();
    }

    [Fact]
    public async Task Return_creates_records_a_create_effect()
    {
        var results = await RunResourceScenario();

        // Given.UserExists → [return: Creates]
        var effect = Assert.Single(results[0].Effects);
        Assert.Equal(LifecycleVerb.Create, effect.Verb);
        Assert.Equal("User:jane@acme.com", effect.Identity.ToString());
    }

    [Fact]
    public async Task Param_and_return_edits_dedup_to_one_edit_effect()
    {
        var results = await RunResourceScenario();

        // When.Suspend([Edits] User) → [return: Edits]; both resolve to the same identity ⇒ one effect.
        var effect = Assert.Single(results[1].Effects);
        Assert.Equal(LifecycleVerb.Edit, effect.Verb);
        Assert.Equal("User:jane@acme.com", effect.Identity.ToString());
    }

    [Fact]
    public async Task Param_reads_records_a_read_effect()
    {
        var results = await RunResourceScenario();

        // Then.CannotSignIn([Reads] User)
        var effect = Assert.Single(results[2].Effects);
        Assert.Equal(LifecycleVerb.Read, effect.Verb);
        Assert.Equal("User:jane@acme.com", effect.Identity.ToString());
    }

    [Fact]
    public void Role_free_scenario_emits_no_resource_calls()
    {
        var result = GeneratorHarness.Run(SampleSources.Dsl + SampleSources.LinearScenario);
        result.AssertCompiles();
        Assert.DoesNotContain(".Resources.", result.GeneratedSource);
    }

    [Fact]
    public async Task Multi_param_roles_emit_in_param_then_return_order()
    {
        var result = GeneratorHarness.Run(SampleSources.ResourceDsl + SampleSources.BookingScenario);
        result.AssertCompiles();
        var results = await result.Definitions().Single().RunAsync();

        // Step 0: Given.UserExists → single [return: Creates] on User:jane@acme.com.
        var userCreate = Assert.Single(results[0].Effects);
        Assert.Equal(LifecycleVerb.Create, userCreate.Verb);
        Assert.Equal("User:jane@acme.com", userCreate.Identity.ToString());

        // Step 1: Given.SlotExists → single [return: Creates] on Slot:1.
        var slotCreate = Assert.Single(results[1].Effects);
        Assert.Equal(LifecycleVerb.Create, slotCreate.Verb);
        Assert.Equal("Slot:1", slotCreate.Identity.ToString());

        // Step 2: When.Book([Reads] User, [Edits] Slot) [return: Creates] → effects must appear in
        // exactly this order: each role-bearing parameter in declaration order, THEN the return role.
        var book = results[2].Effects;
        Assert.Equal(3, book.Count);

        Assert.Equal(LifecycleVerb.Read, book[0].Verb);
        Assert.Equal("User:jane@acme.com", book[0].Identity.ToString());

        Assert.Equal(LifecycleVerb.Edit, book[1].Verb);
        Assert.Equal("Slot:1", book[1].Identity.ToString());

        Assert.Equal(LifecycleVerb.Create, book[2].Verb);
        Assert.Equal("Appointment:jane@acme.com@1", book[2].Identity.ToString());
    }
}
