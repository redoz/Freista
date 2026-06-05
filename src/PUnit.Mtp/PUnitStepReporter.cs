using System.Globalization;
using System.Text;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Messages;
using Microsoft.Testing.Platform.TestHost;
using PUnit.Model;
using PUnit.Scheduling;

namespace PUnit.Mtp;

/// <summary>
/// Bridges the scheduler's <see cref="IStepObserver"/> callbacks onto the Microsoft.Testing.Platform
/// message bus, publishing one <see cref="TestNodeUpdateMessage"/> per step lifecycle event so each
/// scenario step appears (and updates live) as its own first-class MTP test node. This replaces the
/// xUnit-bus <c>ScenarioStepReporter</c>; because MTP's publish path has no filter/lifecycle gate,
/// every step the scheduler executes lights up — including dependency siblings of a single-step run.
/// </summary>
/// <remarks>
/// <para>
/// One reporter is created per scenario run so it can stamp the scenario's id (for the stable
/// <c>{ScenarioId}:{StepId}</c> uid that discovery also emits) and the scenario display-name prefix
/// onto every node. The step-to-state mapping follows the design §6 table:
/// </para>
/// <list type="bullet">
///   <item>start -> <see cref="InProgressTestNodeStateProperty"/>;</item>
///   <item><see cref="StepStatus.Passed"/> -> <see cref="PassedTestNodeStateProperty"/>;</item>
///   <item><see cref="StepStatus.Failed"/> with a <see cref="TimeoutException"/> -> <see cref="TimeoutTestNodeStateProperty"/>;</item>
///   <item><see cref="StepStatus.Failed"/> with an assertion exception -> <see cref="FailedTestNodeStateProperty"/>;</item>
///   <item><see cref="StepStatus.Failed"/> with any other exception -> <see cref="ErrorTestNodeStateProperty"/>;</item>
///   <item><see cref="StepStatus.Skipped"/> -> <see cref="SkippedTestNodeStateProperty"/> (with the skip reason).</item>
/// </list>
/// <para>
/// Finished updates additionally carry a <see cref="TimingProperty"/>, the step's
/// <see cref="TestFileLocationProperty"/>, the runtime-formatted display name, and the step's
/// captured logs (as standard output) and attachments (as file artifacts).
/// </para>
/// <para>
/// The <see cref="IStepObserver"/> callbacks are asynchronous: each awaits the platform's
/// <see cref="IMessageBus.PublishAsync"/> directly instead of blocking on it. The scheduler raises
/// them serially, and this type holds no mutable state, so it is safe to share across a scenario's
/// concurrently-running steps.
/// </para>
/// </remarks>
internal sealed class PUnitStepReporter : IStepObserver
{
    readonly ScenarioDefinition definition;
    readonly IReadOnlyDictionary<int, string> labels;
    readonly SessionUid sessionUid;
    readonly IMessageBus messageBus;
    readonly IDataProducer producer;

    public PUnitStepReporter(
        ScenarioDefinition definition,
        SessionUid sessionUid,
        IMessageBus messageBus,
        IDataProducer producer)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(messageBus);
        ArgumentNullException.ThrowIfNull(producer);

