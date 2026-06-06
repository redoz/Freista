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

                [StepName("greet {patient}")]
                public static Task Greet(Patient patient) => Task.CompletedTask;
            }
        }
        """;

    // Named arguments: the label (`patient:`) must be left alone; only the value is rewritten.
    public const string NamedArgScenario =
        """

        public static class NamedArgScenarios
        {
            [Scenario("named args")]
            public static async Task Booking()
            {
                var patient = await Given.PatientExists(name: "Jane");
                var slot = await Given.AvailableSlot();
                var appointment = await When.CreateAppointment(patient: patient, slot: slot);
                await Then.AppointmentExists(appointment: appointment);
            }
        }
        """;

    public const string RuntimeNameScenario =
        """

        public static class GreetScenarios
        {
            [Scenario("greeting")]
            public static async Task Greeting()
            {
                var patient = await Given.PatientExists("Jane");
                await Then.Greet(patient);
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

    public const string TupleScenario =
        """

        public static class TupleScenarios
        {
            [Scenario("tuple booking")]
            public static async Task Booking()
            {
                await Given.DatabaseIsClean();

                var (patient, slot) = await (
                    Given.PatientExists("Jane"),
                    Given.AvailableSlot());

                var appointment = await When.CreateAppointment(patient, slot);
                await Then.AppointmentExists(appointment);
            }
        }
        """;

    public const string ArrayScenario =
        """

        public static class ArrayScenarios
        {
            [Scenario("array import")]
            public static async Task Import()
            {
                var users = await new[]
                {
                    Given.UserExists("alice"),
                    Given.UserExists("bob"),
                };

                var import = await When.ImportUsers(users);
                await Then.ImportShouldContainUsers(import, users);
            }
        }
        """;

    public const string LinqScenario =
        """

        public static class LinqScenarios
        {
            [Scenario("linq import")]
            public static async Task Import()
            {
                var users = await Enumerable.Range(1, 3)
                    .Select(i => Given.UserExists($"user-{i}"))
                    .ToArray();

                var import = await When.ImportUsers(users);
                await Then.ImportShouldContainUsers(import, users);
            }
        }
        """;

    // A resource-aware DSL: User/Slot/Appointment are CRTP IResource<> records with KeyFor, and the
    // steps carry role attributes ([Creates]/[Edits]/[Reads] on the return value or parameters) the
    // generator lowers into ctx.Resources.* calls.
    public const string ResourceDsl =
        """
        using System.Threading.Tasks;
        using PUnit;

        namespace ResourceDemo;

        public sealed record User(string Email) : IResource<User>
        {
            public static ResourceKey KeyFor(User instance) => instance.Email;
        }

        public sealed record Slot(int Id) : IResource<Slot>
        {
            public static ResourceKey KeyFor(Slot instance) => instance.Id.ToString();
        }

        public sealed record Appointment(User User, Slot Slot) : IResource<Appointment>
        {
            public static ResourceKey KeyFor(Appointment instance) => instance.User.Email + "@" + instance.Slot.Id;
        }

        public static class ResourceDsl
        {
            extension(Given)
            {
                [StepName("user {email} exists")]
                [return: Creates]
                public static async Task<User> UserExists(string email)
                {
                    await Task.Yield();
                    return new User(email);
                }

                [StepName("a slot exists")]
                [return: Creates]
                public static async Task<Slot> SlotExists()
                {
                    await Task.Yield();
                    return new Slot(1);
                }
            }

            extension(When)
            {
                [StepName("suspending the user")]
                [return: Edits]
                public static async Task<User> Suspend([Edits] User user)
                {
                    await Task.Yield();
                    return user;
                }

                [StepName("booking a slot")]
                [return: Creates]
                public static async Task<Appointment> Book([Reads] User user, [Edits] Slot slot)
                {
                    await Task.Yield();
                    return new Appointment(user, slot);
                }
            }

            extension(Then)
            {
                [StepName("the user cannot sign in")]
                public static Task CannotSignIn([Reads] User user) => Task.CompletedTask;
            }
        }
        """;

    // Scenario appended to ResourceDsl, continuing its file-scoped `namespace ResourceDemo;`.
    public const string ResourceScenario =
        """

        public static class ResourceScenarios
        {
            [Scenario("suspended user cannot sign in")]
            public static async Task SuspendedUserCannotSignIn()
            {
                var user = await Given.UserExists("jane@acme.com");
                var suspended = await When.Suspend(user);
                await Then.CannotSignIn(suspended);
            }
        }
        """;

    // Scenario appended to ResourceDsl: exercises When.Book's multi-role parameter list
    // ([Reads] User, [Edits] Slot) plus [return: Creates], locking in param-loop ordering
    // and param-before-return emit order.
    public const string BookingScenario =
        """

        public static class BookingResourceScenarios
        {
            [Scenario("booking a slot")]
            public static async Task BookSlot()
            {
                var user = await Given.UserExists("jane@acme.com");
                var slot = await Given.SlotExists();
                var appt = await When.Book(user, slot);
            }
        }
        """;
}
