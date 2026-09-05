using System.Globalization;
using Freista;
using Xunit;

namespace AppointmentTests;

// Domain entities flow between steps as ordinary types — each `await` unwraps Task<T> into T. The
// shared identities (Patient/Slot/Appointment/User) are CRTP resources so steps can declare what they
// do to them; ImportResult is a pure count DTO, not a shared identity, so it stays a plain record.
// A patient's identity is their name; City is state that steps may edit. Because KeyFor projects the
// stable member, `patient with { City = ... }` is still the SAME resource in the trace.
public sealed record Patient(string Name, string? City = null) : IResource<Patient>
{
    public static ResourceKey KeyFor(Patient instance) => instance.Name;
}

// Same idea for a slot: Id is the identity, Held is state a hold step flips.
public sealed record Slot(int Id, bool Held = false) : IResource<Slot>
{
    public static ResourceKey KeyFor(Slot instance) => instance.Id.ToString(CultureInfo.InvariantCulture);
}

public sealed record Appointment(Patient Patient, Slot Slot) : IResource<Appointment>
{
    public static ResourceKey KeyFor(Appointment instance) => $"{instance.Patient.Name}@{instance.Slot.Id}";
}

public sealed record User(string Name) : IResource<User>
{
    public static ResourceKey KeyFor(User instance) => instance.Name;
}

public sealed record ImportResult(int Count);

/// <summary>
/// The application's domain DSL. Steps are C# 14 static extension members on the Given/When/Then
/// phase markers. They are real implementations returning ordinary <c>Task&lt;T&gt;</c> domain
/// results — no <c>Step&lt;T&gt;</c> handles, no stubs. <c>[StepName]</c> sets how the step reads in
/// the test runner; <c>{placeholder}</c>s bind to parameters.
/// </summary>
public static class AppointmentDsl
{
    // Each step authors how long it "took" via ctx.SimulateElapsed — under the showcase's simulated-time
    // scheduler (Program.cs opts in with simulateTime: true) this advances the step's own clock with no
    // real waiting, so the HTML report's Gantt shows realistic, overlapping bars. On a real run the same
    // calls are inert no-ops. The trailing ScenarioContext is an OPTIONAL parameter (= null): scenario
    // bodies call these steps without it (the compiler type-checks that source), and the generator
    // injects the real per-step context at the actual call site — hence the null-conditional `ctx?.`.
    // The durations below are deliberately VARIED and easy to tweak; parallel siblings (independent
    // arrange steps, bulk imports) overlap, and the booking scenarios run past 1s so the report's
    // auto-scaling time ruler switches from ms to s units.
    extension(Given)
    {
        [StepName("Given the database is clean")]
        public static Task DatabaseIsClean(ScenarioContext? ctx = null)
        {
            ctx?.SimulateElapsed(TimeSpan.FromMilliseconds(250));
            return Task.CompletedTask;
        }

        [StepName("Given patient {name} exists")]
        [return: Created]
        public static Task<Patient> PatientExists(string name, ScenarioContext? ctx = null)
        {
            ctx?.SimulateElapsed(TimeSpan.FromMilliseconds(320));

            // Registered where the value is in scope: a real suite would delete the row here, on the
            // connection this step used. The cleanup runs inside the scenario's Teardown step, long
            // after this step has been reported, so it reports through the teardown context it is
            // handed — not through this step's `ctx`.
            ctx?.OnTeardown(teardown =>
            {
                teardown.Log($"cleaned up patient {name}");
                return Task.CompletedTask;
            });

            return Task.FromResult(new Patient(name));
        }

        [StepName("Given an available slot exists")]
        [return: Created]
        public static Task<Slot> AvailableSlot(ScenarioContext? ctx = null)
        {
            ctx?.SimulateElapsed(TimeSpan.FromMilliseconds(460));
            return Task.FromResult(new Slot(1));
        }

        [StepName("Given the patient is a priority case")]
        public static Task<bool> PatientIsPriority([Read] Patient patient, ScenarioContext? ctx = null)
        {
            ctx?.SimulateElapsed(TimeSpan.FromMilliseconds(180));
            // A deterministic demo rule: names starting with a letter before 'M' are priority.
            return Task.FromResult(patient.Name.Length > 0 && char.ToUpperInvariant(patient.Name[0]) < 'M');
        }

        [StepName("Given user {name} exists")]
        [return: Created]
        public static Task<User> UserExists(string name, ScenarioContext? ctx = null)
        {
            // A small, deterministic per-name jitter so the bulk-import lanes show different bar
            // lengths instead of identical clones (still no real waiting).
            var ms = 140 + (name.Sum(c => (int)c) % 5) * 45;
            ctx?.SimulateElapsed(TimeSpan.FromMilliseconds(ms));
            return Task.FromResult(new User(name));
        }
    }

    extension(When)
    {
        [StepName("When creating an appointment")]
        [return: Created(References = [nameof(patient)], Consumes = [nameof(slot)])]
        public static Task<Appointment> CreateAppointment(Patient patient, Slot slot, ScenarioContext? ctx = null)
        {
            ctx?.SimulateElapsed(TimeSpan.FromMilliseconds(600));
            return Task.FromResult(new Appointment(patient, slot));
        }

        [StepName("When creating an urgent appointment")]
        [return: Created(References = [nameof(patient)], Consumes = [nameof(slot)])]
        public static Task<Appointment> CreateUrgentAppointment(Patient patient, Slot slot, ScenarioContext? ctx = null)
        {
            ctx?.SimulateElapsed(TimeSpan.FromMilliseconds(410));
            return Task.FromResult(new Appointment(patient, slot));
        }

        [StepName("When importing the users")]
        public static Task<ImportResult> ImportUsers(User[] users, ScenarioContext? ctx = null)
        {
            ctx?.SimulateElapsed(TimeSpan.FromMilliseconds(300));
            return Task.FromResult(new ImportResult(users.Length));
        }
    }

    extension(Then)
    {
        [StepName("Then the appointment should exist")]
        public static Task AppointmentExists([Read] Appointment appointment, ScenarioContext? ctx = null)
        {
            ctx?.SimulateElapsed(TimeSpan.FromMilliseconds(170));
            Assert.NotNull(appointment.Patient);
            Assert.NotNull(appointment.Slot);
            return Task.CompletedTask;
        }

        [StepName("Then the import should contain {expected} users")]
        public static Task ImportShouldContainUsers(ImportResult import, int expected, ScenarioContext? ctx = null)
        {
            ctx?.SimulateElapsed(TimeSpan.FromMilliseconds(110));
            Assert.Equal(expected, import.Count);
            return Task.CompletedTask;
        }
    }
}
