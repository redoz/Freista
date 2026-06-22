namespace PUnit;

/// <summary>What a step does to a resource: both a trace label and the lock-mode source.</summary>
public enum LifecycleVerb
{
    /// <summary>Brings a new resource into existence (exclusive).</summary>
    Create,

    /// <summary>Fetches an existing resource (Get/Load) (shared).</summary>
    Load,

    /// <summary>Reads an in-scope resource without mutating it (shared).</summary>
    Read,

    /// <summary>Mutates an existing resource (exclusive).</summary>
    Edit,

    /// <summary>Removes a resource (exclusive).</summary>
    Delete,

    /// <summary>References an independently-living resource — a durable pointer the produced
    /// resource keeps (aggregation; shared). May carry a lineage relation in the report (opt-in via
    /// the target's declared subjects).</summary>
    Reference,

    /// <summary>Consumes/uses-up a resource into the one the step produces (composition; shared in
    /// C1, exclusive in C2). May carry a lineage relation in the report (opt-in via the target's
    /// declared subjects).</summary>
    Consume,
}

/// <summary>Maps a <see cref="LifecycleVerb"/> onto its lock mode and dedup precedence.</summary>
public static class LifecycleVerbExtensions
{
    /// <summary>Read/Load/Reference/Consume yield <see cref="LockMode.Shared"/>; Create/Edit/Delete yield <see cref="LockMode.Exclusive"/>.</summary>
    public static LockMode ToLockMode(this LifecycleVerb verb) => verb switch
    {
        LifecycleVerb.Read or LifecycleVerb.Load
            or LifecycleVerb.Reference or LifecycleVerb.Consume => LockMode.Shared,
        _ => LockMode.Exclusive,
    };

    /// <summary>
    /// Lifecycle precedence for dedup (higher wins):
    /// Delete &gt; Edit &gt; Create &gt; Load &gt; Consume &gt; Reference &gt; Read.
    /// All exclusive verbs (Create/Edit/Delete) still outrank all shared ones, so exclusive wins
    /// over shared on the same identity automatically.
    /// </summary>
    public static int Precedence(this LifecycleVerb verb) => verb switch
    {
        LifecycleVerb.Delete => 7,
        LifecycleVerb.Edit => 6,
        LifecycleVerb.Create => 5,
        LifecycleVerb.Load => 4,
        LifecycleVerb.Consume => 3,
        LifecycleVerb.Reference => 2,
        _ => 1,
    };
}