        this.definition = definition;
        this.labels = ScenarioStepNumbering.Compute(definition);
        this.sessionUid = sessionUid;
        this.messageBus = messageBus;
        this.producer = producer;
    }

    public Task OnStepStartingAsync(StepContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var testNode = BuildNode(context.Node, context.DisplayName);
        testNode.Properties.Add(InProgressTestNodeStateProperty.CachedInstance);
        return Publish(testNode);
    }

    public Task OnStepFinishedAsync(StepResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var node = result.Node;
        var testNode = BuildNode(node, result.DisplayName);

        testNode.Properties.Add(MapState(result));

        // Take a single "now" so the reported start/finish are exactly Duration apart (calling UtcNow
        // twice would make finish slightly later than start + Duration). The scheduler reports a
        // wall-clock duration but not absolute timestamps, so we anchor the window at the finish.
        var finish = DateTimeOffset.UtcNow;
        testNode.Properties.Add(new TimingProperty(
            new TimingInfo(finish - result.Duration, finish, result.Duration)));

        AddOutput(testNode, result);
        AddAttachments(testNode, result);

        return Publish(testNode);
    }

    /// <summary>Maps a terminal <see cref="StepResult"/> onto its MTP node-state property (design §6).</summary>
    static IProperty MapState(StepResult result) => result.Status switch
    {
        StepStatus.Passed => PassedTestNodeStateProperty.CachedInstance,
        StepStatus.Skipped => new SkippedTestNodeStateProperty(result.SkipReason ?? "skipped"),
        StepStatus.Failed => MapFailure(result.Exception),
        // Pending/Running are not terminal; the scheduler never reports them as a finished result.
        _ => new ErrorTestNodeStateProperty(
            new InvalidOperationException(
                string.Format(CultureInfo.InvariantCulture, "Unexpected terminal step status '{0}'.", result.Status))),
    };

    static IProperty MapFailure(Exception? exception)
    {
        var ex = exception ?? new InvalidOperationException("Step failed without an exception.");
        return ex switch
        {
            TimeoutException => new TimeoutTestNodeStateProperty(ex),
            _ when IsAssertionException(ex) => new FailedTestNodeStateProperty(ex),
            _ => new ErrorTestNodeStateProperty(ex),
        };
    }

    /// <summary>
    /// Recognizes an assertion failure without taking a compile-time dependency on any assertion
    /// library (PUnit.Mtp references only MTP + PUnit core). xunit.v3.assert raises
    /// <c>Xunit.Sdk.XunitException</c>; Shouldly/NUnit/etc. raise types whose name or interface ends
    /// in <c>AssertionException</c>. Anything else is treated as an unexpected error.
    /// </summary>
    static bool IsAssertionException(Exception exception)
    {
        for (var type = exception.GetType(); type is not null; type = type.BaseType)
        {
            if (type.Name == "XunitException" || type.Name.EndsWith("AssertionException", StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var iface in exception.GetType().GetInterfaces())
        {
            if (iface.Name.EndsWith("AssertionException", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the per-step <see cref="TestNode"/>: the stable <c>{ScenarioId}:{StepId}</c> uid (so a
    /// run update lands on the same node discovery emitted), the numbered, runtime-formatted display
    /// name, and the file location when the step's source is known.
    /// </summary>
    TestNode BuildNode(ScenarioNode node, string displayName)
    {
        var testNode = new TestNode
        {
            Uid = PUnitDiscoverer.MakeUid(definition.ScenarioId, node.StepId),
            DisplayName = ScenarioStepNumbering.Format(labels, node, displayName),
        };

        testNode.Properties.Add(ScenarioTestIdentity.Create(definition.MethodName, definition.DisplayName, definition.ClassDisplayName));

        if (!string.IsNullOrEmpty(node.SourceFile) && node.SourceLine > 0)
        {
            var position = new LinePosition(node.SourceLine, 0);
            testNode.Properties.Add(new TestFileLocationProperty(
                node.SourceFile, new LinePositionSpan(position, position)));
        }

        return testNode;
    }

    static void AddOutput(TestNode testNode, StepResult result)
    {
        if (result.Logs.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        foreach (var line in result.Logs)
        {
            builder.AppendLine(line);
        }

#pragma warning disable TPEXP // StandardOutputProperty is experimental in MTP 1.9.1 but stable enough for v1.
        testNode.Properties.Add(new StandardOutputProperty(builder.ToString()));
#pragma warning restore TPEXP
    }

    void AddAttachments(TestNode testNode, StepResult result)
    {
        if (result.Attachments.Count == 0)
        {
            return;
        }

        // Mirror xUnit's MTP sink: persist string attachments to temp files and surface them as
        // file artifacts so runners/TRX can collect them. Best-effort — a failed write must not
        // abort reporting.
        string? basePath = null;
        foreach (var (name, value) in result.Attachments)
        {
            try
            {
                basePath ??= CreateAttachmentDirectory(result);
                var path = Path.Combine(basePath, SanitizeFileName(name) + ".txt");
                File.WriteAllText(path, value);
                testNode.Properties.Add(new FileArtifactProperty(new FileInfo(path), name));
            }
            catch (IOException)
            {
                // Skip this attachment; keep reporting the rest of the node.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    string CreateAttachmentDirectory(StepResult result)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "punit-mtp",
            SanitizeFileName(PUnitDiscoverer.MakeUid(definition.ScenarioId, result.Node.StepId)));
        Directory.CreateDirectory(path);
        return path;
    }

    static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        return builder.ToString();
    }

    Task Publish(TestNode testNode)
    {
        NodeDiagnostics.Log("run", testNode);
        return messageBus.PublishAsync(producer, new TestNodeUpdateMessage(sessionUid, testNode));
    }
}
