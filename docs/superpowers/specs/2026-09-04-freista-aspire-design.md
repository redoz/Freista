# Freista.Aspire — Design

- **Date:** 2026-09-04
- **Status:** Design approved in brainstorming. API surface verified against
  `Aspire.Hosting.Testing` / `Aspire.Hosting` **13.5.3** (restored and inspected, not recalled).
- **Scope:** new `src/Freista.Aspire` package; new `samples/AspireAppointments/` (AppHost, one
  service, one test project).
- **Depends on:** `2026-09-04-preflight-design.md` — startup runs as the preflight node, so that
  ships first.
- **Out of scope:** cross-process code coverage (its own spike, after this), Kiota, phase markers,
  C2.

## Intent

Let a Freista suite drive a real Aspire application, with the app started once per run and the
resources it depends on healthy before any scenario executes.

## Boundary: plumbing only

`Freista.Aspire` ships **no phase markers and no `[StepName]` steps**. The framework's premise is
that the DSL is yours; a package shipping `Given.` steps would contradict it, and — more practically
— it is not yet known which steps an Aspire suite actually wants. Everything the package offers is
reachable from a step you write.

This boundary was tested repeatedly during design and held every time. Successive requirements —
per-resource HTTP clients, connection strings, per-scenario auth, multiple identities per scenario,
Kiota clients — each turned out to be served by standard .NET DI in the consumer's own `Program.cs`,
using the service registration that landed in `26f3c16e`. Nothing needed a Freista type.

Should a pattern later prove universal, it can graduate into the package on evidence. Shipping it now
would be guessing.

## Surface

### The bootstrap

```csharp
// Program.cs  —  <FreistaGenerateProgram>false</FreistaGenerateProgram>
return await FreistaAspire.RunAsync<Projects.AspireAppointments_AppHost>(args, aspire =>
{
    aspire.WaitFor("postgres", "api");
    aspire.StartupTimeout = TimeSpan.FromMinutes(2);

    aspire.Services(services =>
    {
        services.AddHttpClient("api", (sp, c) =>
            c.BaseAddress = sp.GetRequiredService<DistributedApplication>().GetEndpoint("api"));
        services.AddSingleton<IApiClientFactory, ApiClientFactory>();
    });
});
```

It performs, in order:

1. `DistributedApplicationTestingBuilder.CreateAsync<TAppHost>(ct)` → `IDistributedApplicationTestingBuilder`
2. the optional `ConfigureBuilder` callback (so a consumer can alter the app model for tests)
3. `builder.BuildAsync()` → `DistributedApplication` — **built but not started**
4. builds an `IServiceProvider` containing the `DistributedApplication` as a **singleton** plus the
   consumer's own registrations
5. `FreistaTestApplication.RunAsync(args, services: provider, preflight: …)`, where the preflight
   delegate does the actual **starting and waiting** — see below
6. disposes the provider, then `await app.DisposeAsync()`, in a `finally`

### Startup runs as preflight

Starting the app and waiting for resources happens **inside** the MTP session, as the preflight
delegate (`2026-09-04-preflight-design.md`):

```csharp
preflight: async ctx =>
{
    ctx.Log("starting AppHost");
    await app.StartAsync(ctx.CancellationToken);

    foreach (var resource in options.WaitForResources)
    {
        var started = ctx.TimeProvider.GetTimestamp();
        await app.ResourceNotifications.WaitForResourceHealthyAsync(resource, timeoutToken);
        ctx.Log($"{resource} → Healthy ({Elapsed(started)})");
    }
}
```

all under a single `StartupTimeout`, applied by a linked `CancellationTokenSource`.

This is what makes startup **visible**: it becomes a discovered, timed `Preflight` node carrying the
startup log, and a failed start is a *failing test* rather than a process that exits before anything
reports.

```
Preflight                        PASS  6.8s
  ├ starting AppHost
  ├ postgres → Healthy (4.1s)
  └ api → Healthy (2.4s)

1. Given patient Alice exists    PASS   8ms
```

### Options

```csharp
public sealed class AspireRunOptions
{
    public void WaitFor(params string[] resourceNames);
    public TimeSpan StartupTimeout { get; set; }              // default: 5 minutes
    public void Services(Action<IServiceCollection> configure);
    public void ConfigureBuilder(Action<IDistributedApplicationTestingBuilder> configure);
    public void ConfigureTestApplication(Action<ITestApplicationBuilder> configure);
}
```

