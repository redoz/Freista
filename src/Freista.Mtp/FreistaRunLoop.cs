using Freista.Model;
using Microsoft.Extensions.DependencyInjection;
using Freista.Reporting;
using Freista.Scheduling;

namespace Freista.Mtp;

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
internal sealed class FreistaRunLoop
{
    /// <summary>Runs one scenario to completion and returns its step results. Tests substitute this
    /// to observe how many runs the loop issues; the default drives a real <see cref="ScenarioScheduler"/>.</summary>
    public delegate Task<IReadOnlyList<StepResult>> RunScenario(
        ScenarioDefinition definition,
        IStepObserver observer,
        IServiceProvider? services,
        CancellationToken cancellationToken);

    private readonly Func<IEnumerable<ScenarioDefinition>> scenarioSource;
    private readonly RunScenario runScenario;
    private readonly bool simulateTime;
    private readonly IServiceProvider? services;
    private readonly Func<ScenarioContext, Task>? preflight;

    /// <param name="scenarioSource">Supplies the registered scenarios to consider for the run.</param>
    /// <param name="runScenario">
    /// How to run one scenario; defaults to a fresh <see cref="ScenarioScheduler"/> per run.
    /// </param>
    /// <param name="simulateTime">
    /// When true, the default scenario runner builds a <see cref="ScenarioScheduler"/> in simulated-time
    /// mode (deterministic DAG-correct timeline driven from step bodies via
    /// <see cref="ScenarioContext.SimulateElapsed"/>). Defaults to <see langword="false"/> so production
    /// runs use real timing. Ignored when an explicit <paramref name="runScenario"/> seam is supplied.
    /// </param>
    /// <param name="services">
    /// The service provider handed to every <see cref="ScenarioContext"/> the default runner creates,
    /// surfacing as <c>ctx.Services</c> to step bodies. <see langword="null"/> (the default) is a real,
    /// supported path — <see cref="FreistaTestFramework"/>'s parameterless ctor has no provider — and
    /// leaves <c>ctx.Services</c> null. Ignored when an explicit <paramref name="runScenario"/> seam is supplied.
    /// </param>
    /// <param name="preflight">
    /// Run-level setup executed once before any scenario and reported as its own node. When it fails,
    /// every scenario's steps report skipped naming preflight, and the run still completes so the
    /// report stays whole. <see langword="null"/> (the default) means no preflight node exists at all.
    /// </param>
    public FreistaRunLoop(
        Func<IEnumerable<ScenarioDefinition>> scenarioSource,
        RunScenario? runScenario = null,
        bool simulateTime = false,
        IServiceProvider? services = null,
        Func<ScenarioContext, Task>? preflight = null)
    {
        ArgumentNullException.ThrowIfNull(scenarioSource);
        this.scenarioSource = scenarioSource;
        this.simulateTime = simulateTime;
        this.services = services;
        this.preflight = preflight;
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
                if (uids.Contains(FreistaDiscoverer.MakeUid(definition.ScenarioId, step.StepId)))
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

        // Run-level setup, before any scenario. It runs even when the filter selected nothing: a
        // filtered run of one step still needs whatever preflight brings up.
        var preflightFailed = false;
        if (preflight is not null)
        {
            var results = await RunOneAsync(Preflight.Definition(preflight), bus, cancellationToken)
                .ConfigureAwait(false);
            preflightFailed = results.Any(r => r.Status is StepStatus.Failed or StepStatus.Skipped);
        }

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

            if (preflightFailed)
            {
                // Attribute the failure to a row rather than to an exit code: every step reports
                // skipped naming preflight, and the run still completes so the report is whole.
                await SkipScenarioAsync(definition, bus).ConfigureAwait(false);
                continue;
            }

            await RunOneAsync(definition, bus, cancellationToken).ConfigureAwait(false);
            started = true;
        }

        await bus.PublishAsync(new RunFinished()).ConfigureAwait(false);
    }

    /// <summary>Reports every step of a scenario that never ran because preflight failed.</summary>
    private static async ValueTask SkipScenarioAsync(ScenarioDefinition definition, IRunEventSink bus)
    {
        await bus.PublishAsync(new ScenarioStarted(definition)).ConfigureAwait(false);

        var results = new List<StepResult>(definition.Nodes.Count);
        foreach (var node in definition.Nodes)
        {
            var result = new StepResult
            {
                Node = node,
                DisplayName = node.DisplayNameTemplate,
                Status = StepStatus.Skipped,
                StartedAt = default,
                SkipReason = Preflight.FailedSkipReason,
            };
            results.Add(result);
            await bus.PublishAsync(new StepFinished(definition, result)).ConfigureAwait(false);
        }

        await bus.PublishAsync(new ScenarioFinished(definition, results)).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<StepResult>> RunOneAsync(
        ScenarioDefinition definition, IRunEventSink bus, CancellationToken cancellationToken)
    {
        // One CTS per scenario run, owned here and linked to the platform token. Tying cancellation
        // to the run (not to any single step node) is what keeps a sibling from canceling the run.
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await bus.PublishAsync(new ScenarioStarted(definition)).ConfigureAwait(false);

        var observer = new BusObserver(definition, bus);

        // One DI scope per scenario, so AddScoped means "per scenario" and AddSingleton means "per
        // run" through ordinary .NET semantics. Disposed only after the scenario returns — the
        // scheduler runs teardown as the last thing inside it, and a cleanup may hold something
        // resolved from this scope.
        var scope = services?.GetService(typeof(IServiceScopeFactory)) is IServiceScopeFactory factory
            ? factory.CreateScope()
            : null;

        try
        {
            var scenarioServices = scope?.ServiceProvider ?? services;
            var results = await runScenario(definition, observer, scenarioServices, runCts.Token).ConfigureAwait(false);

            await bus.PublishAsync(new ScenarioFinished(definition, results)).ConfigureAwait(false);
            return results;
        }
        finally
        {
            scope?.Dispose();
        }
    }

    // Instance (not static) because it reads the simulateTime field to pick the scheduler's timing
    // mode. The provider comes in per scenario: it is the scenario's DI scope, not the root.
    private async Task<IReadOnlyList<StepResult>> DefaultRunScenario(
        ScenarioDefinition definition,
        IStepObserver observer,
        IServiceProvider? scenarioServices,
        CancellationToken cancellationToken)
        => await new ScenarioScheduler(simulatedTime: simulateTime).RunAsync(
            definition,
            services: scenarioServices,
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
