# Cross-Process Code Coverage for Aspire — Spike Result

- **Date:** 2026-09-04
- **Status:** **Answered. It already works** — no Freista feature needed.
- **Target:** `samples/AspireAppointments`, .NET 10.0.303, Aspire 13.5.3,
  Microsoft.Testing.Platform 2.2.3, Microsoft.Testing.Extensions.CodeCoverage 18.7.0, Windows.

## The question

The roadmap called this the hard requirement and "the real risk": Aspire launches services as separate
processes, so `--coverage` was expected to cover only the test host. The proposed fix was to propagate
the CLR profiler variables (`CORECLR_ENABLE_PROFILING`, `CORECLR_PROFILER`, `CORECLR_PROFILER_PATH`)
to each resource via `WithEnvironment(...)` and merge per-process outputs.

## The result

**None of that is necessary for project resources.** Running the sample with `--coverage` produced a
report containing the API that Aspire launched as a child process:

```xml
<package line-rate="0.96875" name="AspireAppointments.Api">
  <class name="Program" filename="...\AspireAppointments.Api\Program.cs">
    <method name="&lt;Main&gt;$"><line number="7" hits="1" ... />
```

The API's `Main` is recorded as hit. The test host never executes that code, so this is genuinely the
child process's coverage. Child processes inherit the profiled test host's environment, and DCP does
not scrub it — so the profiler attaches to everything Aspire launches.

`AspireAppointments.AppHost` appears in the report too, as do the Freista assemblies.

## What it did take

Two things, neither of which is a Freista feature:

1. **Explicit extension registration.** Because a Freista/Aspire suite owns its own `Main`, the
   coverage extension is not auto-wired and `--coverage` is not even a recognised option until it is
   registered. The `ConfigureTestApplication` hook that `AspireRunOptions` already exposes is exactly
   the seam:

   ```csharp
   aspire.ConfigureTestApplication(b => b.AddCodeCoverageProvider());
   ```

   This is worth documenting: "I turned on `--coverage` and it says unknown option" is the first thing
   a user will hit, and the cause is owning `Main`, not anything Aspire-specific.

2. **A matching extension version.** `Microsoft.Testing.Extensions.CodeCoverage` **18.0.4** fails at
   run time against MTP 2.2.3 with
   `TypeLoadException: Method 'OnTestSessionStartingAsync' … does not have an implementation`, and the
   process exits `-532462766` (CLR fatal error) *after* the tests pass — so it looks like a coverage
   bug rather than a version mismatch. **18.7.0** works. Pin deliberately.

## Limits of this result

- **Verified on Windows only**, with DCP launching project resources. Linux/CI behaviour is unverified
  and inherits nothing from this spike.
- **Container resources are not covered and cannot be**, because a container's environment is
  constructed explicitly rather than inherited. If a suite containerises its *own* service and wants
  coverage of it, the roadmap's `WithEnvironment` propagation becomes relevant again — but for a
  third-party image (Postgres, Redis) there is nothing to cover. The sample deliberately uses a
  project resource, so this case is untested.
- **The profiler attaches indiscriminately** to every child, DCP included. That caused no problem
  here, but a suite spawning many processes may see overhead, and multiple processes writing coverage
  is exactly what `dotnet-coverage`'s server mode exists to arbitrate. Not needed at this scale.

## Consequence

The roadmap item is closed with **no code**. `Freista.Aspire` needs no coverage feature; the
`ConfigureTestApplication` seam it already has is sufficient, and the finding belongs in
documentation rather than in the framework.
