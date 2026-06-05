using PUnit.Model;

namespace PUnit.Scheduling;

/// <summary>
/// Callbacks the scheduler raises around each step so a host (e.g. a runner adapter) can report
/// progress without the core taking a dependency on any runner. Every node raises exactly one
/// <see cref="OnStepStartingAsync"/> followed by one <see cref="OnStepFinishedAsync"/>, including
/// skipped steps. The scheduler <c>await</c>s each callback and raises them serially — one completes
/// before the next begins — so implementations need not guard against concurrent calls (though a
/// continuation may resume on a different thread across an await). Making these asynchronous lets a
/// host await a genuinely async sink (e.g. the MTP message bus) instead of blocking sync-over-async.
/// </summary>
public interface IStepObserver
{
    /// <summary>Raised when a step is about to run (or, for skipped steps, immediately before its finish).</summary>
    Task OnStepStartingAsync(ScenarioNode node, string displayName);

    /// <summary>Raised once a step has reached a terminal status.</summary>
    Task OnStepFinishedAsync(StepResult result);
}
