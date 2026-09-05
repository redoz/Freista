using System.ComponentModel;
using Raun;

namespace AspireAppointments.Tests;

/// <summary>
/// Scenarios driving a real Aspire application. The AppHost is started once for the run, as the
/// preflight node — so its startup is timed and reported, and a failure to come up is a failing test
/// rather than a process that exits before anything reports.
/// </summary>
[DisplayName("Appointments API")]
public static class Scenarios
{
    // Two actors in one scenario: booked as an admin, read back as a patient. The actor belongs to
    // the call, which is why nothing here mutates headers on a shared client.
    [Scenario("an admin books an appointment a patient can read")]
    public static async Task AdminBooksPatientReads()
    {
        await Given.ApiIsReachable();

        var appointment = await When.AdminBooks("Alice", "2026-09-05T09:00");

        await Then.PatientCanRead(appointment);
    }

    [Scenario("a patient may not book appointments")]
    public static async Task PatientCannotBook()
    {
        await Given.ApiIsReachable();

        var status = await When.PatientTriesToBook("Bob", "2026-09-05T10:00");

        await Then.AttemptWasRejected(status);
    }
}
