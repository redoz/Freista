# OTEL Step Correlation — Design

- **Date:** 2026-09-04
- **Status:** **SHELVED 2026-09-04**, not rejected. The design is sound and the late-arrival problem
  has a workable answer (option A below), but the cost — losing live per-step progress within a
  scenario — was judged not worth it for the value, given that an `ILoggerProvider` gets most of the
  benefit for an in-process SUT at a fraction of the cost. Revisit **only** when an out-of-process
  SUT (Aspire children) makes per-step server telemetry unobtainable any other way. Keep this
  document for two findings that cost real effort: the .NET OTLP exporter speaks only `grpc` and
  `http/protobuf` (so a JSON receiver is not an option), and exporter batching — not correlation — is
  the actual hard problem.
- **Scope:** `src/Freista` (per-step `Activity`, telemetry on `StepResult`), a new OTLP receiver,
  `src/Freista.Mtp` (feed the existing per-step output channel), the HTML report.
- **Out of scope:** being a real collector (batching, retry, sampling, tail sampling, multiple
  receivers). Point at a real collector for that.

## Problem

An integration test's report shows what the *test process* did. It shows nothing about what the
system under test did during a step. That is the single largest gap in an integration-test report:
step 3 failed, and the reason is in the server's logs, which live somewhere else entirely.

## Intent

Correlate the system under test's OpenTelemetry spans and log records back to the **step** that
provoked them, and surface them where the tooling already looks — MTP's per-step output — as well as
in the self-contained HTML report.

The framing matters: Freista does not define a logging API. Any SUT that can export OTLP
participates, in whatever language or stack. This is also why `ScenarioContext.Log()` should not
grow — long term, OTEL subsumes it.

## Half of this is free

.NET already does the propagation:

- `HttpClient`'s `DiagnosticsHandler` injects a W3C `traceparent` header from `Activity.Current` on
  every outgoing request, enabled by default.
- `Activity.Current` is `AsyncLocal`, so it flows correctly through Freista's **concurrent** steps
  without any of the sharing hazards a per-scenario instance would have had.
- When the SUT handles a request under that trace context, its OTEL log records carry the same
  `TraceId`.

So "put a trace id on every outgoing request" is: start an `Activity` per step. The repo currently
has no `Activity` usage at all, so this is greenfield but small.

## Correlation model

**Each step is its own root trace.** The scheduler starts an `Activity` from an `ActivitySource`
named `Freista` around each step's invocation; `StepResult` records its `TraceId` and `SpanId`.

This gives a clean 1:1 map from `TraceId` to step, which is what makes correlation robust. The
alternative — scenario as root trace, steps as child spans — forces walking a parent-span hierarchy
to attribute an arriving span to a step, and any gap in propagation breaks the walk.

The cost is that no single trace spans the scenario. That is bought back with attributes rather than
hierarchy: every step Activity carries `freista.scenario.id`, `freista.scenario.name`,
`freista.step.id`, and `freista.step.name`, so a tracing UI can group a scenario's steps by
attribute.

Arriving telemetry is attributed by `TraceId`:

- **Match** → attach to that step.
- **No match** (no propagation, background work, startup logs) → a scenario-level "unattributed"
  bucket, kept rather than dropped. Unattributed volume is itself a signal that propagation is
  broken somewhere.

## Receiving

**OTLP over HTTP with protobuf encoding**, hosted in the test process.

This is constrained rather than chosen: the .NET OTLP exporter speaks `grpc` and `http/protobuf`
only — not `http/json` — so a JSON receiver, though far simpler, would not accept telemetry from a
.NET SUT. HTTP is preferred over gRPC to avoid an ASP.NET Core hosting dependency in a test
framework; a minimal `HttpListener` plus the generated `opentelemetry-proto` types is enough for a
receiver that only has to deserialize and dispatch.

- **Off by default.** Enabled by an MTP command-line option (`--freista-otlp`, optional port;
  ephemeral port when unspecified), matching how the HTML report is already opted into.
- The chosen endpoint is exposed to the SUT the standard way, via `OTEL_EXPORTER_OTLP_ENDPOINT` and
  `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`.
