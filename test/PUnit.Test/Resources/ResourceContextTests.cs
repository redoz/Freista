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

    [Fact]
    public async Task Reference_with_a_subject_records_a_lineage_relation_and_the_effect()
    {
        var ctx = NewContext(out _);
        var subject = new User("appt@acme.com");   // stands in for the produced resource
        var target = new User("jane@acme.com");

        await ctx.Reference(target, subject);

        var effect = Assert.Single(ctx.Effects);            // the Reference effect is still recorded
        Assert.Equal(LifecycleVerb.Reference, effect.Verb);
        Assert.Equal(new ResourceIdentity(typeof(User), "jane@acme.com"), effect.Identity);

        var relation = Assert.Single(ctx.Lineage);          // and a subject -> target relation
        Assert.Equal(new ResourceIdentity(typeof(User), "appt@acme.com"), relation.Subject);
        Assert.Equal(new ResourceIdentity(typeof(User), "jane@acme.com"), relation.Target);
        Assert.Equal(LifecycleVerb.Reference, relation.Kind);
    }

    [Fact]
    public async Task Consume_with_two_subjects_records_two_relations()
    {
        var ctx = NewContext(out _);
        var a = new User("a@acme.com");
        var b = new User("b@acme.com");
        var target = new User("slot@acme.com");

        await ctx.Consume(target, a, b);

        Assert.Equal(2, ctx.Lineage.Count);
        Assert.All(ctx.Lineage, e => Assert.Equal(LifecycleVerb.Consume, e.Kind));
        Assert.All(ctx.Lineage, e => Assert.Equal(new ResourceIdentity(typeof(User), "slot@acme.com"), e.Target));
        Assert.Contains(ctx.Lineage, e => e.Subject == new ResourceIdentity(typeof(User), "a@acme.com"));
        Assert.Contains(ctx.Lineage, e => e.Subject == new ResourceIdentity(typeof(User), "b@acme.com"));
    }

    [Fact]
    public async Task Reference_without_subjects_records_no_relation()
    {
        var ctx = NewContext(out _);

        await ctx.Reference(new User("jane@acme.com"));

        Assert.Single(ctx.Effects);
        Assert.Empty(ctx.Lineage);
    }

    sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
