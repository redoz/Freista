using System.Globalization;
using PUnit;
using Xunit;

namespace AppointmentTests;

// Domain entities flow between steps as ordinary types — each `await` unwraps Task<T> into T. The
// shared identities (Patient/Slot/Appointment/User) are CRTP resources so steps can declare what they
// do to them; ImportResult is a pure count DTO, not a shared identity, so it stays a plain record.
public sealed record Patient(string Name) : IResource<Patient>
{
    public static ResourceKey KeyFor(Patient instance) => instance.Name;
}

public sealed record Slot(int Id) : IResource<Slot>
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
    extension(Given)
    {
        [StepName("Given the database is clean")]
        public static Task DatabaseIsClean() => Task.CompletedTask;

        [StepName("Given patient {name} exists")]
        [return: Creates]
        public static async Task<Patient> PatientExists(string name)
        {
            await Task.Yield();
            return new Patient(name);
        }

        [StepName("Given an available slot exists")]
        [return: Creates]
        public static async Task<Slot> AvailableSlot()
        {
            await Task.Yield();
            return new Slot(1);
        }

        [StepName("Given user {name} exists")]
        [return: Creates]
        public static async Task<User> UserExists(string name)
        {
            await Task.Yield();
            return new User(name);
        }
    }

    extension(When)
    {
        [StepName("When creating an appointment")]
        [return: Creates]
        public static async Task<Appointment> CreateAppointment([Reads] Patient patient, [Reads] Slot slot)
        {
            await Task.Yield();
            return new Appointment(patient, slot);
        }

        [StepName("When importing the users")]
        public static async Task<ImportResult> ImportUsers(User[] users)
        {
            await Task.Yield();
            return new ImportResult(users.Length);
        }
    }

    extension(Then)
    {
        [StepName("Then the appointment should exist")]
        public static Task AppointmentExists([Reads] Appointment appointment)
        {
            Assert.NotNull(appointment.Patient);
            Assert.NotNull(appointment.Slot);
            return Task.CompletedTask;
        }

        [StepName("Then the import should contain {expected} users")]
        public static Task ImportShouldContainUsers(ImportResult import, int expected)
        {
            Assert.Equal(expected, import.Count);
            return Task.CompletedTask;
        }
    }
}
