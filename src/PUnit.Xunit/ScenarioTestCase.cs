using PUnit.Model;
using PUnit.Scheduling;
using Xunit.Sdk;
using Xunit.v3;

namespace PUnit;

/// <summary>
/// One xUnit test case per <c>[Scenario]</c>. It self-executes: at run time it resolves the
/// generated <see cref="ScenarioDefinition"/> from <see cref="ScenarioRegistry"/> (keyed by method
/// name, which survives serialization on the base type) and drives the <see cref="ScenarioScheduler"/>,
/// reporting each graph step as its own visible test via <see cref="ScenarioStepReporter"/>.
/// </summary>
public sealed class ScenarioTestCase : XunitTestCase, ISelfExecutingXunitTestCase
{
    [Obsolete("Called by the de-serializer", error: false)]
    public ScenarioTestCase()
    {
    }

    public ScenarioTestCase(
        IXunitTestMethod testMethod,
        string testCaseDisplayName,
        string uniqueID,
        bool @explicit,
        string? sourceFilePath = null,
        int? sourceLineNumber = null,
        int? timeout = null)
        : base(
            testMethod,
            testCaseDisplayName,
            uniqueID,
            @explicit,
            sourceFilePath: sourceFilePath,
            sourceLineNumber: sourceLineNumber,
            timeout: timeout)
    {
    }

    public async ValueTask<RunSummary> Run(
        ExplicitOption explicitOption,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
    {
        var summary = new RunSummary();

        var reporter = new ScenarioStepReporter(
            messageBus,
            TestCollection.TestAssembly.UniqueID,
            TestCollection.UniqueID,
            TestClass?.UniqueID ?? string.Empty,
            TestMethod?.UniqueID ?? string.Empty,
            UniqueID,
            TestCaseDisplayName);

        // xUnit names nested host types with a metadata '+' separator; the generator keys on the
        // Roslyn display form (with '.'). Normalize so both agree. (Generic host types are out of
        // the v1 scenario subset.)
        var methodFullName = TestClassName.Replace('+', '.') + "." + TestMethodName;
        if (!ScenarioRegistry.TryGet(methodFullName, out var factory) || factory is null)
        {
            reporter.ReportScenarioError(
                TestCaseDisplayName,
                new InvalidOperationException(
                    $"No generated scenario was found for '{methodFullName}'. Ensure the PUnit "
                    + "source generator is referenced by this test project."));
            summary.Total = 1;
            summary.Failed = 1;
            return summary;
        }

        ScenarioDefinition definition;
        try
        {
            definition = factory();
        }
        catch (Exception ex)
        {
            reporter.ReportScenarioError(TestCaseDisplayName, ex);
            summary.Total = 1;
            summary.Failed = 1;
            return summary;
        }

        var results = await new ScenarioScheduler().RunAsync(
            definition,
            services: null,
            observer: reporter,
            cancellationToken: cancellationTokenSource.Token);

        foreach (var result in results)
        {
            summary.Total++;
            summary.Time += (decimal)result.Duration.TotalSeconds;
            switch (result.Status)
            {
                case StepStatus.Failed:
                    summary.Failed++;
                    break;
                case StepStatus.Skipped:
                    summary.Skipped++;
                    break;
            }
        }

        return summary;
    }
}
