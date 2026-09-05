using System.Globalization;

namespace Raun.Model;

/// <summary>
/// One line of a step's log and when it was written, as an offset from the start of the scenario —
/// a timer, not a timestamp, because "2.3 seconds in" is what you want to know when reading a run.
/// Under the scheduler's simulated-time mode the offset comes from the step's simulated clock, so
/// the sample report stays deterministic.
/// </summary>
/// <param name="Elapsed">Time since the scenario started when the line was written.</param>
/// <param name="Message">The line.</param>
public readonly record struct LogEntry(TimeSpan Elapsed, string Message)
{
    /// <summary>Renders as <c>+1.234s message</c>.</summary>
    public override string ToString()
        => "+" + Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + "s " + Message;
}
