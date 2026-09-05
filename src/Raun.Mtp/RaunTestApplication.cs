using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Capabilities.TestFramework;

namespace Raun.Mtp;

/// <summary>
/// The public bootstrap for running a Raun test project under Microsoft.Testing.Platform.
/// </summary>
/// <remarks>
/// <para>
/// This is the escape-hatch API. By default the Raun source generator emits a <c>Program.cs</c>
/// whose <c>Main</c> calls <see cref="RunAsync(string[], Action{ITestApplicationBuilder}?, bool, IServiceProvider?, Func{ScenarioContext,Task}?)"/>, giving
/// "just add the package" UX. Setting the MSBuild property <c>&lt;RaunGenerateProgram&gt;false&lt;/RaunGenerateProgram&gt;</c>
/// suppresses that emission so a consumer can write their own <c>Program.cs</c> and call this method
/// directly, taking full control of the host (custom MTP extensions, builder configuration, etc.)
/// via the optional <c>configure</c> callback.
/// </para>
/// </remarks>
public static class RaunTestApplication
{
    /// <summary>
    /// Builds the Microsoft.Testing.Platform host, registers Raun's <see cref="RaunTestFramework"/>,
    /// and runs it.
    /// </summary>
    /// <param name="args">The command-line arguments passed to the test executable.</param>
    /// <param name="configure">
    /// Optional callback to configure the <see cref="ITestApplicationBuilder"/> before the framework
    /// is registered (e.g. to add custom extensions). May be <see langword="null"/>.
    /// </param>
    /// <param name="simulateTime">
    /// Sample-local opt-in: when <see langword="true"/>, the registered <see cref="RaunTestFramework"/>
    /// runs scenarios on a deterministic simulated timeline (durations driven from step bodies via
    /// <c>ScenarioContext.SimulateElapsed</c>, no real waiting). Defaults to <see langword="false"/>, so
    /// the source-generated <c>Program</c> (which calls the 2-arg form) leaves production runs on real
    /// timing.
    /// </param>
    /// <param name="services">
    /// The consumer's own service provider, surfaced to step bodies as <c>ctx.Services</c> (scoped
    /// per scenario when it can supply an <c>IServiceScopeFactory</c>). Built and disposed by the
    /// consumer in their own <c>Main</c>, which is what lets registrations do async setup — an Aspire
    /// AppHost, for instance. <see langword="null"/> leaves <c>ctx.Services</c> null.
    /// </param>
    /// <param name="preflight">
    /// Run-level setup executed once before any scenario and reported as its own <c>Preflight</c>
    /// node, so a failure is a failing test rather than a process that exits before anything reports.
    /// When it fails, every scenario's steps report skipped naming preflight.
    /// </param>
    /// <returns>The process exit code to return from <c>Main</c>.</returns>
    public static async Task<int> RunAsync(
        string[] args,
        Action<ITestApplicationBuilder>? configure = null,
        bool simulateTime = false,
        IServiceProvider? services = null,
        Func<ScenarioContext, Task>? preflight = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = await TestApplication.CreateBuilderAsync(args).ConfigureAwait(false);

        configure?.Invoke(builder);

        builder.CommandLine.AddProvider(() => new HtmlReport.HtmlReportOptionsProvider());

        builder.RegisterTestFramework(
            _ => new TestFrameworkCapabilities(),
            (_, serviceProvider) => new RaunTestFramework(serviceProvider, simulateTime, services, preflight));

        using var app = await builder.BuildAsync().ConfigureAwait(false);
        return await app.RunAsync().ConfigureAwait(false);
    }
}
