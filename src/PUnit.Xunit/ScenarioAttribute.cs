using System.Runtime.CompilerServices;
using Xunit;
using Xunit.v3;

namespace PUnit;

/// <summary>
/// Marks a static method as a PUnit scenario. The method body is source for the generator (xUnit
/// never executes it); discovery is routed to <see cref="ScenarioDiscoverer"/>, which produces one
/// scenario test case whose generated graph is run with each step reported as its own test.
///
/// It lives in the xUnit package (deriving <see cref="FactAttribute"/>) so xUnit's attribute-driven
/// discovery can find it, but stays in namespace <c>PUnit</c> so authoring is just <c>using PUnit;</c>.
/// The generator matches it by metadata name, so it needs no runtime coupling to the generator.
/// </summary>
[XunitTestCaseDiscoverer(typeof(ScenarioDiscoverer))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ScenarioAttribute : FactAttribute
{
    public ScenarioAttribute(
        string? displayName = null,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        ScenarioDisplayName = displayName;
    }

    /// <summary>Optional scenario display name; the generator reads it to name the scenario.</summary>
    public string? ScenarioDisplayName { get; }
}
