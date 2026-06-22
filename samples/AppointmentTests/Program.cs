using Freista.Mtp;

namespace AppointmentTests;

/// <summary>
/// Hand-authored Microsoft.Testing.Platform entry point for the showcase. The generated entry point
/// is suppressed (<c>&lt;FreistaGenerateProgram&gt;false&lt;/FreistaGenerateProgram&gt;</c>) so this
/// <c>Main</c> can opt into Freista's deterministic simulated-time scheduler via
/// <c>simulateTime: true</c>. The Given/When/Then steps author their own durations with
/// <see cref="Freista.ScenarioContext.SimulateElapsed(System.TimeSpan)"/> (no real waiting), which
/// lands a realistic, overlapping timeline in the generated HTML report. Production projects omit
/// the flag
/// (default <see langword="false"/>) and run on real wall-clock timing.
/// </summary>
internal static class Program
{
    private static Task<int> Main(string[] args)
        => FreistaTestApplication.RunAsync(args, simulateTime: true);
}
