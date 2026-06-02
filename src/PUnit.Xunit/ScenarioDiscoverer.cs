using Xunit.Sdk;
using Xunit.v3;

namespace PUnit;

/// <summary>Creates one <see cref="ScenarioTestCase"/> per <c>[Scenario]</c> method.</summary>
public sealed class ScenarioDiscoverer : IXunitTestCaseDiscoverer
{
    public ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        IFactAttribute factAttribute)
    {
        var details = TestIntrospectionHelper.GetTestCaseDetails(discoveryOptions, testMethod, factAttribute);

        IXunitTestCase testCase = new ScenarioTestCase(
            details.ResolvedTestMethod,
            details.TestCaseDisplayName,
            details.UniqueID,
            details.Explicit,
            sourceFilePath: details.SourceFilePath,
            sourceLineNumber: details.SourceLineNumber,
            timeout: details.Timeout);

        return new ValueTask<IReadOnlyCollection<IXunitTestCase>>(new[] { testCase });
    }
}
