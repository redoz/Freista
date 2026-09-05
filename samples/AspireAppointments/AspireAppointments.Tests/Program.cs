using Aspire.Hosting;
using Aspire.Hosting.Testing;
using AspireAppointments.Tests;
using Freista;
using Microsoft.Extensions.DependencyInjection;

// The entry point is real, readable code — no generated Main, nothing hidden. This is where the
// AppHost is described, where you say what must be healthy before scenarios run, and where the
// suite's own services are registered.

return await FreistaAspire.RunAsync<Projects.AspireAppointments_AppHost>(args, aspire =>
{
    // Declarative: scenarios do not start until these report healthy. Starting and waiting happen
    // inside the run as the "Preflight" node, so both are timed and reported.
    aspire.WaitFor("api");
    aspire.StartupTimeout = TimeSpan.FromMinutes(2);

    aspire.Services(services =>
    {
        // IHttpClientFactory pools AND rotates the underlying handler, so each CreateClient is a
        // cheap, isolated wrapper over a shared connection pool. That is what makes a client per
        // identity — or per call — free, instead of something to cache and worry about.
        services.AddHttpClient("api", static (IServiceProvider sp, HttpClient client) =>
            client.BaseAddress = sp.GetRequiredService<DistributedApplication>().GetEndpoint("api"));

        services.AddSingleton<AppointmentsApi>();
    });
});
