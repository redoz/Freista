using PUnit.Mtp;

namespace AppointmentTests;

/// <summary>
/// Hand-authored Microsoft.Testing.Platform entry point for the showcase. The generated entry point
/// is suppressed (<c>&lt;PUnitGenerateProgram&gt;false&lt;/PUnitGenerateProgram&gt;</c>) so this
/// <c>Main</c> can opt into PUnit's deterministic simulated-time scheduler via
/// <c>simulateTime: true</c>. The Given/When/Then steps author their own durations with
/// <see cref="PUnit.ScenarioContext.SimulateElapsed(System.TimeSpan)"/> (no real waiting), which
/// lands a realistic, overlapping timeline in the generated HTML report. Production projects omit
/// the flag
/// (default <see langword="false"/>) and run on real wall-clock timing.
/// </summary>
internal static class Program
{
    private static Task<int> Main(string[] args)
        => PUnitTestApplication.RunAsync(args, simulateTime: true);
}
