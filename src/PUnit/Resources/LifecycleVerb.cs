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
}

/// <summary>Maps a <see cref="LifecycleVerb"/> onto its lock mode and dedup precedence.</summary>
public static class LifecycleVerbExtensions
{
    /// <summary>Read/Load yield <see cref="LockMode.Shared"/>; Create/Edit/Delete yield <see cref="LockMode.Exclusive"/>.</summary>
    public static LockMode ToLockMode(this LifecycleVerb verb) => verb switch
    {
        LifecycleVerb.Read or LifecycleVerb.Load => LockMode.Shared,
        _ => LockMode.Exclusive,
    };

    /// <summary>
    /// Lifecycle precedence for dedup (higher wins): Delete &gt; Edit &gt; Create &gt; Load &gt; Read.
    /// Because exclusive verbs all outrank the shared ones, exclusive wins over shared on the same
    /// identity automatically.
    /// </summary>
    public static int Precedence(this LifecycleVerb verb) => verb switch
    {
        LifecycleVerb.Delete => 5,
        LifecycleVerb.Edit => 4,
        LifecycleVerb.Create => 3,
        LifecycleVerb.Load => 2,
        _ => 1,
    };
}
