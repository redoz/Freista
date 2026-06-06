using System;
using System.Linq;
using System.Threading.Tasks;
using PUnit.Model;
using Xunit;

namespace PUnit.Test.Resources;

/// <summary>
/// Verifies the C1 <see cref="ResourceContext"/> tracer: each lifecycle verb records a
/// <see cref="ResourceEffect"/> with the resolved identity, owning step, and an injected timestamp.
/// </summary>
public class ResourceContextTests
{
    static ResourceContext NewContext(out FixedTimeProvider clock)
    {
        clock = new FixedTimeProvider(new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero));
        return new ResourceContext("step-7", "given a user", new ResourceIdentityResolver(), clock);
    }

    [Fact]
    public async Task Create_records_an_exclusive_effect_with_resolved_identity()
    {
        var ctx = NewContext(out var clock);

        await ctx.Create(new User("admin@acme.com"));

        var effect = Assert.Single(ctx.Effects);
        Assert.Equal(LifecycleVerb.Create, effect.Verb);
        Assert.Equal(LockMode.Exclusive, effect.Mode);
        Assert.Equal(new ResourceIdentity(typeof(User), "admin@acme.com"), effect.Identity);
        Assert.Equal("step-7", effect.StepId);
        Assert.Equal("given a user", effect.StepDisplayName);
        Assert.Equal(clock.GetUtcNow(), effect.Timestamp);
    }

    [Fact]
    public async Task Load_records_a_shared_effect_by_key_with_no_data()
    {
        var ctx = NewContext(out _);

        await ctx.Load<User>("admin@acme.com");

        var effect = Assert.Single(ctx.Effects);
        Assert.Equal(LifecycleVerb.Load, effect.Verb);
        Assert.Equal(LockMode.Shared, effect.Mode);
        Assert.Equal(new ResourceIdentity(typeof(User), "admin@acme.com"), effect.Identity);
        Assert.Null(effect.Data);
    }

    [Fact]
    public async Task Read_Edit_Delete_record_their_verbs_and_carry_the_instance()
    {
        var ctx = NewContext(out _);
        var user = new User("admin@acme.com");

        await ctx.Read(user);
        await ctx.Edit(user with { Suspended = true });
        await ctx.Delete(user);

        Assert.Equal(
            new[] { LifecycleVerb.Read, LifecycleVerb.Edit, LifecycleVerb.Delete },
            ctx.Effects.Select(e => e.Verb));
        Assert.All(ctx.Effects, e => Assert.NotNull(e.Data));
        // with-edited record keeps its key, so all three target the same identity.
        Assert.Single(ctx.Effects.Select(e => e.Identity).Distinct());
    }

    [Fact]
    public async Task Effects_are_recorded_in_call_order()
    {
        var ctx = NewContext(out _);

        await ctx.Create(new User("a@x"));
        await ctx.Load<User>("b@x");

        Assert.Equal(
            new[] { LifecycleVerb.Create, LifecycleVerb.Load },
            ctx.Effects.Select(e => e.Verb));
    }

    sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
