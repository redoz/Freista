# Freista

> Scenario tests for xUnit v3 — Given/When/Then steps, reported as individual tests, wired into a fork/join dependency graph.

Freista lets you write integration-style scenario tests as readable `Given` / `When` / `Then` C# and have each business step show up as its own xUnit test: sequential by default, explicitly parallel where you ask for it, with typed state flowing between steps and dependent steps auto-skipped after a failure.

```csharp
using Freista;

[Scenario("customer books an appointment")]
public static async Task Booking()
{
    var patient = await Given.PatientExists("Jane");
    var slot = await Given.AvailableSlot();

    var appointment = await When.CreateAppointment(patient, slot);

    await Then.AppointmentExists(appointment);
}
```

A Roslyn source generator lowers the scenario method into a manifest + executor that the runtime runs as a step graph on top of xUnit v3.

> The name? The author's name is Patrik — and a test framework that's a pun felt right. Read it as "pun-it."

## How it works

1. You define a domain DSL as C# 14 static extension members on `Given` / `When` / `Then`,
   each annotated with `[StepName("...")]`. These are real methods returning ordinary
   `Task<T>` — each `await` in the scenario unwraps to `T`.
2. You write `[Scenario]` methods using that DSL. The body is **source for the generator** —
   xUnit never executes it directly.
3. The generator lowers each body into a dependency graph (`ScenarioDefinition`): one node per
   step, with **source-order + dataflow** edges, and tuple/array forms lowered to parallel
   sibling groups.
4. At run time, the xUnit v3 adapter discovers each `[Scenario]`, runs the graph through a DAG
   scheduler, and reports **every step as its own test** — passed, failed, or skipped.

### Parallelism is explicit

```csharp
// sequential by default — runs in written order
await Given.DatabaseIsClean();

// fork/join with an awaited tuple — both run in parallel, the next step waits for both
var (patient, slot) = await (Given.PatientExists("Jane"), Given.AvailableSlot());

// homogeneous bulk work — explicit array or a constant LINQ .ToArray()
var users = await new[] { Given.UserExists("alice"), Given.UserExists("bob") };
var more  = await Enumerable.Range(1, 10).Select(i => Given.UserExists($"u{i}")).ToArray();
```

When a step fails, its transitive dependents are skipped (`dependency failed: <op>`) while
independent ready branches keep running.

## Project layout

| Project | What it is |
| --- | --- |
| `src/Freista` | Runner-neutral core: phase markers, parallel awaiters, `ScenarioContext`, the graph model, and the DAG scheduler. No xUnit dependency. |
| `src/Freista.Generator` | Roslyn incremental generator + analyzer (FRST000–010). netstandard2.0. |
| `src/Freista.Mtp` | Microsoft.Testing.Platform test framework: `[Scenario]`, discovery, run loop, per-step node reporter. |
| `samples/AppointmentTests` | End-to-end sample (linear / tuple / array / LINQ). |
| `test/*` | Scheduler tests (xUnit-free), generator/analyzer tests (behavioral + Verify snapshots), and MTP acceptance tests. |

## Run it

```bash
dotnet test                         # whole solution
dotnet test samples/AppointmentTests # see the steps reported as individual tests
```

## Supported scenario subset (v1)

- `[Scenario]` methods are `async Task` / `async ValueTask`.
- Steps are awaited `Given`/`When`/`Then` calls, awaited tuples of them (arity 2–8), awaited
  `new[] { ... }` arrays, or a constant `Enumerable.Range(a, b).Select(...).ToArray()`.
- DSL methods return `Task`/`Task<T>`/`ValueTask`/`ValueTask<T>` and may take an optional trailing
  `ScenarioContext` parameter.
- Control flow (if/for/while/...) is rejected with a diagnostic; loops, conditionals, and richer
  collection forms are future work.

See [the design spec](docs/scenario-graph-extension-design.md) and the
[implementation plan](docs/superpowers/plans/2026-06-03-scenario-graph-extension.md).
