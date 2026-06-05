using System.Diagnostics;
using PUnit.Model;

namespace PUnit.Scheduling;

/// <summary>
/// Executes a <see cref="ScenarioDefinition"/> as a DAG: a node runs only once all its
/// dependencies have passed; independent ready nodes run concurrently (bounded by an optional
/// max-parallelism). When a node fails, its transitive dependents are skipped with a summarizing
/// reason while independent branches continue. Per-step timeouts and scenario cancellation are
/// honored. The scheduler is runner-neutral; reporting flows through an optional
/// <see cref="IStepObserver"/>.
/// </summary>
public sealed class ScenarioScheduler
{
    readonly int _maxParallelism;

    /// <param name="maxParallelism">Maximum steps running at once; 0 (default) means unbounded.</param>
    public ScenarioScheduler(int maxParallelism = 0)
    {
        _maxParallelism = maxParallelism;
    }


    public async Task<IReadOnlyList<StepResult>> RunAsync(
        ScenarioDefinition definition,
        IServiceProvider? services = null,
        IStepObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        definition.Validate();

        var nodes = definition.Nodes;
        var count = nodes.Count;
        var status = new StepStatus[count];          // defaults to Pending
        var results = new StepResult?[count];
        var outputs = new object?[count];
        var inputs = new StepInputs(outputs);
        var capacity = _maxParallelism > 0 ? _maxParallelism : int.MaxValue;

        var pending = new HashSet<int>(Enumerable.Range(0, count));
        var running = new Dictionary<Task<NodeOutcome>, int>();

        while (pending.Count > 0 || running.Count > 0)
        {
            var progressed = false;

            // 1. Resolve skips: cancellation, or all dependencies terminal with at least one bad.
            foreach (var i in pending.ToArray())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    await ApplySkipAsync(i, "scenario canceled").ConfigureAwait(false);
                    progressed = true;
                    continue;
                }

                var node = nodes[i];
                var anyUnresolved = false;
                List<string>? failed = null;
                List<string>? skipped = null;

                foreach (var dep in node.DependsOn)
                {
                    switch (status[dep])
                    {
                        case StepStatus.Pending:
                        case StepStatus.Running:
                            anyUnresolved = true;
                            break;
                        case StepStatus.Failed:
                            (failed ??= []).Add(nodes[dep].OperationName);
                            break;
                        case StepStatus.Skipped:
                            (skipped ??= []).Add(nodes[dep].OperationName);
                            break;
                    }
                }

                if (!anyUnresolved && (failed is not null || skipped is not null))
                {
                    await ApplySkipAsync(i, BuildSkipReason(failed, skipped)).ConfigureAwait(false);
                    progressed = true;
                }
            }

            // 2. Launch ready nodes (all dependencies passed), bounded by capacity.
            if (!cancellationToken.IsCancellationRequested)
            {
                foreach (var i in pending.ToArray())
                {
                    if (running.Count >= capacity)
                    {
                        break;
                    }

                    var node = nodes[i];
                    if (node.DependsOn.All(d => status[d] == StepStatus.Passed))
                    {
                        pending.Remove(i);
                        status[i] = StepStatus.Running;
                        var displayName = FormatName(node, inputs);
                        if (observer is not null)
                        {
                            await observer.OnStepStartingAsync(node, displayName).ConfigureAwait(false);
                        }

                        running[RunNodeAsync(node, inputs, services, displayName, cancellationToken)] = i;
                        progressed = true;
                    }
                }
            }

            // 3. Wait for a running step, or guard against a stuck schedule.
            if (running.Count == 0)
            {
                if (pending.Count == 0)
                {
                    break;
                }

                if (!progressed)
                {
                    throw new InvalidOperationException(
                        "Scenario scheduler stalled with unresolved steps; this indicates a graph defect.");
                }

                continue;
            }

            var finishedTask = await Task.WhenAny(running.Keys).ConfigureAwait(false);
            var index = running[finishedTask];
            running.Remove(finishedTask);
            var outcome = await finishedTask.ConfigureAwait(false); // RunNodeAsync never throws

            status[index] = outcome.Result.Status;
            if (outcome.Result.Status == StepStatus.Passed)
            {
                outputs[index] = outcome.Output;
            }

