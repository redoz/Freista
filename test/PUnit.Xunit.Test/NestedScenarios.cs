using PUnit;

namespace PUnit.Xunit.Test;

/// <summary>
/// A scenario hosted in a nested static class. xUnit names the host with a metadata '+' separator
/// while the generator keys on the '.' display form; if those didn't agree the scenario would
/// report a "no generated scenario" failure instead of its steps. A green run proves they match.
/// </summary>
public static class Outer
{
    public static class Inner
    {
        [Scenario("nested host scenario")]
        public static async Task NestedBooking()
        {
            var patient = await Given.PatientExists("Nested");
            var slot = await Given.AvailableSlot();
            var appointment = await When.CreateAppointment(patient, slot);
            await Then.AppointmentExists(appointment);
        }
    }
}
