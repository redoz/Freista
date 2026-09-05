# Service Registration — Design

- **Date:** 2026-09-04
- **Status:** Design approved in brainstorming.
- **Scope:** `src/Raun.Mtp` (`RaunTestApplication`, `RaunTestFramework`, `RaunRunLoop`).
  `src/Raun` is untouched.
- **Out of scope:** a `dotnet new` template, flipping the generated-entry-point default, C2, the
  Aspire sample itself.

## Problem

There is nowhere for a test assembly to register its own services. `ScenarioContext.Services` is
plumbed the whole way down and, as of `a5da7f1a`, is populated — with **MTP's own provider**, which
carries the platform's logger factory, command-line options, and configuration. So a step can today
resolve MTP internals and couple itself to the platform, and still has no way to reach a `DbContext`,
an `HttpClient`, or an Aspire AppHost.

This has now blocked three things: teardown (what connection does a cleanup run on), the `ctx.Services`
wart, and the Aspire sample (where does the AppHost live).

## The hook already exists

Raun's generated entry point is gated on the MSBuild property `RaunGenerateProgram` (default
`true`). Setting it to `false` lets a consumer write their own `Program.cs` calling the public
bootstrap `RaunTestApplication.RunAsync`. **The user can already own `Main`**, which means all
async setup is theirs — decisive for Aspire, because `DistributedApplicationTestingBuilder.CreateAsync`
is async and awaiting it inside a DI factory would be grim.

So no discovery mechanism is needed. No `[RaunStartup]` attribute, no `IRaunStartup` interface,
no assembly scanning. All three were considered and rejected as redundant surface.

## Design

### One new parameter

```csharp
public static Task<int> RunAsync(
    string[] args,
    Action<ITestApplicationBuilder>? configure = null,
    bool simulateTime = false,
    IServiceProvider? services = null)
```

The consumer builds the provider in their own `Main` and keeps ownership of its lifetime; Raun
never disposes it.

```csharp
// Program.cs  —  <RaunGenerateProgram>false</RaunGenerateProgram>
var app = await DistributedApplicationTestingBuilder
    .CreateAsync<Projects.AspireAppointments_AppHost>();
await app.StartAsync();

var services = new ServiceCollection();
services.AddSingleton(app);
services.AddScoped<AppointmentsClient>();

return await RaunTestApplication.RunAsync(args, services: services.BuildServiceProvider());
```

### A scope per scenario

`RaunRunLoop` resolves `IServiceScopeFactory` from the supplied provider and opens **one scope per
scenario**; `ScenarioContext.Services` becomes that scope's provider.

This buys both lifetimes through ordinary .NET semantics, with no Raun-specific vocabulary:
`AddSingleton` gives the AppHost once per run, `AddScoped` a fresh `DbContext` per scenario.

When the provider has no `IServiceScopeFactory` (a hand-rolled `IServiceProvider`), the provider is
used directly and no scope is created — the scenario simply shares the root.

### Disposal order

The scenario scope is disposed **after the scenario's teardown node completes**, filling the slot the
teardown design reserved. User cleanups may hold objects resolved from the scope, so the scope cannot
go first. Since the scheduler runs teardown as the last thing inside `RunAsync`, disposing the scope
after that call returns is sufficient and needs no new hook.

### `ctx.Services` stops being MTP's provider

It is the scenario scope's provider, or `null` when the consumer supplied nothing. MTP's provider
stays internal to `RaunTestFramework` for framework use (`HtmlReportPath.Resolve`, the logger
factory). This is a deliberate behaviour change to what shipped in `a5da7f1a`: exposing platform
internals as user-facing DI was a wart, and nothing depends on it — `ctx.Services` was null until
that commit.

### Cost

`src/Raun.Mtp` takes a dependency on `Microsoft.Extensions.DependencyInjection.Abstractions`, for
`IServiceScopeFactory` and `IServiceScope`. `src/Raun` is untouched: `IServiceProvider` is BCL.

## Explicitly not doing

- **No discovery mechanism.** The consumer's `Main` is the hook.
- **No change to the `RaunGenerateProgram` default.** It stays `true`, so "just add the package"
  keeps working. A `dotnet new raun` template that scaffolds a real `Program.cs` — the ASP.NET
  shape, where the entry point is visible code rather than a hidden generated file — is the intended
  future default and is tracked as separate work. Flipping the property's default without that
  template would turn a bare package add into `CS5001`, which is a worse experience than the magic.
- **Raun does not build or dispose the provider.** The consumer owns it.

## Testing

| Project | Coverage |
|---|---|
| `Raun.Mtp.Test` | A provider passed to `RunAsync` reaches a step's `ctx.Services`; a scoped registration yields a **different** instance per scenario and the **same** instance within one scenario; a singleton yields the same instance across scenarios; the scope is disposed after the run (a scoped `IDisposable` sees `Dispose`); no provider ⇒ `ctx.Services` is null and steps still run; a provider without `IServiceScopeFactory` is used directly without throwing. |
| `Raun.Mtp.Test` | A scoped disposable is **not** disposed before teardown runs — the ordering the teardown design reserved. |