            results[index] = outcome.Result;
            if (observer is not null)
            {
                await observer.OnStepFinishedAsync(outcome.Result).ConfigureAwait(false);
            }
        }

        return results.Select(r => r!).ToList();

        async Task ApplySkipAsync(int i, string reason)
        {
            pending.Remove(i);
            status[i] = StepStatus.Skipped;
            var node = nodes[i];
            var name = FormatName(node, inputs);
            if (observer is not null)
            {
                await observer.OnStepStartingAsync(node, name).ConfigureAwait(false);
            }

            var result = new StepResult
            {
                Node = node,
                DisplayName = name,
                Status = StepStatus.Skipped,
                SkipReason = reason,
            };
            results[i] = result;
            if (observer is not null)
            {
                await observer.OnStepFinishedAsync(result).ConfigureAwait(false);
            }
        }
    }

    static async Task<NodeOutcome> RunNodeAsync(
        ScenarioNode node,
        IStepInputs inputs,
        IServiceProvider? services,
        string displayName,
        CancellationToken scenarioToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(scenarioToken);
        var context = new ScenarioContext(node.StepId, displayName, services, stepCts.Token);

        try
        {
            object? output;
            if (node.Timeout is { } timeout)
            {
                var invokeTask = node.Invoke(inputs, context);
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stepCts.Token);
                var delayTask = Task.Delay(timeout, delayCts.Token);
                var winner = await Task.WhenAny(invokeTask, delayTask).ConfigureAwait(false);

                if (winner == delayTask && !invokeTask.IsCompleted)
                {
                    await stepCts.CancelAsync().ConfigureAwait(false); // best-effort: ask the operation to stop
                    // Observe the abandoned operation's eventual fault so it doesn't surface as an
                    // unobserved task exception in the test host.
                    _ = invokeTask.ContinueWith(
                        static t => _ = t.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    throw new TimeoutException(
                        $"Step '{displayName}' exceeded its timeout of {timeout}.");
                }

                await delayCts.CancelAsync().ConfigureAwait(false); // operation won the race; stop the timer
                output = await invokeTask.ConfigureAwait(false);
            }
            else
            {
                output = await node.Invoke(inputs, context).ConfigureAwait(false);
            }

            stopwatch.Stop();
            return new NodeOutcome(
                new StepResult
                {
                    Node = node,
                    DisplayName = displayName,
                    Status = StepStatus.Passed,
                    Duration = stopwatch.Elapsed,
                    Logs = context.Logs,
                    Attachments = context.Attachments,
                },
                output);
        }
        catch (OperationCanceledException) when (scenarioToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Outcome(StepStatus.Skipped, skipReason: "scenario canceled");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return Outcome(StepStatus.Failed, exception: ex);
        }

        NodeOutcome Outcome(StepStatus statusValue, Exception? exception = null, string? skipReason = null)
            => new(
                new StepResult
                {
                    Node = node,
                    DisplayName = displayName,
                    Status = statusValue,
                    Duration = stopwatch.Elapsed,
                    Exception = exception,
                    SkipReason = skipReason,
                    Logs = context.Logs,
                    Attachments = context.Attachments,
                },
                null);
    }

    static string FormatName(ScenarioNode node, IStepInputs inputs)
    {
        if (node.FormatDisplayName is null)
        {
            return node.DisplayNameTemplate;
        }

        try
        {
            return node.FormatDisplayName(inputs);
        }
        catch
        {
            // A throwing formatter (or, for skips, unavailable dependency outputs) must not abort
            // the scenario — fall back to the unformatted template.
            return node.DisplayNameTemplate;
        }
    }

    static string BuildSkipReason(List<string>? failed, List<string>? skipped)
    {
        if (failed is not null && skipped is not null)
        {
            return $"dependency failed: {string.Join(", ", failed)}; "
                + $"dependency skipped: {string.Join(", ", skipped)}";
        }

        return failed is not null
            ? $"dependency failed: {string.Join(", ", failed)}"
            : $"dependency skipped: {string.Join(", ", skipped!)}";
    }

    readonly record struct NodeOutcome(StepResult Result, object? Output);

    sealed class StepInputs(object?[] outputs) : IStepInputs
    {
        public T Get<T>(int producerIndex) => (T)outputs[producerIndex]!;
    }
}