- Aspire needs no special handling: its AppHost already sets `OTEL_EXPORTER_OTLP_ENDPOINT` on every
  child it launches, so this is a matter of pointing children at Freista's endpoint instead of (or
  as well as) the dashboard.

## Surfacing

**The channel already exists.** `MtpReportSink` already builds a per-step string of logs and effects
and publishes it as `StandardOutputProperty` ([MtpReportSink.cs:188](../../../src/Freista.Mtp/MtpReportSink.cs)).
Correlated telemetry becomes another section of that same string — so VS Test Explorer, TRX, and CI
dashboards get it with no new plumbing.

Three surfaces, in increasing completeness:

1. **MTP per-step standard output** — the correlated spans and log records for that step, formatted
   compactly. This is the one that reaches all the tooling.
2. **A file artifact** (`FileArtifactProperty`, already used for attachments) carrying the raw
   correlated telemetry for a step when it exceeds a size threshold, so a chatty step does not bloat
   the run's console output.
3. **The HTML report** — the complete record including the unattributed bucket, rendered under each
   step alongside its existing logs and effects.

## The late-arrival problem

**This is the decision that needs confirming before planning.**

OTEL exporters batch. The .NET `BatchLogRecordProcessor` defaults to a 5-second schedule delay. A
step runs in 40ms. Its telemetry therefore arrives *seconds after* the step's `TestNode` was already
published with a terminal state — and MTP nodes are published once and cannot be amended. Naively
implemented, per-step output would be empty and the feature would silently do nothing.

Three ways out:

| Option | Effect | Cost |
|---|---|---|
| **A. Hold terminal publication until the scenario drains** (recommended) | Steps stay `InProgress` in the runner; after the DAG completes, wait a bounded drain window, correlate, then publish every step node with complete output. | No live per-step progress within a scenario. Total run time barely changes — one drain per scenario, not per step. |
| **B. Per-step drain window** | Each step waits ~250ms before publishing. | Live progress preserved; adds latency per step (24 steps ≈ 6s). Still not a correctness guarantee. |
| **C. Publish promptly, best-effort output** | Whatever arrived in time appears; the complete record goes to the HTML report and a file artifact only. | Never blocks, but the stated goal — telemetry in the tooling — is met only by luck. |

**Recommendation: A**, with the drain window configurable and the SUT nudged toward fast export via
`OTEL_BSP_SCHEDULE_DELAY` on children. Integration-test scenarios take seconds anyway, so losing
intra-scenario live progress is a smaller loss than telemetry that is usually absent. Option C is the
fallback if holding publication turns out to upset a runner.

This mirrors the `NotTaken` spike from the conditionals work: the honest move is to verify MTP's
tolerance against `samples/AppointmentTests` during implementation rather than assume it.

## Sequencing

This design assumes the Aspire sample exists, or lands alongside it. Two reasons: there is nothing to
receive telemetry *from* until a real SUT runs as a child process, and Aspire already configures OTLP
export on its children, so it is the cheapest possible first consumer. Building the receiver against
a hand-rolled fake would be designing against speculation.

The per-step `Activity` half has no such dependency and is independently useful — even with no
receiver, recording each step's `TraceId` in the report lets a person pivot to whatever tracing UI
they already run (the Aspire dashboard, Jaeger, Grafana), filtered to that step. **It is worth
shipping on its own first.**

## Testing

| Project | Coverage |
|---|---|
| `Freista.Test` | An `Activity` is current inside a step's invoke; concurrent steps get distinct `TraceId`s and do not observe each other's; `StepResult` carries the ids; scenario/step attributes are stamped. |
| New receiver tests | OTLP protobuf payloads deserialize; spans and log records attribute to the right step by `TraceId`; unmatched telemetry lands in the unattributed bucket; a malformed payload is rejected without killing the run. |
| `Freista.Mtp.Test` | Correlated telemetry reaches `StandardOutputProperty`; oversized telemetry becomes a file artifact instead; the drain window resolves before publication (per the decision above). |
| `samples/` | End-to-end against a real child process exporting OTLP. |

## Non-goals

- **Being a collector.** No batching, retry, sampling, or persistence. Freista receives, correlates,
  renders, and forgets.
- **Metrics.** Spans and log records only. Per-step metric correlation has no obvious consumer in a
  test report.
- **Defining a logging API.** The point is that Freista does not have one.
