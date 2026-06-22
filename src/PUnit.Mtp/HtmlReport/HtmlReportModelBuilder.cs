using System.Globalization;
using PUnit;
using PUnit.Model;

namespace PUnit.Mtp.HtmlReport;

/// <summary>
/// Builds the deterministic <see cref="HtmlReportModel"/> from the run-event stream. All layout
/// (lane packing, resource rollup, ms-offset reduction) happens here — not in the renderer — so the
/// JSON is snapshot-testable (design §4). Drive it with <see cref="OnScenarioStarted"/> then one
/// <see cref="OnStepFinished"/> per terminal step, in scheduler order, then <see cref="Build"/>.
/// </summary>
internal sealed class HtmlReportModelBuilder
{
    private readonly List<ScenarioAccumulator> _scenarios = [];
    private ScenarioAccumulator? _current;

    public void OnScenarioStarted(ScenarioDefinition definition)
    {
        _current = new ScenarioAccumulator(definition);
        _scenarios.Add(_current);
    }

    public void OnStepFinished(ScenarioDefinition definition, StepResult result)
    {
        var acc = _scenarios.LastOrDefault(s => s.Definition.ScenarioId == definition.ScenarioId)
                  ?? throw new InvalidOperationException(
                      $"StepFinished for '{definition.ScenarioId}' before its ScenarioStarted.");
        acc.Add(result);
    }

    public HtmlReportModel Build(string generatedAtUtc)
    {
        var scenarios = _scenarios.Select(s => s.Build()).ToList();
        var summary = new ReportSummary
        {
            Passed = scenarios.Count(s => s.Status == "passed"),
            Failed = scenarios.Count(s => s.Status == "failed"),
            Skipped = scenarios.Count(s => s.Status == "skipped"),
            TotalMs = scenarios.Sum(s => s.DurationMs),
        };

        return new HtmlReportModel
        {
            GeneratedAtUtc = generatedAtUtc,
            Summary = summary,
            Scenarios = scenarios,
        };
    }

    private sealed class ScenarioAccumulator(ScenarioDefinition definition)
    {
        private readonly List<StepResult> _results = [];
        public ScenarioDefinition Definition { get; } = definition;

        public void Add(StepResult result) => _results.Add(result);

        public ReportScenario Build()
        {
            var start = _results.Count == 0
                ? DateTimeOffset.UnixEpoch
                : _results.Min(r => r.StartedAt);

            var ordered = _results.OrderBy(r => r.Node.Index).ToList();
            var lanes = PackLanes(ordered, start);

            var steps = new List<ReportStep>(ordered.Count);
            for (var i = 0; i < ordered.Count; i++)
            {
                var r = ordered[i];
                steps.Add(new ReportStep
                {
                    StepId = r.Node.StepId,
                    Index = r.Node.Index,
                    Label = r.Node.Index.ToString(CultureInfo.InvariantCulture),
                    Phase = r.Node.Phase,
                    DisplayName = r.DisplayName,
                    Status = StatusText(r.Status),
                    OffsetMs = Ms(r.StartedAt - start),
                    DurationMs = Ms(r.Duration),
                    Lane = lanes[i],
                    DependsOn = r.Node.DependsOn,
                    GroupId = r.Node.GroupId,
                    Logs = r.Logs,
                    Effects = r.Effects.Select(e => new ReportEffect
                    {
                        Verb = e.Verb.ToString(),
                        Type = e.Identity.Type.Name,
                        Key = e.Identity.Key.ToString(),
                        OffsetMs = Ms(e.Timestamp - start),
                        Data = e.Data?.ToString(),
                    }).ToList(),
                    Exception = r.Exception?.ToString(),
                    SkipReason = r.SkipReason,
                });
            }

            var resources = ordered
                .SelectMany(r => r.Effects)
                .GroupBy(e => (e.Identity.Type.Name, Key: e.Identity.Key.ToString()))
                .Select(g => new ReportResource
                {
                    Type = g.Key.Name,
                    Key = g.Key.Key,
                    Events = g.Select(e => new ReportResourceEvent
                    {
                        Verb = e.Verb.ToString(),
                        OffsetMs = Ms(e.Timestamp - start),
                        StepId = e.StepId ?? string.Empty,
                    }).ToList(),
                })
                .ToList();

            // Lineage relations (2026-06-22 spec): relations are recorded explicitly at runtime from each
            // [References]/[Consumes] target's declared subjects. Map them straight through; dedup by
            // (subject, target) across the scenario. No subject inference.
            var references = new List<ReportReference>();
            var seenRelations = new HashSet<(string, string, string, string)>();
            foreach (var r in ordered)
            {
                foreach (var relation in r.Lineage)
                {
                    var subjectType = relation.Subject.Type.Name;
                    var subjectKey = relation.Subject.Key.ToString();
                    var targetType = relation.Target.Type.Name;
                    var targetKey = relation.Target.Key.ToString();
                    if (!seenRelations.Add((subjectType, subjectKey, targetType, targetKey)))
                    {
                        continue;
                    }

                    references.Add(new ReportReference
                    {
                        SubjectType = subjectType,
                        SubjectKey = subjectKey,
                        TargetType = targetType,
                        TargetKey = targetKey,
                        Kind = relation.Kind.ToString(),
                    });
                }
            }

            var status = steps.Any(s => s.Status == "failed") ? "failed"
                : steps.Any(s => s.Status == "skipped") ? "skipped"
                : "passed";

            var durationMs = steps.Count == 0 ? 0 : steps.Max(s => s.OffsetMs + s.DurationMs);

            return new ReportScenario
            {
                ScenarioId = Definition.ScenarioId,
                DisplayName = Definition.DisplayName,
                ClassDisplayName = Definition.ClassDisplayName,
                MethodName = Definition.MethodName,
                StartedAtUtc = start.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                DurationMs = durationMs,
                Status = status,
                Steps = steps,
                Resources = resources,
                References = references,
            };
        }

        // Greedy interval packing: a step takes the first lane whose last bar ended at/before its start.
        private static int[] PackLanes(List<StepResult> ordered, DateTimeOffset start)
        {
            var laneEnds = new List<double>();
            var lanes = new int[ordered.Count];
            for (var i = 0; i < ordered.Count; i++)
            {
                var s = Ms(ordered[i].StartedAt - start);
                var e = s + Ms(ordered[i].Duration);
                var lane = -1;
                for (var l = 0; l < laneEnds.Count; l++)
                {
                    if (laneEnds[l] <= s) { lane = l; break; }
                }

                if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(e); }
                else { laneEnds[lane] = e; }

                lanes[i] = lane;
            }

            return lanes;
        }

        private static double Ms(TimeSpan span) => span.TotalMilliseconds;

        private static string StatusText(StepStatus status) => status switch
        {
            StepStatus.Passed => "passed",
            StepStatus.Failed => "failed",
            StepStatus.Skipped => "skipped",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }
}
