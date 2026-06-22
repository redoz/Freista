using Freista.Model;
using Freista.Scheduling;

namespace Freista.Reporting;

/// <summary>Base type for the runner-neutral run-event stream (design §3.A).</summary>
public abstract record RunEvent;

/// <summary>Raised once at the start of a run, before any scenario.</summary>
public sealed record RunStarted(int ScenarioCount) : RunEvent;

/// <summary>Raised when a scenario begins; carries the definition so a session-scoped sink can
/// attribute every following step to its scenario.</summary>
public sealed record ScenarioStarted(ScenarioDefinition Definition) : RunEvent;

/// <summary>Raised when a step is about to run (or, for a skipped step, just before its finish).</summary>
public sealed record StepStarted(ScenarioDefinition Definition, StepContext Context) : RunEvent;

/// <summary>Raised when a step reaches a terminal status; the result is self-contained (carries
/// StartedAt, duration, logs, effects, exception/skip reason).</summary>
public sealed record StepFinished(ScenarioDefinition Definition, StepResult Result) : RunEvent;

/// <summary>Raised when a scenario's steps have all reached terminal status.</summary>
public sealed record ScenarioFinished(
    ScenarioDefinition Definition, IReadOnlyList<StepResult> Results) : RunEvent;

/// <summary>Raised once at the end of a run, after the last scenario.</summary>
public sealed record RunFinished : RunEvent;
