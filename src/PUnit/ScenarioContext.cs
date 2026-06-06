using System.Collections.Concurrent;

namespace PUnit;

/// <summary>
/// Per-step execution context an optional trailing DSL parameter can accept. It carries the
/// scenario's cancellation token and service provider, and collects logs and attachments that the
/// runner associates with the running step. A fresh instance is created for every step so the
/// collected output stays correct even when sibling steps run concurrently.
/// </summary>
public sealed class ScenarioContext
{
    private readonly ConcurrentQueue<string> _logs = new();
    private readonly ConcurrentDictionary<string, string> _attachments = new();

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
        Resources = new ResourceContext(
            stepId,
            stepDisplayName,
            resolver ?? new ResourceIdentityResolver(),
            timeProvider ?? TimeProvider.System);
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
    /// The resource surface for this step: lifecycle verbs (<c>Create</c>/<c>Load</c>/<c>Read</c>/
    /// <c>Edit</c>/<c>Delete</c>) that record <see cref="Model.ResourceEffect"/>s for the report's
    /// resource lane. In C1 this is a pure tracer with no locking.
    /// </summary>
    public ResourceContext Resources { get; }

    /// <summary>Appends a log line associated with the current step.</summary>
    public void Log(string message) => _logs.Enqueue(message);

    /// <summary>Records a named text attachment associated with the current step.</summary>
    public void AddAttachment(string name, string value) => _attachments[name] = value;

    /// <summary>Log lines collected for this step, in append order.</summary>
    public IReadOnlyList<string> Logs => _logs.ToArray();

    /// <summary>Attachments collected for this step, keyed by name.</summary>
    public IReadOnlyDictionary<string, string> Attachments => _attachments;
}
