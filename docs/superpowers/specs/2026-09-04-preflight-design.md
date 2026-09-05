# Preflight — Design

- **Date:** 2026-09-04
- **Status:** Design approved in brainstorming.
- **Scope:** `src/Raun.Mtp` (`RaunTestApplication`, `RaunTestFramework`, discovery,
  `RaunRunLoop`). `src/Raun` is untouched.
- **First consumer:** `Raun.Aspire` — see `2026-09-04-raun-aspire-design.md`.

## Problem

Raun has no run-level setup hook. Anything a suite must do **once, before any scenario** — start an
Aspire AppHost, migrate a database, wait for a dependency to become healthy — has to happen outside
the Microsoft.Testing.Platform session, in the consumer's `Main`, before `RunAsync` is called.

That has two costs, and the second is the serious one:

1. **It is invisible.** Six seconds of AppHost startup appear nowhere in the report or the timeline.
2. **Its failures are opaque.** A failed start aborts the process *before any test node reports*, so
   CI shows a non-zero exit and no failing test. The one thing that broke is the one thing with no row.

This is the mirror image of teardown: per-scenario cleanup that runs last, versus per-run setup that
runs first. Teardown got a reported node precisely so its failures could not be swallowed; preflight
needs the same for the same reason.

## Surface

```csharp
return await RaunTestApplication.RunAsync(
    args,
    services: provider,
    preflight: async ctx =>
    {
        ctx.Log("starting AppHost");
        await app.StartAsync(ctx.CancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("postgres", ctx.CancellationToken);
    });
```

`preflight` is `Func<ScenarioContext, Task>?`, defaulting to null. When null, **no preflight node
exists** — nothing is discovered and nothing runs, so suites that do not need it are unaffected.

`ScenarioContext` is reused rather than inventing a `PreflightContext`. It already carries
`Services`, `CancellationToken`, `Log`, and `TimeProvider`, and reusing it means
`ScenarioContext.Current` is set while preflight runs — so **anything writing through
`RaunLoggerProvider` during startup is collected into the preflight node automatically**,
including a system under test's own `ILogger` output if it is routed there.

## The node

One node, discovered like any test:

```
Preflight                        PASS  6.8s
  ├ starting AppHost
  ├ postgres → Healthy (4.1s)
  └ api → Healthy (2.4s)

1. Given patient Alice exists    PASS   8ms
```

- **Uid:** `raun:preflight` — stable, so a runner can filter to it.
- **Display name:** `Preflight`. It carries no step number: numbering is per-scenario
  (`ScenarioStepNumbering`), and preflight belongs to the run, not to a scenario.
- **Identity:** its own `TestMethodIdentifierProperty` (`Raun.Preflight`) so runners group it
  apart from scenarios rather than filing it under an empty namespace.
- **Logs** collected during preflight are published as `StandardOutputProperty`, exactly as step logs
  already are.

Granularity is deliberately **one node**, not one per underlying action. Raun.Mtp stays generic —
it knows only that a preflight ran and whether it threw. Naming individual waits would require the
framework to accept a list of named actions and to own their uid scheme; the single node plus its log
carries the same information without that surface. It can become plural later on evidence.

## Failure cascades

When preflight throws, the node reports `Failed` with the exception, and **every scenario's steps
report `Skipped` with reason `preflight failed`**. The run continues to completion so the report is
complete, and exits non-zero because a node failed.

This is the whole point: the failure is attributed to a row that names it, instead of a process that
exits before reporting.

Preflight does **not** run during a discovery request — discovery must never start containers. It is
announced at discovery and executed only on a run request.

## Ordering

Within the run: preflight → scenarios (each with its own DI scope and teardown node) → run finished.
Preflight runs once per run regardless of how many scenarios the filter selects, and runs even when
the filter selects none — a filtered run of one step still needs the app up.

## Not included

- **A run-level "postflight".** Symmetry suggests one, but the consumer's `Main` already has a
  `finally` after `RunAsync` returns, which is a perfectly good place to dispose an AppHost, and its
  failure cannot mislead anyone about test results. Add it only if a real need appears.
- **Multiple preflights.** One delegate; consumers compose inside it.
- **A preflight timeout owned by Raun.** The delegate receives the run's `CancellationToken`;
  imposing an additional timeout is the consumer's business (`Raun.Aspire` has its own
  `StartupTimeout`).

## Testing

| Project | Coverage |
|---|---|
| `Raun.Mtp.Test` | A preflight node is discovered when a delegate is supplied, and **not** discovered when none is; the delegate runs once before any scenario step; it runs once even with multiple scenarios; logs written during preflight reach the node's standard output; a throwing preflight reports `Failed` and every scenario step reports `Skipped` with `preflight failed`; the run still completes and reports every node; a discovery request does **not** invoke the delegate; `ScenarioContext.Current` is set during preflight. |
