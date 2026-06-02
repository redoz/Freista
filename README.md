# PUnit

> Scenario tests for xUnit v3 — Given/When/Then steps, reported as individual tests, wired into a fork/join dependency graph.

PUnit lets you write integration-style scenario tests as readable `Given` / `When` / `Then` C# and have each business step show up as its own xUnit test: sequential by default, explicitly parallel where you ask for it, with typed state flowing between steps and dependent steps auto-skipped after a failure.

```csharp
using PUnit;

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

## Status

Design phase. See [the design spec](docs/scenario-graph-extension-design.md).
```