`WaitFor` and `Services` are additive across calls, so composition helpers can each contribute.

### The one extension method

```csharp
public static DistributedApplication Aspire(this ScenarioContext ctx);
```

Sugar over `ctx.Services!.GetRequiredService<DistributedApplication>()`, with an actionable message
when the run was not started through `FreistaAspire.RunAsync`. It returns the app itself rather than
a facade: Aspire already provides `CreateHttpClient(resource, endpoint?)`,
`GetEndpoint(resource, endpoint?)` and `GetConnectionStringAsync(resource, ct)` as extensions on
`DistributedApplication`, and a wrapper that forwarded to them would add a type to learn and nothing
else.

## Startup failure

Because startup runs as preflight, a failure is a **failing `Preflight` node** and every scenario
step reports `Skipped` with `preflight failed`. The run still completes and reports, so CI shows the
thing that actually broke rather than an opaque non-zero exit.

The timeout message still has to be self-sufficient, because it is the primary diagnostic. When
`StartupTimeout` elapses, the exception names: which resources were awaited, which reached healthy,
which did not, each unhealthy resource's last known state via
`ResourceNotifications.TryGetCurrentState(name, out _)`, and the timeout that was applied.

An earlier draft of this design started the app *before* the MTP session, which made startup
invisible and its failures unreportable — that was recorded as an accepted cost of plumbing-only. It
is not a necessary cost: preflight buys the visibility back without shipping a single phase marker,
so the DSL stays entirely the consumer's **and** the report is complete.

## The sample

`samples/AspireAppointments/`:

| Project | Contents |
|---|---|
| `AspireAppointments.AppHost` | Aspire AppHost: one API service, one Postgres resource |
| `AspireAppointments.Api` | A minimal API with an appointments endpoint and role-based authorization |
| `AspireAppointments.Tests` | The Freista suite: its own DSL, an explicit `Program.cs`, scenarios |

It demonstrates, deliberately:

- **An explicit `Program.cs`** — the first project in the repo with `FreistaGenerateProgram=false`,
  and the shape a `dotnet new freista` template should later scaffold.
- **`IHttpClientFactory`, not hand-rolled handler pooling.** The factory pools *and rotates* handlers;
  `CreateClient` returns a cheap client over a shared handler, so header isolation is free. It is also
  the seam Aspire service discovery plugs into.
- **Two identities in one scenario.** A scenario acts as an admin *and* as a patient, because
  identity is a property of the call, not of the scenario or even of the step. A single-identity
  sample would teach the wrong reflex — auth must not be mutated on a shared client, which races
  against Freista's own intra-scenario step parallelism.
- **A hand-written typed client**, not Kiota. The DI shape is identical; Kiota would drag an OpenAPI
  document and a codegen step into the sample and start teaching Kiota rather than Freista.

## Testing

`Freista.Aspire` is mostly integration glue, so its tests split by what can be tested without Docker:

| Level | Coverage |
|---|---|
| Unit (`test/Freista.Aspire.Test`) | `AspireRunOptions` accumulates `WaitFor` names and `Services` callbacks across calls; `StartupTimeout` defaults to 5 minutes; `ctx.Aspire()` throws an actionable message when no `DistributedApplication` is registered, and returns it when one is. |
| Integration (the sample) | The suite runs green against a real AppHost; the app is disposed after the run; a `WaitFor` naming an unknown resource fails with a message listing the known resource names. |

The sample is the integration test. It requires a container runtime, so CI must either provide one or
exclude the sample project — decided when CI is set up, and noted here so it is not a surprise.

## Follow-on, not included

- **Cross-process code coverage.** The roadmap's stated hard requirement and its own words call it
  "mostly an integration/investigation spike". Aspire launches services as separate processes, so the
  coverage profiler must attach to each child — candidate approach: propagate
  `CORECLR_ENABLE_PROFILING` / `CORECLR_PROFILER` / `CORECLR_PROFILER_PATH` to every resource via
  `WithEnvironment(...)` and merge the outputs. If it works, injecting those automatically is a
  natural `Freista.Aspire` feature; if it does not, nothing here depends on it. Runs as a spike
  against this sample once it exists.
- **A `dotnet new freista` template** scaffolding the explicit `Program.cs`.
