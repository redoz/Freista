// The smallest AppHost that still exercises the integration: one project resource, no containers.
// A container resource (Postgres, say) would be more realistic but would make the sample require a
// container runtime to run at all — the Freista wiring is identical either way.

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.AspireAppointments_Api>("api")
    // Declared here rather than in a launchSettings.json profile: one fewer file, and the sample's
    // topology is visible in one place. Aspire injects ASPNETCORE_URLS for the allocated port.
    .WithHttpEndpoint()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
