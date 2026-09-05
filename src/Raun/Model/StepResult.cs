namespace Raun.Model;

/// <summary>The outcome of running (or skipping) a single scenario step.</summary>
public sealed class StepResult
{
    /// <summary>The node this result describes.</summary>
    public required ScenarioNode Node { get; init; }

    /// <summary>The formatted display name (runtime placeholders resolved).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Final status of the step.</summary>
    public required StepStatus Status { get; init; }

    /// <summary>Absolute wall-clock instant the step began (stamped scheduler-side via an injected
    /// <see cref="TimeProvider"/>). Skipped steps carry the instant at skip time; <see cref="Duration"/>
    /// stays zero. <c>FinishedAt</c> is derived (<c>StartedAt + Duration</c>), not stored.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Wall-clock duration the operation ran (zero for skipped steps).</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>The thrown exception when <see cref="Status"/> is <see cref="StepStatus.Failed"/>.</summary>
    public Exception? Exception { get; init; }

    /// <summary>Why the step was skipped, e.g. <c>"dependency failed: creating an appointment"</c>.</summary>
    public string? SkipReason { get; init; }

    /// <summary>Log lines collected during the step, messages only. Resource events the step recorded
    /// appear here too, in order, as <c>[resource] {Verb} {Identity}</c> lines.</summary>
    public IReadOnlyList<string> Logs { get; init; } = [];

    /// <summary>The same lines with their offset from the scenario's start. Populated by the scheduler;
    /// a result built elsewhere may leave it empty, in which case sinks fall back to <see cref="Logs"/>.</summary>
    public IReadOnlyList<LogEntry> LogEntries { get; init; } = [];

    /// <summary>Attachments collected during the step.</summary>
    public IReadOnlyDictionary<string, string> Attachments { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Resource effects (lifecycle acquires) the step recorded, in call order.</summary>
    public IReadOnlyList<ResourceEffect> Effects { get; init; } = [];

    /// <summary>Lineage relations the step recorded from a producer's [Created]/[Loaded]/[Edited] References/Consumes targets.</summary>
    public IReadOnlyList<ResourceLineageRelation> Lineage { get; init; } = [];
}
