using PUnit.Model;
using PUnit.Scheduling;
using Xunit.Sdk;
using Xunit.v3;

namespace PUnit;

/// <summary>
/// Bridges the scheduler's <see cref="IStepObserver"/> callbacks onto the xUnit v3 message bus,
/// emitting a <c>TestStarting → (TestPassed|TestFailed|TestSkipped) → TestFinished</c> quartet per
/// step so every scenario step appears as its own visible test in the runner. The six routing
/// UniqueIDs are shared, so each message kind is built by a small factory.
/// </summary>
internal sealed class ScenarioStepReporter : IStepObserver
{
    static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> EmptyTraits =
        new Dictionary<string, IReadOnlyCollection<string>>();

    static readonly IReadOnlyDictionary<string, TestAttachment> EmptyAttachments =
        new Dictionary<string, TestAttachment>();

    static readonly ScenarioNode SyntheticNode = new()
    {
        Index = 0,
        StepId = "error",
        Phase = "Scenario",
        OperationName = "Scenario",
        DisplayNameTemplate = "scenario",
        DependsOn = [],
        Invoke = (_, _) => Task.FromResult<object?>(null),
    };

    readonly IMessageBus _bus;
    readonly string _assemblyId;
    readonly string _collectionId;
    readonly string _classId;
    readonly string _methodId;
    readonly string _caseId;
    readonly string _caseDisplayName;

    public ScenarioStepReporter(
        IMessageBus bus,
        string assemblyId,
        string collectionId,
        string classId,
        string methodId,
        string caseId,
        string caseDisplayName)
    {
        _bus = bus;
        _assemblyId = assemblyId;
        _collectionId = collectionId;
        _classId = classId;
        _methodId = methodId;
        _caseId = caseId;
        _caseDisplayName = caseDisplayName;
    }

    public Task OnStepStartingAsync(ScenarioNode node, string displayName)
    {
        EmitStarting(node, displayName);
        return Task.CompletedTask;
    }

    void EmitStarting(ScenarioNode node, string displayName)
        => _bus.QueueMessage(new TestStarting
        {
            AssemblyUniqueID = _assemblyId,
            TestCollectionUniqueID = _collectionId,
            TestClassUniqueID = _classId,
            TestMethodUniqueID = _methodId,
            TestCaseUniqueID = _caseId,
            TestUniqueID = TestId(node.Index),
            TestDisplayName = $"{_caseDisplayName} ▸ {displayName}",
            Explicit = false,
            StartTime = DateTimeOffset.UtcNow,
            Timeout = 0,
            Traits = EmptyTraits,
        });

    public Task OnStepFinishedAsync(StepResult result)
    {
        var testId = TestId(result.Node.Index);
        var executionTime = (decimal)result.Duration.TotalSeconds;
        var finishTime = DateTimeOffset.UtcNow;

        _bus.QueueMessage(result.Status switch
        {
            StepStatus.Skipped => Skipped(testId, result.SkipReason ?? "skipped", finishTime),
            StepStatus.Failed => Failed(testId, result.Exception, executionTime, finishTime),
            _ => Passed(testId, executionTime, finishTime),
        });

        _bus.QueueMessage(Finished(testId, executionTime, finishTime));
        return Task.CompletedTask;
    }

    /// <summary>Reports a single failed test for a whole-scenario error (e.g. missing generated graph).</summary>
    public void ReportScenarioError(string displayName, Exception exception)
    {
        EmitStarting(SyntheticNode, displayName);
        var testId = TestId(0);
        var now = DateTimeOffset.UtcNow;
        _bus.QueueMessage(Failed(testId, exception, executionTime: 0m, now));
        _bus.QueueMessage(Finished(testId, executionTime: 0m, now));
    }

    TestPassed Passed(string testId, decimal executionTime, DateTimeOffset finishTime) => new()
    {
        AssemblyUniqueID = _assemblyId,
        TestCollectionUniqueID = _collectionId,
        TestClassUniqueID = _classId,
        TestMethodUniqueID = _methodId,
        TestCaseUniqueID = _caseId,
        TestUniqueID = testId,
        ExecutionTime = executionTime,
        FinishTime = finishTime,
        Output = string.Empty,
        Warnings = null,
    };

    TestSkipped Skipped(string testId, string reason, DateTimeOffset finishTime) => new()
    {
        AssemblyUniqueID = _assemblyId,
        TestCollectionUniqueID = _collectionId,
        TestClassUniqueID = _classId,
        TestMethodUniqueID = _methodId,
        TestCaseUniqueID = _caseId,
        TestUniqueID = testId,
        Reason = reason,
        ExecutionTime = 0m,
        FinishTime = finishTime,
        Output = string.Empty,
        Warnings = null,
    };

    IMessageSinkMessage Failed(string testId, Exception? exception, decimal executionTime, DateTimeOffset finishTime)
        => TestFailed.FromException(
            exception ?? new InvalidOperationException("step failed"),
            _assemblyId, _collectionId, _classId, _methodId, _caseId, testId,
            executionTime, output: string.Empty, warnings: null, finishTime: finishTime);

    TestFinished Finished(string testId, decimal executionTime, DateTimeOffset finishTime) => new()
    {
        AssemblyUniqueID = _assemblyId,
        TestCollectionUniqueID = _collectionId,
        TestClassUniqueID = _classId,
        TestMethodUniqueID = _methodId,
        TestCaseUniqueID = _caseId,
        TestUniqueID = testId,
        ExecutionTime = executionTime,
        FinishTime = finishTime,
        Output = string.Empty,
        Warnings = null,
        Attachments = EmptyAttachments,
    };

    string TestId(int index) => UniqueIDGenerator.ForTest(_caseId, index);
}
