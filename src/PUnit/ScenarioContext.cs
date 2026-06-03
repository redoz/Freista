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
    readonly ConcurrentQueue<string> _logs = new();
    readonly ConcurrentDictionary<string, string> _attachments = new();

    public ScenarioContext(
        string stepId,
        string stepDisplayName,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        StepId = stepId;
        StepDisplayName = stepDisplayName;
        CancellationToken = cancellationToken;
        Services = services;
    }

    /// <summary>Stable id of the step this context belongs to.</summary>
    public string StepId { get; }

    /// <summary>Formatted display name of the running step.</summary>
    public string StepDisplayName { get; }

    /// <summary>Cancellation for the scenario (scenario/step timeout or external cancel).</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Optional per-scenario service provider for DI; may be null.</summary>
    public IServiceProvider? Services { get; }

    /// <summary>Appends a log line associated with the current step.</summary>
    public void Log(string message) => _logs.Enqueue(message);

    /// <summary>Records a named text attachment associated with the current step.</summary>
    public void AddAttachment(string name, string value) => _attachments[name] = value;

    /// <summary>Log lines collected for this step, in append order.</summary>
    public IReadOnlyList<string> Logs => _logs.ToArray();

    /// <summary>Attachments collected for this step, keyed by name.</summary>
    public IReadOnlyDictionary<string, string> Attachments => _attachments;
}
