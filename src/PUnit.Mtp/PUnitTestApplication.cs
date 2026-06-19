using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Capabilities.TestFramework;

namespace PUnit.Mtp;

/// <summary>
/// The public bootstrap for running a PUnit test project under Microsoft.Testing.Platform.
/// </summary>
/// <remarks>
/// <para>
/// This is the escape-hatch API. By default the PUnit source generator emits a <c>Program.cs</c>
/// whose <c>Main</c> calls <see cref="RunAsync(string[], Action{ITestApplicationBuilder}?, bool)"/>, giving
/// "just add the package" UX. Setting the MSBuild property <c>&lt;PUnitGenerateProgram&gt;false&lt;/PUnitGenerateProgram&gt;</c>
/// suppresses that emission so a consumer can write their own <c>Program.cs</c> and call this method
/// directly, taking full control of the host (custom MTP extensions, builder configuration, etc.)
/// via the optional <c>configure</c> callback.
/// </para>
/// </remarks>
public static class PUnitTestApplication
{
    /// <summary>
    /// Builds the Microsoft.Testing.Platform host, registers PUnit's <see cref="PUnitTestFramework"/>,
    /// and runs it.
    /// </summary>
    /// <param name="args">The command-line arguments passed to the test executable.</param>
    /// <param name="configure">
    /// Optional callback to configure the <see cref="ITestApplicationBuilder"/> before the framework
    /// is registered (e.g. to add custom extensions). May be <see langword="null"/>.
    /// </param>
    /// <param name="simulateTime">
    /// Sample-local opt-in: when <see langword="true"/>, the registered <see cref="PUnitTestFramework"/>
    /// runs scenarios on a deterministic simulated timeline (durations driven from step bodies via
    /// <c>ScenarioContext.SimulateElapsed</c>, no real waiting). Defaults to <see langword="false"/>, so
    /// the source-generated <c>Program</c> (which calls the 2-arg form) leaves production runs on real
    /// timing.
    /// </param>
    /// <returns>The process exit code to return from <c>Main</c>.</returns>
    public static async Task<int> RunAsync(
        string[] args,
        Action<ITestApplicationBuilder>? configure = null,
        bool simulateTime = false)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = await TestApplication.CreateBuilderAsync(args).ConfigureAwait(false);

        configure?.Invoke(builder);

        builder.CommandLine.AddProvider(() => new HtmlReport.HtmlReportOptionsProvider());

        builder.RegisterTestFramework(
            _ => new TestFrameworkCapabilities(),
            (_, serviceProvider) => new PUnitTestFramework(serviceProvider, simulateTime));

        using var app = await builder.BuildAsync().ConfigureAwait(false);
        return await app.RunAsync().ConfigureAwait(false);
    }
}
