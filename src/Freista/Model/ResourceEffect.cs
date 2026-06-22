namespace Freista.Model;

/// <summary>
/// One symbolic effect a step declared on a resource: the trace event powering the report's resource
/// lane and debugging ("which step deleted User:123?"). C1 records lifecycle-verb acquires only;
/// locking transitions (Wounded/Retried/Contended) arrive in C2.
/// </summary>
public sealed record ResourceEffect
{
    /// <summary>The lifecycle verb the step declared (Create/Load/Read/Edit/Delete).</summary>
    public required LifecycleVerb Verb { get; init; }

    /// <summary>The resource identity the effect targets.</summary>
    public required ResourceIdentity Identity { get; init; }

    /// <summary>Optional data snapshot shown in the trace (e.g. the resource instance).</summary>
    public object? Data { get; init; }

    /// <summary>Stable id of the step that produced the effect.</summary>
    public string? StepId { get; init; }

    /// <summary>Display name of the step that produced the effect.</summary>
    public string? StepDisplayName { get; init; }

    /// <summary>When the effect was recorded.</summary>
    public System.DateTimeOffset Timestamp { get; init; }

    /// <summary>The reader/writer mode this verb implies.</summary>
    public LockMode Mode => Verb.ToLockMode();
}
