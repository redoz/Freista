using PUnit.Model;

namespace PUnit.Scheduling;

/// <summary>
/// Callbacks the scheduler raises around each step so a host (e.g. the xUnit adapter) can report
/// progress without the core taking a dependency on any runner. Every node raises exactly one
/// <see cref="OnStepStarting"/> followed by one <see cref="OnStepFinished"/>, including skipped
/// steps. Callbacks may be invoked from arbitrary threads; implementations must be thread-safe.
/// </summary>
public interface IStepObserver
{
    /// <summary>Raised when a step is about to run (or, for skipped steps, immediately before its finish).</summary>
    void OnStepStarting(ScenarioNode node, string displayName);

    /// <summary>Raised once a step has reached a terminal status.</summary>
    void OnStepFinished(StepResult result);
}
