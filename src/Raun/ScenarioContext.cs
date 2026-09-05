using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Raun.Scheduling;

namespace Raun;

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

    private TeardownLog? _teardownLog;
    private int _teardownStepIndex;

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
    /// resource lane. Under the scheduler each verb also claims the identity in the scenario's
    /// <see cref="ResourceLedger"/>, so a claim that collides with an unordered sibling's throws
    /// <see cref="ResourceConflictException"/>; no lock is taken and nothing waits. The
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
    public ILogger GetLogger(string category) => new RaunLogger(category, this);

    /// <summary>Wires this step's resource verbs to the scenario's conflict ledger, so a claim that
    /// collides with an unordered sibling's throws <see cref="ResourceConflictException"/>. Called by
    /// the scheduler; a context built outside it has no ledger and its verbs only trace.</summary>
    internal void AttachLedger(ResourceLedger ledger, int stepIndex) => Resources.AttachLedger(ledger, stepIndex);

    /// <summary>Wires this context to the scenario's teardown log. Called by the scheduler; a context
    /// built outside it (a DSL method under unit test) simply has nowhere to register, and
    /// <see cref="OnTeardown(Func{Task})"/> becomes a no-op rather than throwing.</summary>
    internal void AttachTeardown(TeardownLog log, int stepIndex)
    {
        _teardownLog = log;
        _teardownStepIndex = stepIndex;
    }

    /// <summary>
    /// Registers cleanup for something this step created. The closure captures the object and the
    /// connection, because it is written where both are in scope. Runs after the scenario, subject to
    /// the scenario's <c>[Teardown(Run.…)]</c> policy. This form is for cleanups that report nothing;
    /// to log or attach from a cleanup, take the teardown context:
    /// <see cref="OnTeardown(Func{ScenarioContext, Task})"/>. A cleanup that reaches for this step's
    /// own context instead is a compile-time error (RAUN014) — that output would be lost.
    /// </summary>
    public void OnTeardown(Func<Task> cleanup) => OnTeardown(Cleanup.Optional, cleanup);

    /// <summary>
    /// Registers cleanup of the given kind. <see cref="Cleanup.Required"/> runs whatever the
    /// scenario's policy says — use it for things whose absence is a leak rather than a choice.
    /// </summary>
    public void OnTeardown(Cleanup kind, Func<Task> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        OnTeardown(kind, _ => cleanup());
    }

    /// <summary>
    /// Registers cleanup that receives the <b>teardown node's</b> context. A cleanup runs inside the
    /// scenario's Teardown step, long after this step has finished and its logs have been reported,
    /// so anything it logs or attaches must go through the context it is handed — not through the
    /// captured <c>ctx</c> of the step that registered it. That context is also
    /// <see cref="Current"/> while cleanups run, its <see cref="Services"/> is still the scenario's
    /// DI scope, and its <see cref="CancellationToken"/> is never cancelled: cleanups run after
    /// cancellation precisely so nothing leaks.
    /// </summary>
    public void OnTeardown(Func<ScenarioContext, Task> cleanup) => OnTeardown(Cleanup.Optional, cleanup);

    /// <summary>Registers cleanup of the given kind that receives the teardown node's context.</summary>
    public void OnTeardown(Cleanup kind, Func<ScenarioContext, Task> cleanup)
        => _teardownLog?.Add(_teardownStepIndex, kind, cleanup);

    /// <summary>Records a named text attachment associated with the current step.</summary>
    public void AddAttachment(string name, string value) => _attachments[name] = value;

    /// <summary>Log lines collected for this step, in append order.</summary>
    public IReadOnlyList<string> Logs => _logs.ToArray();

    /// <summary>Attachments collected for this step, keyed by name.</summary>
    public IReadOnlyDictionary<string, string> Attachments => _attachments;
}
