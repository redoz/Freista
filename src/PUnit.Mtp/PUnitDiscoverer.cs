using Microsoft.Testing.Platform.Extensions.Messages;
using PUnit.Model;

namespace PUnit.Mtp;

/// <summary>
/// Turns a lowered <see cref="ScenarioDefinition"/> into Microsoft.Testing.Platform
/// <see cref="TestNode"/>s — one per <see cref="ScenarioNode"/> (step), with no parent scenario
/// node. (Decision ①: no runner renders the MTP hierarchy today, so a parent node would be a
/// dangling flat sibling; scenario identity lives in the display-name prefix instead.)
/// </summary>
/// <remarks>
/// Each node gets:
/// <list type="bullet">
///   <item>a stable <c>{ScenarioId}:{StepId}</c> uid so single-step run filters resolve to the
///   same node that discovery emitted;</item>
///   <item>a <c>{scenario} ▸ {step template}</c> display name (the template; the reporter refines
///   runtime-bound names at execution time);</item>
///   <item>a <see cref="TestFileLocationProperty"/> for "go to source" when the step's source is
///   known;</item>
///   <item>the <see cref="DiscoveredTestNodeStateProperty"/>.</item>
/// </list>
/// This is a pure function (no MTP session machinery), so it is unit-testable directly and reused
/// by the framework's discovery request path.
/// </remarks>
internal static class PUnitDiscoverer
{
    /// <summary>The scenario/step separator used in display names (matches the xUnit reporter).</summary>
    internal const string DisplayNameSeparator = " ▸ ";

    /// <summary>Builds the per-step <see cref="TestNode"/> list for one scenario.</summary>
    public static IReadOnlyList<TestNode> BuildNodes(ScenarioDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var nodes = new List<TestNode>(definition.Nodes.Count);
        foreach (var step in definition.Nodes)
        {
            nodes.Add(BuildNode(definition, step));
        }

        return nodes;
    }

    /// <summary>Builds the <see cref="TestNode"/> for a single scenario step.</summary>
    public static TestNode BuildNode(ScenarioDefinition definition, ScenarioNode step)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(step);

        var node = new TestNode
        {
            Uid = MakeUid(definition.ScenarioId, step.StepId),
            DisplayName = definition.DisplayName + DisplayNameSeparator + step.DisplayNameTemplate,
        };

        node.Properties.Add(DiscoveredTestNodeStateProperty.CachedInstance);
        node.Properties.Add(ScenarioTestIdentity.Create(definition.MethodName));

        if (TryMakeFileLocation(step, out var location))
        {
            node.Properties.Add(location);
        }

        return node;
    }

    /// <summary>The stable node uid for a step: <c>{ScenarioId}:{StepId}</c>.</summary>
    public static string MakeUid(string scenarioId, string stepId) => scenarioId + ":" + stepId;

    static bool TryMakeFileLocation(ScenarioNode step, out TestFileLocationProperty location)
    {
        // SourceLine is 1-based, or 0 when unknown; without a file there is nothing to navigate to.
        if (!string.IsNullOrEmpty(step.SourceFile) && step.SourceLine > 0)
        {
            var position = new LinePosition(step.SourceLine, 0);
            location = new TestFileLocationProperty(step.SourceFile, new LinePositionSpan(position, position));
            return true;
        }

        location = null!;
        return false;
    }
}
