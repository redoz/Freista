using System.Collections.Concurrent;
using PUnit.Model;
using PUnit.Scheduling;
using Xunit.Sdk;
using Xunit.v3;

namespace PUnit;

/// <summary>
/// Bridges the scheduler's <see cref="IStepObserver"/> callbacks onto the xUnit v3 message bus,
/// emitting a <c>TestStarting → (TestPassed|TestFailed|TestSkipped) → TestFinished</c> quartet per
/// step so every scenario step appears as its own visible test in the runner.
/// </summary>
internal sealed class ScenarioStepReporter : IStepObserver
{
    static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> EmptyTraits =
        new Dictionary<string, IReadOnlyCollection<string>>();

    static readonly IReadOnlyDictionary<string, TestAttachment> EmptyAttachments =
        new Dictionary<string, TestAttachment>();

    readonly IMessageBus _bus;
    readonly string _assemblyId;
    readonly string _collectionId;
    readonly string _classId;
    readonly string _methodId;
    readonly string _caseId;
    readonly string _caseDisplayName;
    readonly ConcurrentDictionary<int, DateTimeOffset> _started = new();

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

    public void OnStepStarting(ScenarioNode node, string displayName)
    {
        _started[node.Index] = DateTimeOffset.UtcNow;
        _bus.QueueMessage(new TestStarting
        {
            AssemblyUniqueID = _assemblyId,
            TestCollectionUniqueID = _collectionId,
            TestClassUniqueID = _classId,
            TestMethodUniqueID = _methodId,
            TestCaseUniqueID = _caseId,
            TestUniqueID = TestId(node.Index),
            TestDisplayName = $"{_caseDisplayName} ▸ {displayName}",
            Explicit = false,
            StartTime = _started[node.Index],
            Timeout = 0,
            Traits = EmptyTraits,
        });
    }

    public void OnStepFinished(StepResult result)
    {
        var index = result.Node.Index;
        var testId = TestId(index);
        var executionTime = (decimal)result.Duration.TotalSeconds;
        var finishTime = DateTimeOffset.UtcNow;

        switch (result.Status)
        {
            case StepStatus.Skipped:
                _bus.QueueMessage(new TestSkipped
                {
                    AssemblyUniqueID = _assemblyId,
                    TestCollectionUniqueID = _collectionId,
                    TestClassUniqueID = _classId,
                    TestMethodUniqueID = _methodId,
                    TestCaseUniqueID = _caseId,
                    TestUniqueID = testId,
                    Reason = result.SkipReason ?? "skipped",
                    ExecutionTime = 0m,
                    FinishTime = finishTime,
                    Output = string.Empty,
                    Warnings = null,
                });
                break;

            case StepStatus.Failed:
                _bus.QueueMessage(TestFailed.FromException(
                    result.Exception ?? new InvalidOperationException("step failed"),
                    _assemblyId, _collectionId, _classId, _methodId, _caseId, testId,
                    executionTime, output: string.Empty, warnings: null, finishTime: finishTime));
                break;

            default: // Passed
                _bus.QueueMessage(new TestPassed
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
                });
                break;
        }

        _bus.QueueMessage(new TestFinished
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
        });
    }

    /// <summary>Reports a single failed test for a whole-scenario error (e.g. missing generated graph).</summary>
    public void ReportScenarioError(string displayName, Exception exception)
    {
        OnStepStarting(SyntheticNode, displayName);
        _bus.QueueMessage(TestFailed.FromException(
            exception, _assemblyId, _collectionId, _classId, _methodId, _caseId, TestId(0),
            0m, output: string.Empty, warnings: null, finishTime: DateTimeOffset.UtcNow));
        _bus.QueueMessage(new TestFinished
        {
            AssemblyUniqueID = _assemblyId,
            TestCollectionUniqueID = _collectionId,
            TestClassUniqueID = _classId,
            TestMethodUniqueID = _methodId,
            TestCaseUniqueID = _caseId,
            TestUniqueID = TestId(0),
            ExecutionTime = 0m,
            FinishTime = DateTimeOffset.UtcNow,
            Output = string.Empty,
            Warnings = null,
            Attachments = EmptyAttachments,
        });
    }

    static readonly ScenarioNode SyntheticNode = new()
    {
        Index = 0,
        StepId = "error",
        Phase = "Scenario",
        OperationName = "Scenario",
        DisplayNameTemplate = "scenario",
        DependsOn = [],
        Invoke = (_, _) => System.Threading.Tasks.Task.FromResult<object?>(null),
    };

    string TestId(int index) => UniqueIDGenerator.ForTest(_caseId, index);
}
