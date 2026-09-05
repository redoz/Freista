using System.Collections.Generic;
using System.Linq;

namespace Freista;

/// <summary>
/// A deduped claim on one identity: the strongest verb a step (or a set of effects) declares on it.
/// </summary>
public readonly record struct ResourceClaim(ResourceIdentity Identity, LifecycleVerb Verb)
{
    /// <summary>The reader/writer mode this claim's verb implies.</summary>
    public LockMode Mode => Verb.ToLockMode();

    /// <summary>
    /// Collapses claims to one per identity, keeping the strongest lifecycle (Delete &gt; Edit &gt;
    /// Create &gt; Load &gt; Read); exclusive therefore wins over shared.
    /// </summary>
    public static IReadOnlyList<ResourceClaim> Reduce(IEnumerable<ResourceClaim> claims) =>
        claims
            .GroupBy(c => c.Identity)
            .Select(g => new ResourceClaim(g.Key, g.MaxBy(c => c.Verb.Precedence()).Verb))
            .ToList();
}
