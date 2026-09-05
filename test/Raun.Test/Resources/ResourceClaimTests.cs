using Xunit;

namespace Raun.Test.Resources;

/// <summary>Verifies claim dedup: one claim per identity, keeping the strongest lifecycle so
/// exclusive wins over shared.</summary>
public class ResourceClaimTests
{
    private static ResourceIdentity Id(string key) => new(typeof(User), key);

    [Fact]
    public void Reduce_dedups_by_identity_keeping_the_strongest_lifecycle_and_exclusive_mode()
    {
        var claims = new[]
        {
            new ResourceClaim(Id("u1"), LifecycleVerb.Read),   // shared
            new ResourceClaim(Id("u1"), LifecycleVerb.Edit),   // exclusive, stronger lifecycle
            new ResourceClaim(Id("u2"), LifecycleVerb.Load),
        };

        var reduced = ResourceClaim.Reduce(claims);

        var u1 = Assert.Single(reduced, c => c.Identity == Id("u1"));
        Assert.Equal(LifecycleVerb.Edit, u1.Verb);
        Assert.Equal(LockMode.Exclusive, u1.Mode);
        Assert.Contains(reduced, c => c.Identity == Id("u2") && c.Verb == LifecycleVerb.Load);
    }

    [Fact]
    public void Reduce_keeps_delete_over_edit_over_create_over_load_over_read()
    {
        var claims = new[]
        {
            new ResourceClaim(Id("u1"), LifecycleVerb.Create),
            new ResourceClaim(Id("u1"), LifecycleVerb.Delete),
            new ResourceClaim(Id("u1"), LifecycleVerb.Read),
        };

        var claim = Assert.Single(ResourceClaim.Reduce(claims));
        Assert.Equal(LifecycleVerb.Delete, claim.Verb);
    }
}
