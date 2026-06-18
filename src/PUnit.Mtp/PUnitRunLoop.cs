using PUnit.Model;
using PUnit.Reporting;
using PUnit.Scheduling;

namespace PUnit.Mtp;

/// <summary>
/// The run loop (design §5): turns a run request's filter into a set of <em>distinct</em> scenarios
/// and runs each one exactly once through a <see cref="ScenarioScheduler"/>, emitting the run-event
/// envelope (<see cref="RunStarted"/> → per-scenario <see cref="ScenarioStarted"/>/steps/
/// <see cref="ScenarioFinished"/> → <see cref="RunFinished"/>) onto an <see cref="IRunEventSink"/>.
/// </summary>
/// <remarks>
/// <para>
/// Selecting several step uids of one scenario yields a single scheduler run (the DAG executes the
/// scenario's steps once and memoizes their outputs), so a multi-step filter ⇒ one run. Because the
/// scheduler runs a scenario's transitive dependency closure, naming a single step still runs (and
/// the sink still receives) all of its executed siblings.
/// </para>
/// <para>
/// Each scenario run gets its own <see cref="CancellationTokenSource"/>, linked to the platform's
/// token and <strong>owned by the loop</strong> — never tied to a single step node's lifecycle — so
/// one step can never cancel the shared run out from under its siblings. Cross-scenario execution is
/// sequential for v1 (the scheduler already provides bounded parallelism <em>within</em> a scenario).
/// </para>
/// </remarks>
internal sealed class PUnitRunLoop
{
    /// <summary>Runs one scenario to completion and returns its step results. Tests substitute this
    /// to observe how many runs the loop issues; the default drives a real <see cref="ScenarioScheduler"/>.</summary>
    public delegate Task<IReadOnlyList<StepResult>> RunScenario(
        ScenarioDefinition definition,
        IStepObserver observer,
        CancellationToken cancellationToken);

    private readonly Func<IEnumerable<ScenarioDefinition>> scenarioSource;
    private readonly RunScenario runScenario;

    /// <param name="scenarioSource">Supplies the registered scenarios to consider for the run.</param>
    /// <param name="runScenario">
    /// How to run one scenario; defaults to a fresh <see cref="ScenarioScheduler"/> per run.
    /// </param>
    public PUnitRunLoop(
        Func<IEnumerable<ScenarioDefinition>> scenarioSource,
        RunScenario? runScenario = null)
    {
        ArgumentNullException.ThrowIfNull(scenarioSource);
        this.scenarioSource = scenarioSource;
        this.runScenario = runScenario ?? DefaultRunScenario;
    }

    /// <summary>
    /// Maps a run filter onto the distinct scenarios it selects. A <see langword="null"/> uid set
    /// (the request had no filter, or a no-op one) selects every scenario; otherwise a scenario is
    /// selected when any of its step uids (<c>{ScenarioId}:{StepId}</c>) appears in the set.
    /// </summary>
    public static IReadOnlyList<ScenarioDefinition> SelectScenarios(
        IEnumerable<ScenarioDefinition> scenarios,
        ISet<string>? uids)
    {
        ArgumentNullException.ThrowIfNull(scenarios);

        if (uids is null)
        {
            return scenarios.ToList();
        }

        var selected = new List<ScenarioDefinition>();
        foreach (var definition in scenarios)
        {
            foreach (var step in definition.Nodes)
            {
                if (uids.Contains(PUnitDiscoverer.MakeUid(definition.ScenarioId, step.StepId)))
                {
                    selected.Add(definition);
                    break; // distinct scenario, regardless of how many of its steps matched
                }
            }
        }

        return selected;
    }

    /// <summary>Runs every scenario the <paramref name="uids"/> filter selects (or all when null),
    /// emitting the run-event envelope (<see cref="RunStarted"/> → per scenario
    /// <see cref="ScenarioStarted"/>/steps/<see cref="ScenarioFinished"/> → <see cref="RunFinished"/>).</summary>
    public async ValueTask RunAsync(ISet<string>? uids, IRunEventSink bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bus);

        var selected = SelectScenarios(scenarioSource(), uids);
        await bus.PublishAsync(new RunStarted(selected.Count)).ConfigureAwait(false);

        // v1: sequential cross-scenario execution. The scheduler already parallelizes steps WITHIN a
        // scenario; bounding concurrency ACROSS scenarios is a future enhancement — this foreach is
        // the seam where a SemaphoreSlim / Parallel.ForEachAsync with a bounded degree would slot in.
        var started = false;
        foreach (var definition in selected)
        {
            // Honor platform cancellation between scenarios: once cancellation is observed after a
            // scenario has run, stop launching scenarios that have not started, rather than reporting
            // every remaining one as all-skipped (which would flood the runner with skip updates for
            // work the user never started). The first selected scenario always runs so that a run
            // canceled up-front still reports its (skipped) steps via the scheduler's skip path.
            if (started && cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await RunOneAsync(definition, bus, cancellationToken).ConfigureAwait(false);
            started = true;
        }

        await bus.PublishAsync(new RunFinished()).ConfigureAwait(false);
    }

    private async ValueTask RunOneAsync(
        ScenarioDefinition definition, IRunEventSink bus, CancellationToken cancellationToken)
    {
        // One CTS per scenario run, owned here and linked to the platform token. Tying cancellation
        // to the run (not to any single step node) is what keeps a sibling from canceling the run.
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await bus.PublishAsync(new ScenarioStarted(definition)).ConfigureAwait(false);

        var observer = new BusObserver(definition, bus);
        var results = await runScenario(definition, observer, runCts.Token).ConfigureAwait(false);

        await bus.PublishAsync(new ScenarioFinished(definition, results)).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<StepResult>> DefaultRunScenario(
        ScenarioDefinition definition, IStepObserver observer, CancellationToken cancellationToken)
        => await new ScenarioScheduler().RunAsync(
            definition,
            services: null,
            observer: observer,
            cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <summary>Republishes the scheduler's per-step callbacks onto the bus, tagged with the scenario.</summary>
    private sealed class BusObserver(ScenarioDefinition definition, IRunEventSink bus) : IStepObserver
    {
        public Task OnStepStartingAsync(StepContext context)
            => bus.PublishAsync(new StepStarted(definition, context)).AsTask();

        public Task OnStepFinishedAsync(StepResult result)
            => bus.PublishAsync(new StepFinished(definition, result)).AsTask();
    }
}
