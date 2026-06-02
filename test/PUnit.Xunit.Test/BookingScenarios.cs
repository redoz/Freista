using PUnit;

namespace PUnit.Xunit.Test;

/// <summary>
/// Real scenarios executed by the xUnit v3 runner. Each step shows up as its own test; these all
/// pass, so a green run proves discovery + self-execution + per-step reporting + parallel join work
/// end-to-end. Fail/skip reporting is covered deterministically by <c>ScenarioReporterTests</c>.
/// </summary>
public static class BookingScenarios
{
    [Scenario("customer books an appointment")]
    public static async Task Booking()
    {
        var patient = await Given.PatientExists("Jane");
        var slot = await Given.AvailableSlot();

        var appointment = await When.CreateAppointment(patient, slot);

        await Then.AppointmentExists(appointment);
    }

    [Scenario("customer books with parallel arrange")]
    public static async Task BookingWithParallelArrange()
    {
        await Given.DatabaseIsClean();

        var (patient, slot) = await (
            Given.PatientExists("Jane"),
            Given.AvailableSlot());

        var appointment = await When.CreateAppointment(patient, slot);

        await Then.AppointmentExists(appointment);
    }

    [Scenario("bulk user import")]
    public static async Task ImportUsers()
    {
        var users = await new[]
        {
            Given.UserExists("alice"),
            Given.UserExists("bob"),
        };

        var import = await When.ImportUsers(users);

        await Then.ImportShouldContain(import, 2);
    }
}
