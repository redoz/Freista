using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Freista.Scheduling;

namespace Freista;

/// <summary>
/// Per-step execution context an optional trailing DSL parameter can accept. It carries the
/// scenario's cancellation token and service provider, and collects logs and attachments that the
/// runner associates with the running step. A fresh instance is created for every step so the
/// collected output stays correct even when sibling steps run concurrently.
/// </summary>
public sealed class ScenarioContext
{
    private static readonly AsyncLocal<ScenarioContext?> CurrentContext = new();

    private readonly ConcurrentQueue<string> _logs = new();
    private readonly ConcurrentDictionary<string, string> _attachments = new();

    /// <summary>
    /// The context of the step running on this execution flow, or null outside a step. Backed by
    /// <see cref="AsyncLocal{T}"/>, so concurrent sibling steps each observe their own context and
    /// never each other's — the same property that makes per-step log attribution correct without
    /// threading a context through user code.
    /// </summary>
    public static ScenarioContext? Current => CurrentContext.Value;

    /// <summary>Sets the ambient context for the calling flow. Called by the scheduler around a
    /// step's invocation; setting it inside the step's own async method keeps it out of the
    /// scheduler loop's flow and out of sibling steps'.</summary>
    internal static void SetCurrent(ScenarioContext? context) => CurrentContext.Value = context;

    public ScenarioContext(
        string stepId,
        string stepDisplayName,
        IServiceProvider? services,
        CancellationToken cancellationToken)
        : this(stepId, stepDisplayName, services, resolver: null, timeProvider: null, cancellationToken)
    {
    }

    public ScenarioContext(
        string stepId,
        string stepDisplayName,
        IServiceProvider? services,
        ResourceIdentityResolver? resolver,
        TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        StepId = stepId;
        StepDisplayName = stepDisplayName;
        CancellationToken = cancellationToken;
        Services = services;
        TimeProvider = timeProvider ?? TimeProvider.System;
        var effectiveResolver =
            resolver
            ?? services?.GetService(typeof(ResourceIdentityResolver)) as ResourceIdentityResolver
            ?? new ResourceIdentityResolver();
        Resources = new ResourceContext(
            stepId,
            stepDisplayName,
            effectiveResolver,
            TimeProvider);
    }

    /// <summary>Stable id of the step this context belongs to.</summary>
    public string StepId { get; }

    /// <summary>Formatted display name of the running step.</summary>
    public string StepDisplayName { get; }

    /// <summary>Cancellation for the scenario (scenario/step timeout or external cancel).</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Optional per-scenario service provider for DI; may be null.</summary>
    public IServiceProvider? Services { get; }

    /// <summary>
    /// The per-step <see cref="TimeProvider"/> backing this context — the same instance that stamps
    /// the step's <see cref="Model.ResourceEffect"/> timestamps. On a real run this is
    /// <see cref="TimeProvider.System"/>; in the scheduler's simulated-time mode it is a per-step
    /// <see cref="SimulatedClock"/> that only moves via <see cref="SimulateElapsed"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// The resource surface for this step: lifecycle verbs (<c>Create</c>/<c>Load</c>/<c>Read</c>/
    /// <c>Edit</c>/<c>Delete</c>) that record <see cref="Model.ResourceEffect"/>s for the report's
    /// resource lane. In C1 this is a pure tracer with no locking. The
    /// <see cref="ResourceIdentityResolver"/> is selected with the following precedence: an explicit
    /// resolver passed to the 6-arg constructor, then a <see cref="ResourceIdentityResolver"/>
    /// registered in <see cref="Services"/>, then a fresh default instance.
    /// </summary>
    public ResourceContext Resources { get; }

    /// <summary>
    /// Simulates that this step took <paramref name="delta"/> of wall-clock time without any real
    /// waiting. When the step runs under the scheduler's simulated-time mode (its
    /// <see cref="TimeProvider"/> is a <see cref="SimulatedClock"/>) this advances that per-step clock,
    /// which both lengthens the step's measured duration and shifts the timestamp of any effect
    /// recorded afterwards. On a real run (a non-simulated <see cref="TimeProvider"/>) it is an inert
    /// no-op, so the same DSL bodies stay correct in production.
    /// </summary>
    public void SimulateElapsed(TimeSpan delta)
    {
        if (TimeProvider is SimulatedClock clock)
        {
            clock.Advance(delta);
        }
    }

    /// <summary>Appends a log line associated with the current step.</summary>
    public void Log(string message) => _logs.Enqueue(message);

    /// <summary>
    /// A logger whose writes are collected as this step's log lines. Category is
    /// <typeparamref name="T"/>'s full name.
    /// </summary>
    public ILogger GetLogger<T>() => GetLogger(typeof(T).FullName ?? typeof(T).Name);

    /// <summary>A logger with an explicit category, writing to the running step.</summary>
    public ILogger GetLogger(string category) => new FreistaLogger(category, this);

    /// <summary>Records a named text attachment associated with the current step.</summary>
    public void AddAttachment(string name, string value) => _attachments[name] = value;

    /// <summary>Log lines collected for this step, in append order.</summary>
    public IReadOnlyList<string> Logs => _logs.ToArray();

    /// <summary>Attachments collected for this step, keyed by name.</summary>
    public IReadOnlyDictionary<string, string> Attachments => _attachments;
}
