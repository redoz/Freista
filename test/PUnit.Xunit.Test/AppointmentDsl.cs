using PUnit;
using Xunit;

namespace PUnit.Xunit.Test;

public sealed record Patient(string Name);
public sealed record Slot(int Id);
public sealed record Appointment(Patient Patient, Slot Slot);
public sealed record User(string Name);
public sealed record ImportResult(int Count);

/// <summary>The end-to-end AppointmentDsl from the design, with real (non-stub) implementations.</summary>
public static class AppointmentDsl
{
    extension(Given)
    {
        [StepName("the database is clean")]
        public static Task DatabaseIsClean() => Task.CompletedTask;

        [StepName("patient {name} exists")]
        public static async Task<Patient> PatientExists(string name)
        {
            await Task.Yield();
            return new Patient(name);
        }

        [StepName("an available slot exists")]
        public static async Task<Slot> AvailableSlot()
        {
            await Task.Yield();
            return new Slot(1);
        }

        [StepName("user {name} exists")]
        public static async Task<User> UserExists(string name)
        {
            await Task.Yield();
            return new User(name);
        }
    }

    extension(When)
    {
        [StepName("creating an appointment")]
        public static async Task<Appointment> CreateAppointment(Patient patient, Slot slot)
        {
            await Task.Yield();
            return new Appointment(patient, slot);
        }

        [StepName("importing the users")]
        public static async Task<ImportResult> ImportUsers(User[] users)
        {
            await Task.Yield();
            return new ImportResult(users.Length);
        }
    }

    extension(Then)
    {
        [StepName("the appointment should exist")]
        public static Task AppointmentExists(Appointment appointment)
        {
            Assert.NotNull(appointment.Patient);
            Assert.NotNull(appointment.Slot);
            return Task.CompletedTask;
        }

        [StepName("the import should contain {count} users")]
        public static Task ImportShouldContain(ImportResult import, int count)
        {
            Assert.Equal(count, import.Count);
            return Task.CompletedTask;
        }
    }
}
