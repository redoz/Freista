using System.Linq;
using System.Text.Json;
using PUnit;
using PUnit.Model;
using static VerifyXunit.Verifier;
using Xunit;

namespace PUnit.Mtp.Test;

public class HtmlReportModelBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static ScenarioNode Node(int index, string stepId, string phase, string template,
        int[]? dependsOn = null, string? group = null) => new()
    {
        Index = index, StepId = stepId, Phase = phase, OperationName = $"Op{index}",
        DisplayNameTemplate = template, DependsOn = dependsOn ?? [], GroupId = group,
        Invoke = (_, _) => Task.FromResult<object?>(null),
    };

    private static ScenarioDefinition Def(params ScenarioNode[] nodes) => new()
    {
        ScenarioId = "scn", DisplayName = "customer books", MethodName = "Ns.Booking",
        ClassDisplayName = "Appointment booking", Nodes = nodes,
    };

    private static StepResult Result(ScenarioNode node, DateTimeOffset startedAt, double ms,
        StepStatus status = StepStatus.Passed, IReadOnlyList<ResourceEffect>? effects = null,
        IReadOnlyList<string>? logs = null) => new()
    {
        Node = node, DisplayName = node.DisplayNameTemplate, Status = status,
        StartedAt = startedAt, Duration = TimeSpan.FromMilliseconds(ms),
        Effects = effects ?? [], Logs = logs ?? [],
    };

    [Fact]
    public Task Builds_the_expected_json_model()
    {
        var n0 = Node(0, "p", "Given", "Given patient Jane exists");
        var n1 = Node(1, "s", "Given", "Given an available slot exists");
        var n2 = Node(2, "c", "When", "When creating an appointment", dependsOn: [0, 1]);
        var def = Def(n0, n1, n2);

        var builder = new HtmlReport.HtmlReportModelBuilder();
        builder.OnScenarioStarted(def);
        builder.OnStepFinished(def, Result(n0, T0, 40, effects:
        [
            new ResourceEffect
            {
                Verb = LifecycleVerb.Create, Identity = new ResourceIdentity(typeof(string), "Jane"),
                StepId = "p", Timestamp = T0.AddMilliseconds(1),
            },
        ]));
        builder.OnStepFinished(def, Result(n1, T0, 30));                 // concurrent with n0 → lane 1
        builder.OnStepFinished(def, Result(n2, T0.AddMilliseconds(40), 50));

        var model = builder.Build(generatedAtUtc: "2026-06-09T12:00:01Z");
        var json = JsonSerializer.Serialize(model, JsonOptions);
        return Verify(json);
    }

    [Fact]
    public void Overlapping_steps_are_packed_into_separate_lanes()
    {
        var n0 = Node(0, "a", "Given", "a");
        var n1 = Node(1, "b", "Given", "b");
        var def = Def(n0, n1);

        var builder = new HtmlReport.HtmlReportModelBuilder();
        builder.OnScenarioStarted(def);
        builder.OnStepFinished(def, Result(n0, T0, 100));               // [0,100)
        builder.OnStepFinished(def, Result(n1, T0.AddMilliseconds(10), 50)); // [10,60) overlaps → lane 1

        var scenario = Assert.Single(builder.Build("x").Scenarios);
        Assert.Equal(0, scenario.Steps[0].Lane);
        Assert.Equal(1, scenario.Steps[1].Lane);
    }

    [Fact]
    public void Sequential_steps_reuse_lane_zero()
    {
        var n0 = Node(0, "a", "Given", "a");
        var n1 = Node(1, "b", "When", "b", dependsOn: [0]);
        var def = Def(n0, n1);

        var builder = new HtmlReport.HtmlReportModelBuilder();
        builder.OnScenarioStarted(def);
        builder.OnStepFinished(def, Result(n0, T0, 50));                // [0,50)
        builder.OnStepFinished(def, Result(n1, T0.AddMilliseconds(50), 50)); // [50,100) no overlap → lane 0

        var scenario = Assert.Single(builder.Build("x").Scenarios);
        Assert.Equal(0, scenario.Steps[0].Lane);
        Assert.Equal(0, scenario.Steps[1].Lane);
    }

    [Fact]
    public void Resource_effects_roll_up_into_one_lifeline_per_identity()
    {
        var n0 = Node(0, "a", "Given", "a");
        var def = Def(n0);
        var id = new ResourceIdentity(typeof(string), "Jane");

        var builder = new HtmlReport.HtmlReportModelBuilder();
        builder.OnScenarioStarted(def);
        builder.OnStepFinished(def, Result(n0, T0, 10, effects:
        [
            new ResourceEffect { Verb = LifecycleVerb.Create, Identity = id, StepId = "a", Timestamp = T0.AddMilliseconds(2) },
        ]));

        var scenario = Assert.Single(builder.Build("x").Scenarios);
        var resource = Assert.Single(scenario.Resources);
        Assert.Equal("String", resource.Type);
        Assert.Equal("Jane", resource.Key);
        Assert.Equal("Create", Assert.Single(resource.Events).Verb);
    }

    [Fact]
    public void References_and_consumes_derive_lineage_edges_from_the_step_subject()
    {
        var n0 = Node(0, "c", "When", "When creating an appointment");
        var def = Def(n0);
        var appointment = new ResourceIdentity(typeof(string), "appt-1");
        var patient = new ResourceIdentity(typeof(string), "Jane");
        var slot = new ResourceIdentity(typeof(int), "7");

        var builder = new HtmlReport.HtmlReportModelBuilder();
        builder.OnScenarioStarted(def);
        builder.OnStepFinished(def, Result(n0, T0, 10, effects:
        [
            new ResourceEffect { Verb = LifecycleVerb.Reference, Identity = patient, StepId = "c", Timestamp = T0.AddMilliseconds(1) },
            new ResourceEffect { Verb = LifecycleVerb.Consume, Identity = slot, StepId = "c", Timestamp = T0.AddMilliseconds(2) },
            new ResourceEffect { Verb = LifecycleVerb.Create, Identity = appointment, StepId = "c", Timestamp = T0.AddMilliseconds(3) },
        ]));

        var scenario = Assert.Single(builder.Build("x").Scenarios);
        Assert.Equal(2, scenario.References.Count);

        var aggregation = scenario.References.Single(e => e.Kind == "Reference");
        Assert.Equal("String", aggregation.SubjectType);
        Assert.Equal("appt-1", aggregation.SubjectKey);
        Assert.Equal("String", aggregation.TargetType);
        Assert.Equal("Jane", aggregation.TargetKey);

        var composition = scenario.References.Single(e => e.Kind == "Consume");
        Assert.Equal("appt-1", composition.SubjectKey);
        Assert.Equal("Int32", composition.TargetType);
        Assert.Equal("7", composition.TargetKey);
    }

    [Fact]
    public void A_reference_effect_without_a_subject_yields_no_edge()
    {
        var n0 = Node(0, "t", "Then", "Then the appointment should exist");
        var def = Def(n0);
        var patient = new ResourceIdentity(typeof(string), "Jane");

        var builder = new HtmlReport.HtmlReportModelBuilder();
        builder.OnScenarioStarted(def);
        builder.OnStepFinished(def, Result(n0, T0, 10, effects:
        [
            new ResourceEffect { Verb = LifecycleVerb.Reference, Identity = patient, StepId = "t", Timestamp = T0.AddMilliseconds(1) },
        ]));

        var scenario = Assert.Single(builder.Build("x").Scenarios);
        Assert.Empty(scenario.References);
    }
}
