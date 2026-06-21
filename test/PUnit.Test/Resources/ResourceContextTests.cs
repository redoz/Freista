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
    public async Task Same_identity_dedups_to_the_strongest_verb()
    {
        var ctx = NewContext(out _);
        var user = new User("admin@acme.com");

        await ctx.Read(user);
        await ctx.Edit(user with { Suspended = true });
        await ctx.Delete(user);

        // All three touches have the same identity — dedup collapses to a single effect.
        var effect = Assert.Single(ctx.Effects);
        Assert.Equal(LifecycleVerb.Delete, effect.Verb);
        Assert.Equal(LockMode.Exclusive, effect.Mode);
        Assert.Equal(new ResourceIdentity(typeof(User), "admin@acme.com"), effect.Identity);
        Assert.NotNull(effect.Data);
    }

    [Fact]
    public async Task Dedup_keeps_latest_nonnull_data()
    {
        var ctx = NewContext(out _);
        var user = new User("admin@acme.com");
        var suspended = user with { Suspended = true };

        await ctx.Read(user);
        await ctx.Edit(suspended);

        var effect = Assert.Single(ctx.Effects);
        Assert.Equal(LifecycleVerb.Edit, effect.Verb);
        // The Edit data (suspended copy) replaces the Read data — proven by reference identity.
        Assert.Same(suspended, effect.Data);
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

    [Fact]
    public async Task Load_instance_overload_records_a_shared_effect_with_resolved_identity_and_data()
    {
        var ctx = NewContext(out _);
        var user = new User("jane@acme.com");

        await ctx.Load(user);

        var effect = Assert.Single(ctx.Effects);
        Assert.Equal(LifecycleVerb.Load, effect.Verb);
        Assert.Equal(LockMode.Shared, effect.Mode);
        Assert.Equal(new ResourceIdentity(typeof(User), "jane@acme.com"), effect.Identity);
        Assert.NotNull(effect.Data);
    }

    [Fact]
    public async Task Reference_records_a_shared_effect_with_resolved_identity()
    {
        var ctx = NewContext(out _);

        await ctx.Reference(new User("jane@acme.com"));

        var effect = Assert.Single(ctx.Effects);
        Assert.Equal(LifecycleVerb.Reference, effect.Verb);
        Assert.Equal(LockMode.Shared, effect.Mode);
        Assert.Equal(new ResourceIdentity(typeof(User), "jane@acme.com"), effect.Identity);
    }

    [Fact]
    public async Task Consume_records_a_shared_effect_with_resolved_identity()
    {
        var ctx = NewContext(out _);

        await ctx.Consume(new User("jane@acme.com"));

        var effect = Assert.Single(ctx.Effects);
        Assert.Equal(LifecycleVerb.Consume, effect.Verb);
        Assert.Equal(LockMode.Shared, effect.Mode);
        Assert.Equal(new ResourceIdentity(typeof(User), "jane@acme.com"), effect.Identity);
    }

    [Fact]
    public async Task Consume_outranks_read_in_dedup()
    {
        var ctx = NewContext(out _);
        var user = new User("jane@acme.com");

        await ctx.Read(user);
        await ctx.Consume(user);

        // Same identity ⇒ one effect; the usage verb (Consume) outranks plain Read.
        var effect = Assert.Single(ctx.Effects);
        Assert.Equal(LifecycleVerb.Consume, effect.Verb);
    }

    sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
