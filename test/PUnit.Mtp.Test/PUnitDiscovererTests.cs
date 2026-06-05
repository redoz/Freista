using Microsoft.Testing.Platform.Extensions.Messages;
using PUnit.Mtp;
using PUnit.Model;
using Xunit;

namespace PUnit.Mtp.Test;

/// <summary>
/// Phase 3 behavioral tests for discovery. PUnit's MTP framework turns each registered
/// <see cref="ScenarioDefinition"/> into one <see cref="TestNode"/> per <see cref="ScenarioNode"/>
/// (step nodes only, no parent scenario node), with a stable <c>{ScenarioId}:{StepId}</c> uid, a
/// <c>{scenario} ▸ {step}</c> display name, a <see cref="TestFileLocationProperty"/> for "go to
/// source", and the <see cref="DiscoveredTestNodeStateProperty"/>. These tests drive the pure
/// node-builder directly so they don't need MTP's session machinery.
/// </summary>
public class PUnitDiscovererTests
{
    static ScenarioNode Node(int index, string stepId, string template, string? file = null, int line = 0) => new()
    {
        Index = index,
        StepId = stepId,
        Phase = "Given",
        OperationName = $"Op{index}",
        DisplayNameTemplate = template,
        SourceFile = file,
        SourceLine = line,
        DependsOn = [],
        Invoke = (_, _) => Task.FromResult<object?>(null),
    };

    static ScenarioDefinition Definition(string id = "scn", string display = "my scenario", string method = "Ns.Scn", params ScenarioNode[] nodes) => new()
    {
        ScenarioId = id,
        DisplayName = display,
        MethodName = method,
        Nodes = nodes,
    };

    [Fact]
    public void Emits_one_node_per_step()
    {
        var definition = Definition(
            nodes:
            [
                Node(0, "a", "step a"),
                Node(1, "b", "step b"),
                Node(2, "c", "step c"),
            ]);

        var nodes = PUnitDiscoverer.BuildNodes(definition);

        Assert.Equal(3, nodes.Count);
    }

    [Fact]
    public void Uid_combines_scenario_id_and_step_id()
    {
        var definition = Definition(
            id: "scn-1",
            nodes:
            [
                Node(0, "step-0", "first"),
                Node(1, "step-1", "second"),
            ]);

        var nodes = PUnitDiscoverer.BuildNodes(definition);

        Assert.Equal("scn-1:step-0", nodes[0].Uid.Value);
        Assert.Equal("scn-1:step-1", nodes[1].Uid.Value);
    }

    [Fact]
    public void Display_name_joins_scenario_and_step_template_with_separator()
    {
        var definition = Definition(
            display: "patient booking",
            nodes: [Node(0, "a", "patient Jane exists")]);

        var node = Assert.Single(PUnitDiscoverer.BuildNodes(definition));

        Assert.Equal("patient booking ▸ patient Jane exists", node.DisplayName);
    }

    [Fact]
    public void Node_carries_discovered_state_property()
    {
        var definition = Definition(nodes: [Node(0, "a", "step a")]);

        var node = Assert.Single(PUnitDiscoverer.BuildNodes(definition));

        Assert.NotEmpty(node.Properties.OfType<DiscoveredTestNodeStateProperty>());
    }

    [Fact]
    public void Node_carries_file_location_when_source_is_known()
    {
        var definition = Definition(
            nodes: [Node(0, "a", "step a", file: @"C:\src\Booking.cs", line: 42)]);

        var node = Assert.Single(PUnitDiscoverer.BuildNodes(definition));

        var location = Assert.Single(node.Properties.OfType<TestFileLocationProperty>());
        Assert.Equal(@"C:\src\Booking.cs", location.FilePath);
        Assert.Equal(42, location.LineSpan.Start.Line);
        Assert.Equal(42, location.LineSpan.End.Line);
    }

    [Fact]
    public void Node_carries_method_identity_for_namespace_class_method_grouping()
    {
        // The generator emits MethodName as the scenario method's FQN. Runners (VS Test Explorer,
        // the VSTest bridge) build their namespace -> class -> method tree from a
        // TestMethodIdentifierProperty; without it the step nodes fall under "<Empty Namespace>" /
        // "<Empty Class>". The FQN's last segment is the method, the one before it the class.
        var definition = Definition(
            method: "MyApp.Booking.Scenarios.BookAppointment",
            nodes: [Node(0, "a", "step a")]);

        var node = Assert.Single(PUnitDiscoverer.BuildNodes(definition));

        var id = Assert.Single(node.Properties.OfType<TestMethodIdentifierProperty>());
        Assert.Equal("MyApp.Booking", id.Namespace);
        Assert.Equal("Scenarios", id.TypeName);
        Assert.Equal("BookAppointment", id.MethodName);
    }

    [Fact]
    public void Node_omits_file_location_when_source_is_unknown()
    {
        // No file / line 0 -> no "go to source" target, so no location property is attached.
        var definition = Definition(nodes: [Node(0, "a", "step a", file: null, line: 0)]);

        var node = Assert.Single(PUnitDiscoverer.BuildNodes(definition));

        Assert.Empty(node.Properties.OfType<TestFileLocationProperty>());
    }
}
