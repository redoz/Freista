namespace PUnit.Generator.Test;

/// <summary>Reusable input source for generator tests: a small AppointmentDsl with real impls.</summary>
public static class SampleSources
{
    public const string Dsl =
        """
        using System.Linq;
        using System.Threading.Tasks;
        using PUnit;

        namespace Demo;

        public sealed record Patient(string Name);
        public sealed record Slot(int Id);
        public sealed record Appointment(Patient Patient, Slot Slot);
        public sealed record User(string Name);
        public sealed record Import(int Count);

        public static class AppointmentDsl
        {
            extension(Given)
            {
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

                [StepName("database is clean")]
                public static Task DatabaseIsClean() => Task.CompletedTask;

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

                [StepName("importing users")]
                public static async Task<Import> ImportUsers(User[] users)
                {
                    await Task.Yield();
                    return new Import(users.Length);
                }
            }

            extension(Then)
            {
                [StepName("the appointment should exist")]
                public static Task AppointmentExists(Appointment appointment) => Task.CompletedTask;

                [StepName("the import should contain the users")]
                public static Task ImportShouldContainUsers(Import import, User[] users) => Task.CompletedTask;
            }
        }
        """;

    // Scenario snippets are appended to Dsl, continuing its file-scoped `namespace Demo;`.
    public const string LinearScenario =
        """

        public static class BookingScenarios
        {
            [Scenario("booking")]
            public static async Task Booking()
            {
                var patient = await Given.PatientExists("Jane");
                var slot = await Given.AvailableSlot();
                var appointment = await When.CreateAppointment(patient, slot);
                await Then.AppointmentExists(appointment);
            }
        }
        """;
}
