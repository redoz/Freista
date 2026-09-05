namespace Raun.Reporting;

/// <summary>
/// Base sink with virtual no-op handlers and sealed pattern-match dispatch, so a concrete sink
/// overrides only the events it cares about.
/// </summary>
public abstract class RunEventSink : IRunEventSink
{
    public ValueTask PublishAsync(RunEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return evt switch
        {
            RunStarted e => OnRunStartedAsync(e),
            ScenarioStarted e => OnScenarioStartedAsync(e),
            StepStarted e => OnStepStartedAsync(e),
            StepFinished e => OnStepFinishedAsync(e),
            ScenarioFinished e => OnScenarioFinishedAsync(e),
            RunFinished e => OnRunFinishedAsync(e),
            _ => default,
        };
    }

    protected virtual ValueTask OnRunStartedAsync(RunStarted e) => default;
    protected virtual ValueTask OnScenarioStartedAsync(ScenarioStarted e) => default;
    protected virtual ValueTask OnStepStartedAsync(StepStarted e) => default;
    protected virtual ValueTask OnStepFinishedAsync(StepFinished e) => default;
    protected virtual ValueTask OnScenarioFinishedAsync(ScenarioFinished e) => default;
    protected virtual ValueTask OnRunFinishedAsync(RunFinished e) => default;
}
