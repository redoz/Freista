# Freista

> Scenario / integration tests for .NET — Given/When/Then steps, each reported as its own test, wired into a fork/join dependency graph.

Freista lets you write integration-style scenario tests as readable `Given` / `When` / `Then` C# and have each business step show up as its own test: sequential by default, explicitly parallel where you ask for it, with typed state flowing between steps and dependent steps auto-skipped after a failure.

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

A Roslyn source generator lowers the scenario method into a manifest + executor that the runtime runs as a step graph on Microsoft.Testing.Platform.

## How it works

1. You define a domain DSL as C# 14 static extension members on `Given` / `When` / `Then`,
   each annotated with `[StepName("...")]`. These are real methods returning ordinary
   `Task<T>` — each `await` in the scenario unwraps to `T`.
2. You write `[Scenario]` methods using that DSL. The body is **source for the generator** —
   MTP never executes it directly.
3. The generator lowers each body into a dependency graph (`ScenarioDefinition`): one node per
   step, with **source-order + dataflow** edges, and tuple/array forms lowered to parallel
   sibling groups.
4. At run time, the MTP test framework discovers each `[Scenario]`, runs the graph through a DAG
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
| `src/Freista.Aspire` | Aspire integration: builds the AppHost, starts it as the run's preflight while waiting for the resources you declare, registers it for your steps. Plumbing only — no phase markers, no steps. |
| `samples/AppointmentTests` | End-to-end sample (linear / tuple / array / LINQ / conditionals / teardown). |
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
- `if`/`else` shapes the graph when the condition is an awaited `Given`/`When`/`Then` call whose
  result is usable as a C# condition (`bool`, an implicit conversion to `bool`, or `operator true`).
  The condition is an ordinary step — discovered, timed, and reported like any other. Exactly one arm
  runs; steps in the other are reported **not taken**, never green. A local assigned in both arms is
  merged automatically.
- Loops (`for`/`foreach`/`while`/`do`), `switch`, `try`/`catch`, and `goto` are rejected with a
  diagnostic: put the loop, retry, or polling **inside a step**. A step is an ordinary
  `async Task<T>` method, so it can loop, retry, or poll internally; what a step cannot do is stop a
  later step from running, which is why `if`/`else` is the one construct the graph models.

### Conditionals

```csharp
var patient = await Given.PatientExists("Alice");
var slot = await Given.AvailableSlot();

Appointment appointment;
if (await Given.PatientIsPriority(patient))
    appointment = await When.CreateUrgentAppointment(patient, slot);
else
    appointment = await When.CreateAppointment(patient, slot);

await Then.AppointmentExists(appointment);
```

### Logging

Steps write through the standard `ILogger` abstraction; the lines are collected as that step's log
output and appear under it in the runner and the HTML report.

```csharp
ctx.GetLogger<BookingSteps>().LogInformation("seeded {Count} patients", count);
```

Registering `FreistaLoggerProvider` with an in-process system under test attributes *its* logs to the
step that provoked them, because the destination is resolved per write from the step that is running:

```csharp
builder.Logging.AddProvider(new FreistaLoggerProvider());
```

### Teardown

Cleanup is registered by the step that created the thing, so the closure captures both the object and
the connection it needs — nothing to derive, nothing to wire:

```csharp
[StepName("patient {name} exists")]
[return: Created]
public static async Task<Patient> PatientExists(string name, ScenarioContext? ctx = null)
{
    var patient = await Db.InsertPatient(name);
    ctx?.OnTeardown(() => Db.DeletePatient(patient.Id));
    return patient;
}
```

Every scenario reports a final `Teardown` step, so a cleanup that throws fails visibly instead of
being swallowed. Cleanups run in reverse dependency order, and one that throws does not stop the
rest — every error is collected onto that step. The step's log records what ran and what was skipped,
naming the step each cleanup came from:

```
cleaned up: CreateAppointment
cleaned up: PatientExists
skipped 2 optional cleanup(s) — teardown policy is OnSuccess and the scenario failed: AvailableSlot, DatabaseIsClean
```

`[Teardown(Run.OnSuccess)]` on the scenario leaves state intact when the test failed; `Run.Never`
disables cleanup entirely while you go and poke at it. A registration marked `Cleanup.Required`
ignores that policy and runs regardless — including after cancellation or a timeout — for things
whose absence is a leak rather than a choice:

```csharp
ctx?.OnTeardown(Cleanup.Required, () => container.StopAsync());
```

See [the design spec](docs/scenario-graph-extension-design.md), the
[conditionals design](docs/superpowers/specs/2026-09-03-scenario-conditionals-design.md), the
[teardown design](docs/superpowers/specs/2026-09-04-scenario-teardown-design.md), and the
[implementation plan](docs/superpowers/plans/2026-06-03-scenario-graph-extension.md).
