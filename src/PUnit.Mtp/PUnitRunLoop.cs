using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Messages;
using Microsoft.Testing.Platform.TestHost;
using PUnit.Model;
using PUnit.Scheduling;

namespace PUnit.Mtp;

/// <summary>
/// The run loop (design §5): turns an MTP run request's filter into a set of <em>distinct</em>
/// scenarios and runs each one exactly once through a <see cref="ScenarioScheduler"/>, reporting
/// every executed step via a <see cref="PUnitStepReporter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Selecting several step uids of one scenario yields a single scheduler run (the DAG executes the
/// scenario's steps once and memoizes their outputs), so a multi-step filter ⇒ one run. Because the
/// scheduler runs a scenario's transitive dependency closure, naming a single step still runs (and
/// the reporter still publishes) all of its executed siblings — MTP's publish path has no
/// filter/lifecycle gate, so they all "light up".
/// </para>
/// <para>
/// Each scenario run gets its own <see cref="CancellationTokenSource"/>, linked to the platform's
/// token and <strong>owned by the loop</strong> — never tied to a single step node's lifecycle — so
/// one step can never cancel the shared run out from under its siblings. Cross-scenario execution is
/// sequential for v1 (the scheduler already provides bounded parallelism <em>within</em> a
/// scenario); see <see cref="RunSelectedAsync"/> for the seam where a future bounded degree would go.
/// </para>
/// </remarks>
internal sealed class PUnitRunLoop
{
    /// <summary>
    /// Runs one scenario to completion. The default implementation drives a real
    /// <see cref="ScenarioScheduler"/>; tests substitute it to observe how many runs the loop issues.
    /// </summary>
    public delegate Task RunScenario(
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

    /// <summary>
    /// Runs every scenario the <paramref name="uids"/> filter selects (or all scenarios when it is
    /// <see langword="null"/>), publishing a node update for each executed step.
    /// </summary>
    public async ValueTask RunAsync(
        SessionUid sessionUid,
        ISet<string>? uids,
        IMessageBus messageBus,
        IDataProducer producer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messageBus);
        ArgumentNullException.ThrowIfNull(producer);

        var selected = SelectScenarios(scenarioSource(), uids);
        await RunSelectedAsync(selected, sessionUid, messageBus, producer, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RunSelectedAsync(
        IReadOnlyList<ScenarioDefinition> selected,
        SessionUid sessionUid,
        IMessageBus messageBus,
        IDataProducer producer,
        CancellationToken cancellationToken)
    {
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

            await RunOneAsync(definition, sessionUid, messageBus, producer, cancellationToken).ConfigureAwait(false);
            started = true;
        }
    }

    private async ValueTask RunOneAsync(
        ScenarioDefinition definition,
        SessionUid sessionUid,
        IMessageBus messageBus,
        IDataProducer producer,
        CancellationToken cancellationToken)
    {
        // One CTS per scenario run, owned here and linked to the platform token. Tying cancellation
        // to the run (not to any single step node) is what keeps a sibling from canceling the run.
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var reporter = new PUnitStepReporter(definition, sessionUid, messageBus, producer);
        await runScenario(definition, reporter, runCts.Token).ConfigureAwait(false);
    }

    private static async Task DefaultRunScenario(
        ScenarioDefinition definition,
        IStepObserver observer,
        CancellationToken cancellationToken)
        => await new ScenarioScheduler().RunAsync(
            definition,
            services: null,
            observer: observer,
            cancellationToken: cancellationToken).ConfigureAwait(false);
}
