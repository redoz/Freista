# Scenario Teardown Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Freista teardown — cleanup registered as a closure by the step that created the thing, run after the scenario under a per-scenario policy, and reported as its own discovered test node so a failing cleanup is never silent.

**Architecture:** `ScenarioContext.OnTeardown` appends to a scheduler-owned `TeardownLog`. After the DAG drains, the scheduler runs the registered closures in reverse-topological order of their owning step and reports the outcome on a generator-emitted `Teardown` node — an ordinary discovered, numbered node carrying an `IsTeardown` marker that only the scheduler reads. Ordinary cleanups obey the scenario's `[Teardown(Run.…)]` policy; `Cleanup.Required` ones always run, including after cancellation, on their own token.

**Tech Stack:** .NET 10, Microsoft.Testing.Platform, Roslyn incremental generator, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-04-scenario-teardown-design.md`

## Global Constraints

- **Version control: `jj` only.** Never run `git commit/add/branch/checkout/reset/rebase/stash/merge/push`. Read-only `git status`/`log`/`diff` is fine. Commit each task with `jj commit -m "..."`, then `jj bookmark set feat/scenario-conditionals -r @-`.
- **No `Co-Authored-By` and no tooling trailers of any kind** in commit messages.
- Conventional-commit prefixes: `feat(scope):`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`.
- **Build/test:** `dotnet build Freista.slnx`, `dotnet test Freista.slnx`. MTP does **not** accept `--nologo` or `--filter`; run whole projects.
- **Baseline: 323 tests, 0 warnings.** Every task ends green; the count only grows.
- **TDD:** the failing test is written and seen to fail before the implementation, in the same task.
- **A not-taken teardown is never green.** `StepStatus.NotTaken` when policy skipped everything.
- **Never abort the cleanup chain on error.** A throwing cleanup is recorded; the rest still run.
- **Non-goals (do not build):** a user-facing DI registration API, C2 resource-aware scheduling, OTEL correlation, any `IAsyncLifetime`-style scenario instance.

## File Structure

| File | Change |
|---|---|
| `src/Freista/Teardown/Cleanup.cs` | **new** — `public enum Cleanup { Optional, Required }` |
| `src/Freista/Teardown/Run.cs` | **new** — `public enum Run { Always, OnSuccess, Never }` (core, because `ScenarioDefinition` carries it) |
| `src/Freista/Teardown/TeardownLog.cs` | **new** — thread-safe registration list owned by the scheduler |
| `src/Freista/ScenarioContext.cs` | `OnTeardown` overloads; internal `TeardownLog` reference |
| `src/Freista/Model/ScenarioNode.cs` | add `IsTeardown` |
| `src/Freista/Model/ScenarioDefinition.cs` | add `TeardownPolicy` |
| `src/Freista/Scheduling/ScenarioScheduler.cs` | pass the log into each context; run the teardown node after the DAG |
| `src/Freista.Mtp/TeardownAttribute.cs` | **new** — `[Teardown(Run)]`, beside `ScenarioAttribute` |
| `src/Freista.Generator/Lowering/AttributeReader.cs` | read `[Teardown]` |
| `src/Freista.Generator/Lowering/Ir.cs` | `ParsedScenario.TeardownPolicy`, `ParsedStep.IsTeardown` |
| `src/Freista.Generator/Lowering/ScenarioParser.cs` | append the teardown node; carry the policy |
| `src/Freista.Generator/Emit/ScenarioEmitter.cs` | emit `IsTeardown` and `TeardownPolicy` |
| `test/Freista.Test/TeardownTests.cs` | **new** |
| `test/Freista.Generator.Test/SampleSources.cs`, `ConditionalLoweringTests.cs` (or new `TeardownLoweringTests.cs`) | lowering coverage |
| `test/Freista.Mtp.Test/FreistaDiscovererTests.cs` | the teardown node IS discovered and numbered last |
| `samples/AppointmentTests/AppointmentDsl.cs` | a step registering cleanup |
| `README.md` | teardown section |

---

### Task 1: Core model — enums, `IsTeardown`, `TeardownPolicy`, `TeardownLog`, `OnTeardown`

**Files:**
- Create: `src/Freista/Teardown/Cleanup.cs`, `src/Freista/Teardown/Run.cs`, `src/Freista/Teardown/TeardownLog.cs`
- Modify: `src/Freista/Model/ScenarioNode.cs`, `src/Freista/Model/ScenarioDefinition.cs`, `src/Freista/ScenarioContext.cs`
- Test: `test/Freista.Test/TeardownTests.cs`

**Interfaces:**
- Produces: `Cleanup.Optional|Required`; `Run.Always|OnSuccess|Never`; `ScenarioNode.IsTeardown` (bool, default false); `ScenarioDefinition.TeardownPolicy` (`Run`, default `Always`); `TeardownLog` with `Add(int owningStepIndex, Cleanup kind, Func<Task> cleanup)` and `IReadOnlyList<TeardownRegistration> Entries`; `TeardownRegistration(int OwningStepIndex, int Sequence, Cleanup Kind, Func<Task> Cleanup)`; `ScenarioContext.OnTeardown(Func<Task>)` and `ScenarioContext.OnTeardown(Cleanup, Func<Task>)`.

- [ ] **Step 1: Write the failing tests** — create `test/Freista.Test/TeardownTests.cs`

```csharp
using Freista.Model;
using Xunit;

namespace Freista.Test;

/// <summary>
/// Cleanup is registered by the step that created the thing, so the closure captures both the object
/// and the connection. The log is scenario-scoped and written concurrently by parallel steps.
/// </summary>
public class TeardownTests
{
    private static ScenarioContext Context(string stepId, TeardownLog log, int stepIndex)
    {
        var ctx = new ScenarioContext(stepId, stepId, services: null, CancellationToken.None);
        ctx.AttachTeardown(log, stepIndex);
        return ctx;
    }

    [Fact]
    public void Registrations_record_their_owning_step_and_sequence()
    {
        var log = new TeardownLog();
        var ctx = Context("a", log, stepIndex: 3);

        ctx.OnTeardown(() => Task.CompletedTask);
        ctx.OnTeardown(Cleanup.Required, () => Task.CompletedTask);

        Assert.Equal(2, log.Entries.Count);
        Assert.All(log.Entries, e => Assert.Equal(3, e.OwningStepIndex));
        Assert.Equal(Cleanup.Optional, log.Entries[0].Kind);
        Assert.Equal(Cleanup.Required, log.Entries[1].Kind);
        Assert.True(log.Entries[1].Sequence > log.Entries[0].Sequence);
    }

    [Fact]
    public void A_context_with_no_log_attached_ignores_registration()
    {
        // A context built outside the scheduler (unit tests of DSL methods) must not throw.
        var ctx = new ScenarioContext("a", "a", services: null, CancellationToken.None);

        ctx.OnTeardown(() => Task.CompletedTask);   // must not throw
    }

    [Fact]
    public void Concurrent_registration_keeps_every_entry()
    {
        var log = new TeardownLog();

        Parallel.For(0, 200, i =>
        {
            var ctx = Context("s" + i, log, i);
            ctx.OnTeardown(() => Task.CompletedTask);
        });

        Assert.Equal(200, log.Entries.Count);
        Assert.Equal(200, log.Entries.Select(e => e.Sequence).Distinct().Count());
    }

    [Fact]
    public void Node_is_not_a_teardown_node_by_default()
    {
        var node = new ScenarioNode
        {
            Index = 0,
            StepId = "s",
            Phase = "Given",
            OperationName = "Op",
            DisplayNameTemplate = "op",
            DependsOn = [],
            Invoke = (_, _) => Task.FromResult<object?>(null),
        };

        Assert.False(node.IsTeardown);
    }

    [Fact]
    public void Definition_defaults_to_running_teardown_always()
    {
        var def = new ScenarioDefinition
        {
            ScenarioId = "s",
            DisplayName = "s",
            MethodName = "Ns.S",
            Nodes = [],
        };

        Assert.Equal(Run.Always, def.TeardownPolicy);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Freista.Test/Freista.Test.csproj`
Expected: FAIL — compile errors; `TeardownLog`, `Cleanup`, `Run`, `AttachTeardown`, `OnTeardown`, `IsTeardown`, `TeardownPolicy` do not exist.

- [ ] **Step 3: Create `src/Freista/Teardown/Cleanup.cs`**

```csharp
namespace Freista;

/// <summary>
/// Whether a registered cleanup is optional or mandatory. This is a KIND, not a policy: leaving
/// database rows behind for inspection is a choice, but leaving a container running is a leak, so a
/// <see cref="Required"/> cleanup runs even under <see cref="Run.Never"/>.
/// </summary>
public enum Cleanup
{
    /// <summary>Runs only when the scenario's <see cref="Run"/> policy allows it.</summary>
    Optional,

    /// <summary>Always runs, whatever the scenario's policy — including after cancellation.</summary>
    Required,
}
```

- [ ] **Step 4: Create `src/Freista/Teardown/Run.cs`**

```csharp
namespace Freista;

/// <summary>When a scenario's <see cref="Cleanup.Optional"/> teardowns run.</summary>
public enum Run
{
    /// <summary>Run them whether the scenario passed or failed. The default.</summary>
    Always,

    /// <summary>Run them only when every step passed, so a failed scenario leaves its state intact
    /// for inspection.</summary>
    OnSuccess,

    /// <summary>Never run them. <see cref="Cleanup.Required"/> registrations still run.</summary>
    Never,
}
```

- [ ] **Step 5: Create `src/Freista/Teardown/TeardownLog.cs`**

```csharp
using System.Collections.Concurrent;

namespace Freista;

/// <summary>One registered cleanup, tagged with the step that registered it.</summary>
/// <param name="OwningStepIndex">The step whose execution registered this cleanup; the scheduler
/// orders teardown by the reverse topological position of this step.</param>
/// <param name="Sequence">Global registration order, used to break ties within one step.</param>
public readonly record struct TeardownRegistration(
    int OwningStepIndex, int Sequence, Cleanup Kind, Func<Task> Cleanup);

/// <summary>
/// Scenario-scoped collector for cleanups registered by steps. Owned by the scheduler and shared by
/// every step's <see cref="ScenarioContext"/> — steps register concurrently, so this is the
/// synchronized object while the context itself stays per-step.
/// </summary>
public sealed class TeardownLog
{
    private readonly ConcurrentQueue<TeardownRegistration> _entries = new();
    private int _sequence = -1;

    /// <summary>Records a cleanup for <paramref name="owningStepIndex"/>.</summary>
    public void Add(int owningStepIndex, Cleanup kind, Func<Task> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        _entries.Enqueue(new TeardownRegistration(
            owningStepIndex, Interlocked.Increment(ref _sequence), kind, cleanup));
    }

    /// <summary>Registrations in registration order.</summary>
    public IReadOnlyList<TeardownRegistration> Entries => [.. _entries];
}
```

- [ ] **Step 6: Add `IsTeardown` to `src/Freista/Model/ScenarioNode.cs`** (immediately after `IsSynthetic`)

```csharp
    /// <summary>
    /// True for the scenario's single generator-emitted teardown node. The INVERSE of
    /// <see cref="IsSynthetic"/>: it is discovered and numbered like an ordinary step (users must see
    /// a failing cleanup in CI), and only the scheduler and the report treat it specially.
    /// </summary>
    public bool IsTeardown { get; init; }
```

- [ ] **Step 7: Add `TeardownPolicy` to `src/Freista/Model/ScenarioDefinition.cs`** (after `Timeout`)

```csharp
    /// <summary>When this scenario's <see cref="Cleanup.Optional"/> teardowns run; from
    /// <c>[Teardown(Run.…)]</c>, defaulting to <see cref="Run.Always"/> when the attribute is absent.</summary>
    public Run TeardownPolicy { get; init; } = Run.Always;
```

- [ ] **Step 8: Add registration to `src/Freista/ScenarioContext.cs`.** Add the fields next to `_logs`, and the members next to `Log`.

```csharp
    private TeardownLog? _teardownLog;
    private int _teardownStepIndex;
```

```csharp
    /// <summary>Wires this context to the scenario's teardown log. Called by the scheduler; a context
    /// built outside it (a DSL method under unit test) simply has nowhere to register, and
    /// <see cref="OnTeardown(Func{Task})"/> becomes a no-op rather than throwing.</summary>
    internal void AttachTeardown(TeardownLog log, int stepIndex)
    {
        _teardownLog = log;
        _teardownStepIndex = stepIndex;
    }

    /// <summary>
    /// Registers cleanup for something this step created. The closure captures the object and the
    /// connection, because it is written where both are in scope. Runs after the scenario, subject to
    /// the scenario's <c>[Teardown(Run.…)]</c> policy.
    /// </summary>
    public void OnTeardown(Func<Task> cleanup) => OnTeardown(Cleanup.Optional, cleanup);

    /// <summary>
    /// Registers cleanup of the given kind. <see cref="Cleanup.Required"/> runs whatever the
    /// scenario's policy says — use it for things whose absence is a leak rather than a choice.
    /// </summary>
    public void OnTeardown(Cleanup kind, Func<Task> cleanup)
        => _teardownLog?.Add(_teardownStepIndex, kind, cleanup);
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test test/Freista.Test/Freista.Test.csproj`
Expected: PASS — 323 baseline plus the 5 new.

- [ ] **Step 10: Full solution green**

Run: `dotnet build Freista.slnx` then `dotnet test Freista.slnx`
Expected: 0 warnings, all pass.

- [ ] **Step 11: Commit**

```bash
jj commit -m "feat(teardown): registration model — Cleanup/Run enums, TeardownLog, OnTeardown"
```
```bash
jj bookmark set feat/scenario-conditionals -r @-
```

---

### Task 2: Scheduler — attach the log, run the teardown node

**Files:**
- Modify: `src/Freista/Scheduling/ScenarioScheduler.cs`
- Test: `test/Freista.Test/TeardownTests.cs`

**Interfaces:**
- Consumes: everything from Task 1.
- Produces: scheduler behaviour only. The teardown node's `StepResult` reports `Passed` / `Failed` / `NotTaken`; its `SkipReason` names why when not run; its `Exception` is an `AggregateException` of every cleanup that threw.

Rules:

| Situation | Teardown node |
|---|---|
| Cleanups ran, none threw | `Passed` |
| One or more threw | `Failed`, `Exception` = `AggregateException` of all |
| Policy skipped everything and nothing was `Required` | `NotTaken`, reason naming the policy |
| No registrations at all | `NotTaken`, reason `"no teardown registered"` |

Scenario success = every non-teardown node is `Passed` or `NotTaken`.

- [ ] **Step 1: Write the failing tests** — append to `test/Freista.Test/TeardownTests.cs` (add `using Freista.Scheduling;` at the top)

```csharp
    private static ScenarioNode Step(
        int index,
        Func<IStepInputs, ScenarioContext, Task<object?>> invoke,
        int[]? dependsOn = null) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "Given",
        OperationName = $"Op{index}",
        DisplayNameTemplate = $"op {index}",
        DependsOn = dependsOn ?? [],
        Invoke = invoke,
    };

    private static ScenarioNode TeardownNode(int index) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "Then",
        OperationName = "Teardown",
        DisplayNameTemplate = "Teardown",
        DependsOn = [],
        IsTeardown = true,
        Invoke = (_, _) => Task.FromResult<object?>(null),
    };

    private static ScenarioDefinition Def(Run policy, params ScenarioNode[] nodes) => new()
    {
        ScenarioId = "scn",
        DisplayName = "scenario",
        MethodName = "Ns.Scn",
        TeardownPolicy = policy,
        Nodes = nodes,
    };

    [Fact]
    public async Task Cleanups_run_in_reverse_topological_order()
    {
        var order = new List<string>();
        var def = Def(Run.Always,
            Step(0, (_, ctx) => { ctx.OnTeardown(() => { lock (order) order.Add("first"); return Task.CompletedTask; }); return Task.FromResult<object?>(null); }),
            Step(1, (_, ctx) => { ctx.OnTeardown(() => { lock (order) order.Add("second"); return Task.CompletedTask; }); return Task.FromResult<object?>(null); }, [0]),
            TeardownNode(2));

        var results = await new ScenarioScheduler().RunAsync(def);

        Assert.Equal(["second", "first"], order);
        Assert.Equal(StepStatus.Passed, results[2].Status);
    }

    [Fact]
    public async Task Within_one_step_cleanups_run_in_reverse_registration_order()
    {
        var order = new List<string>();
        var def = Def(Run.Always,
            Step(0, (_, ctx) =>
            {
                ctx.OnTeardown(() => { order.Add("a"); return Task.CompletedTask; });
                ctx.OnTeardown(() => { order.Add("b"); return Task.CompletedTask; });
                return Task.FromResult<object?>(null);
            }),
            TeardownNode(1));

        await new ScenarioScheduler().RunAsync(def);

        Assert.Equal(["b", "a"], order);
    }

    [Fact]
    public async Task OnSuccess_skips_optional_cleanups_when_a_step_failed()
    {
        var ran = false;
        var def = Def(Run.OnSuccess,
            Step(0, (_, ctx) => { ctx.OnTeardown(() => { ran = true; return Task.CompletedTask; }); return Task.FromResult<object?>(null); }),
            Step(1, (_, _) => throw new InvalidOperationException("boom"), [0]),
            TeardownNode(2));

        var results = await new ScenarioScheduler().RunAsync(def);

        Assert.False(ran);
        Assert.Equal(StepStatus.NotTaken, results[2].Status);
    }

    [Fact]
    public async Task OnSuccess_runs_optional_cleanups_when_every_step_passed()
    {
        var ran = false;
        var def = Def(Run.OnSuccess,
            Step(0, (_, ctx) => { ctx.OnTeardown(() => { ran = true; return Task.CompletedTask; }); return Task.FromResult<object?>(null); }),
            TeardownNode(1));

        await new ScenarioScheduler().RunAsync(def);

        Assert.True(ran);
    }

    [Fact]
    public async Task Required_cleanups_run_even_under_Run_Never()
    {
        var optional = false;
        var required = false;
        var def = Def(Run.Never,
            Step(0, (_, ctx) =>
            {
                ctx.OnTeardown(() => { optional = true; return Task.CompletedTask; });
                ctx.OnTeardown(Cleanup.Required, () => { required = true; return Task.CompletedTask; });
                return Task.FromResult<object?>(null);
            }),
            TeardownNode(1));

        var results = await new ScenarioScheduler().RunAsync(def);

        Assert.False(optional);
        Assert.True(required);
        Assert.Equal(StepStatus.Passed, results[1].Status);
    }

    [Fact]
    public async Task A_throwing_cleanup_does_not_stop_the_rest()
    {
        var later = false;
        var def = Def(Run.Always,
            Step(0, (_, ctx) => { ctx.OnTeardown(() => { later = true; return Task.CompletedTask; }); return Task.FromResult<object?>(null); }),
            Step(1, (_, ctx) => { ctx.OnTeardown(() => throw new InvalidOperationException("cleanup boom")); return Task.FromResult<object?>(null); }, [0]),
            TeardownNode(2));

        var results = await new ScenarioScheduler().RunAsync(def);

        Assert.True(later);
        Assert.Equal(StepStatus.Failed, results[2].Status);
        Assert.Contains("cleanup boom", results[2].Exception!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Teardown_node_is_not_taken_when_nothing_registered()
    {
        var def = Def(Run.Always, Step(0, (_, _) => Task.FromResult<object?>(null)), TeardownNode(1));

        var results = await new ScenarioScheduler().RunAsync(def);

        Assert.Equal(StepStatus.NotTaken, results[1].Status);
    }

    [Fact]
    public async Task A_step_in_an_untaken_branch_registers_nothing()
    {
        var ran = false;
        var cond = new ScenarioNode
        {
            Index = 0,
            StepId = "cond",
            Phase = "Given",
            OperationName = "Cond",
            DisplayNameTemplate = "cond",
            DependsOn = [],
            Invoke = (_, _) => Task.FromResult<object?>(false),
            EvaluateCondition = static o => (bool)o!,
        };
        var arm = new ScenarioNode
        {
            Index = 1,
            StepId = "arm",
            Phase = "When",
            OperationName = "Arm",
            DisplayNameTemplate = "arm",
            DependsOn = [0],
            Guards = [new Guard(0, true)],
            Invoke = (_, ctx) => { ctx.OnTeardown(() => { ran = true; return Task.CompletedTask; }); return Task.FromResult<object?>(null); },
        };

        var results = await new ScenarioScheduler().RunAsync(Def(Run.Always, cond, arm, TeardownNode(2)));

        Assert.Equal(StepStatus.NotTaken, results[1].Status);
        Assert.False(ran);
        Assert.Equal(StepStatus.NotTaken, results[2].Status);
    }

    [Fact]
    public async Task Required_cleanups_run_after_the_scenario_is_cancelled()
    {
        // The case that matters most: a cancelled or timed-out scenario is exactly when a container
        // leaks, so the cancelled token must not suppress the cleanup that prevents it.
        var released = false;
        using var cts = new CancellationTokenSource();
        var def = Def(Run.Always,
            Step(0, (_, ctx) =>
            {
                ctx.OnTeardown(Cleanup.Required, () => { released = true; return Task.CompletedTask; });
                cts.Cancel();
                return Task.FromResult<object?>(null);
            }),
            Step(1, (_, _) => Task.FromResult<object?>(null), [0]),
            TeardownNode(2));

        await new ScenarioScheduler().RunAsync(def, cancellationToken: cts.Token);

        Assert.True(released);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Freista.Test/Freista.Test.csproj`
Expected: FAIL — the teardown node is currently run as an ordinary node (its no-op `Invoke` makes it `Passed`), no cleanup runs, and ordering assertions fail.

- [ ] **Step 3: Exclude the teardown node from the DAG and attach the log.** In `RunAsync`, after `var pending = new HashSet<int>(Enumerable.Range(0, count));`, add:

```csharp
        // The teardown node is not part of the DAG: it runs after every other node is terminal.
        var teardownIndex = -1;
        for (var i = 0; i < count; i++)
        {
            if (nodes[i].IsTeardown)
            {
                teardownIndex = i;
                pending.Remove(i);
            }
        }

        var teardownLog = new TeardownLog();
```

- [ ] **Step 4: Give each step's context the log.** In `RunNodeAsync`, after the `context` is constructed and before `ScenarioContext.SetCurrent(context)`, add a parameter and the call. Change the signature to take `TeardownLog teardownLog`, pass it from the single call site in `RunAsync`, and add:

```csharp
        context.AttachTeardown(teardownLog, node.Index);
```

- [ ] **Step 5: Run teardown after the loop.** In `RunAsync`, immediately after the `while` loop ends and before `return results.Select(r => r!).ToList();`, add:

```csharp
        if (teardownIndex >= 0)
        {
            await RunTeardownAsync(teardownIndex).ConfigureAwait(false);
        }
```

- [ ] **Step 6: Add the teardown local function** next to `ApplyTerminalAsync` inside `RunAsync`

```csharp
        async Task RunTeardownAsync(int i)
        {
            var node = nodes[i];
            var name = FormatName(node, inputs);

            // Success is a property of the SCENARIO, not of the step that registered a cleanup: a
            // failed run should leave the whole world intact, not a half-torn-down mix of it.
            var succeeded = true;
            for (var n = 0; n < count; n++)
            {
                if (n != i && status[n] is not (StepStatus.Passed or StepStatus.NotTaken))
                {
                    succeeded = false;
                    break;
                }
            }

            var optionalAllowed = definition.TeardownPolicy switch
            {
                Run.Always => true,
                Run.OnSuccess => succeeded,
                _ => false,
            };

            // Reverse topological order of the owning step, then reverse registration order within a
            // step. Registration order alone is nondeterministic: steps run concurrently.
            var selected = teardownLog.Entries
                .Where(e => e.Kind == Cleanup.Required || optionalAllowed)
                .OrderByDescending(e => e.OwningStepIndex)
                .ThenByDescending(e => e.Sequence)
                .ToList();

            var startedAt = _timeProvider.GetUtcNow();
            if (_simulatedTime)
            {
                simStartOffset![i] = TimeSpan.Zero;
                simFinishOffset![i] = TimeSpan.Zero;
            }

            if (selected.Count == 0)
            {
                var reason = teardownLog.Entries.Count == 0
                    ? "no teardown registered"
                    : $"teardown policy is {definition.TeardownPolicy}"
                        + (definition.TeardownPolicy == Run.OnSuccess && !succeeded ? " and the scenario failed" : "");
                status[i] = StepStatus.NotTaken;
                results[i] = new StepResult
                {
                    Node = node,
                    DisplayName = name,
                    Status = StepStatus.NotTaken,
                    StartedAt = startedAt,
                    SkipReason = reason,
                };
                if (observer is not null)
                {
                    await observer.OnStepFinishedAsync(results[i]!).ConfigureAwait(false);
                }

                return;
            }

            if (observer is not null)
            {
                await observer.OnStepStartingAsync(
                    new StepContext { Node = node, DisplayName = name }).ConfigureAwait(false);
            }

            var stopwatch = Stopwatch.StartNew();
            List<Exception>? errors = null;
            foreach (var entry in selected)
            {
                try
                {
                    await entry.Cleanup().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Recorded, never rethrown here: aborting would leak everything behind it.
                    (errors ??= []).Add(ex);
                }
            }

            stopwatch.Stop();
            status[i] = errors is null ? StepStatus.Passed : StepStatus.Failed;
            results[i] = new StepResult
            {
                Node = node,
                DisplayName = name,
                Status = status[i],
                StartedAt = startedAt,
                Duration = stopwatch.Elapsed,
                Exception = errors is null
                    ? null
                    : new AggregateException($"{errors.Count} teardown action(s) failed.", errors),
            };
            if (observer is not null)
            {
                await observer.OnStepFinishedAsync(results[i]!).ConfigureAwait(false);
            }
        }
```

Note the cancellation requirement: this runs **after** the scheduling loop, and the cleanup delegates are invoked directly rather than through `stepCts`, so a cancelled scenario token does not suppress them. The scheduling loop marks remaining nodes `Skipped` on cancellation and then exits, which is what lets this code be reached.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test test/Freista.Test/Freista.Test.csproj`
Expected: PASS.

- [ ] **Step 8: Full solution green**

Run: `dotnet build Freista.slnx` then `dotnet test Freista.slnx`
Expected: 0 warnings, all pass.

- [ ] **Step 9: Commit**

```bash
jj commit -m "feat(teardown): run registered cleanups after the scenario under its policy"
```
```bash
jj bookmark set feat/scenario-conditionals -r @-
```

---

### Task 3: `[Teardown]` attribute and generator emission

**Files:**
- Create: `src/Freista.Mtp/TeardownAttribute.cs`
- Modify: `src/Freista.Generator/Lowering/AttributeReader.cs`, `Ir.cs`, `ScenarioParser.cs`, `src/Freista.Generator/Emit/ScenarioEmitter.cs`
- Test: `test/Freista.Generator.Test/SampleSources.cs`, new `test/Freista.Generator.Test/TeardownLoweringTests.cs`

**Interfaces:**
- Consumes: `Run`, `ScenarioNode.IsTeardown`, `ScenarioDefinition.TeardownPolicy` (Task 1).
- Produces: every lowered scenario ends with a node whose `IsTeardown` is true, `OperationName` is `"Teardown"`, `DisplayNameTemplate` is `"Teardown"`, and `DependsOn` is empty; the definition carries `TeardownPolicy` from the attribute.

The node is emitted **unconditionally**. The generator cannot see registrations — `OnTeardown` is a runtime call inside a DSL method body — so emitting only when `[Teardown]` is present would make "a step registers cleanup in a scenario with no attribute" fail silently.

- [ ] **Step 1: Create `src/Freista.Mtp/TeardownAttribute.cs`**

```csharp
namespace Freista;

/// <summary>
/// Sets when a scenario's <see cref="Cleanup.Optional"/> teardowns run. Absent, the policy is
/// <see cref="Run.Always"/>. <see cref="Cleanup.Required"/> registrations ignore this entirely —
/// they exist for things whose absence is a leak rather than a choice.
/// </summary>
/// <remarks>Lives beside <c>ScenarioAttribute</c> and is read by the generator by metadata name.</remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TeardownAttribute(Run run = Run.Always) : Attribute
{
    /// <summary>When the scenario's optional teardowns run.</summary>
    public Run Run { get; } = run;
}
```

- [ ] **Step 2: Write the failing tests** — create `test/Freista.Generator.Test/TeardownLoweringTests.cs`

```csharp
using Freista;
using Freista.Model;
using Xunit;

namespace Freista.Generator.Test;

/// <summary>
/// Every lowered scenario ends with a discovered teardown node. It is emitted unconditionally: the
/// generator cannot see `OnTeardown` calls (they are runtime calls inside DSL bodies), so emitting
/// it only when `[Teardown]` is present would make a registered cleanup fail silently.
/// </summary>
public class TeardownLoweringTests
{
    private static ScenarioDefinition Lower(string scenario)
    {
        var result = GeneratorHarness.Run(SampleSources.Dsl + scenario);
        result.AssertCompiles();
        return Assert.Single(result.Definitions());
    }

    [Fact]
    public void Every_scenario_ends_with_a_teardown_node()
    {
        var def = Lower(SampleSources.LinearScenario);

        var last = def.Nodes[^1];
        Assert.True(last.IsTeardown);
        Assert.False(last.IsSynthetic);
        Assert.Equal("Teardown", last.OperationName);
        Assert.Empty(last.DependsOn);
        Assert.Single(def.Nodes, n => n.IsTeardown);
    }

    [Fact]
    public void Policy_defaults_to_always_without_the_attribute()
    {
        Assert.Equal(Run.Always, Lower(SampleSources.LinearScenario).TeardownPolicy);
    }

    [Fact]
    public void Policy_comes_from_the_attribute()
    {
        Assert.Equal(Run.OnSuccess, Lower(SampleSources.TeardownOnSuccessScenario).TeardownPolicy);
    }

    [Fact]
    public async Task Registered_cleanup_runs_end_to_end()
    {
        var result = GeneratorHarness.Run(SampleSources.TeardownDsl + SampleSources.TeardownScenario);
        result.AssertCompiles();

        var def = Assert.Single(result.Definitions());
        var results = await def.RunAsync();

        Assert.All(results, r => Assert.True(
            r.Status is StepStatus.Passed,
            $"step {r.Node.Index} ({r.Node.OperationName}) was {r.Status}: {r.SkipReason}{r.Exception}"));
        Assert.True(results[^1].Node.IsTeardown);
    }
}
```

- [ ] **Step 3: Add the sample sources** — append to `test/Freista.Generator.Test/SampleSources.cs`, before the closing brace

```csharp
    // A scenario with an explicit teardown policy.
    public const string TeardownOnSuccessScenario =
        """

        public static class TeardownPolicyScenarios
        {
            [Scenario("policy")]
            [Teardown(Run.OnSuccess)]
            public static async Task Booking()
            {
                var patient = await Given.PatientExists("Jane");
                await Then.AppointmentExists(patient);
            }
        }
        """;

    // A DSL whose step registers a cleanup, proving the closure runs end to end.
    public const string TeardownDsl =
        """
        using System.Threading.Tasks;
        using Freista;

        namespace TeardownDemo;

        public sealed record Patient(string Name);

        public static class Probe
        {
            public static int Cleaned;
        }

        public static class TeardownDsl
        {
            extension(Given)
            {
                [StepName("patient {name} exists")]
                public static Task<Patient> PatientExists(string name, ScenarioContext? ctx = null)
                {
                    ctx?.OnTeardown(() => { Probe.Cleaned++; return Task.CompletedTask; });
                    return Task.FromResult(new Patient(name));
                }
            }

            extension(Then)
            {
                [StepName("the patient should exist")]
                public static Task PatientIsThere(Patient patient) => Task.CompletedTask;
            }
        }
        """;

    public const string TeardownScenario =
        """

        public static class TeardownScenarios
        {
            [Scenario("cleanup runs")]
            public static async Task Booking()
            {
                var patient = await Given.PatientExists("Jane");
                await Then.PatientIsThere(patient);
            }
        }
        """;
```

Note: `SampleSources.Dsl`-based scenarios reference `Then.AppointmentExists`; check the existing `Dsl` constant and use whichever `Then` step it actually declares, adjusting `TeardownOnSuccessScenario` to match. Do not invent a step name.

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test test/Freista.Generator.Test/Freista.Generator.Test.csproj`
Expected: FAIL — no teardown node is emitted; `TeardownPolicy` is always `Always`.

- [ ] **Step 5: Read the attribute.** Add to `src/Freista.Generator/Lowering/AttributeReader.cs`

```csharp
    /// <summary>The scenario's teardown policy as the underlying <c>Run</c> enum value, defaulting to
    /// 0 (<c>Run.Always</c>) when <c>[Teardown]</c> is absent.</summary>
    public static int TeardownPolicy(IMethodSymbol method)
    {
        var attr = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "TeardownAttribute");
        if (attr is { ConstructorArguments.Length: > 0 } && attr.ConstructorArguments[0].Value is int value)
        {
            return value;
        }

        return 0;
    }
```

- [ ] **Step 6: Carry it through the IR.** In `src/Freista.Generator/Lowering/Ir.cs`, add to `ParsedScenario`:

```csharp
    /// <summary>The scenario's teardown policy as the underlying <c>Freista.Run</c> value.</summary>
    public int TeardownPolicy { get; init; }
```

and to `ParsedStep`:

```csharp
    /// <summary>True for the scenario's single teardown node — discovered and numbered like an
    /// ordinary step, but run by the scheduler after the DAG rather than as part of it.</summary>
    public bool IsTeardown { get; init; }
```

- [ ] **Step 7: Emit the node in `ScenarioParser.Parse()`.** Immediately before `var usings = CollectUsings().ToList();`, append the node; and add `TeardownPolicy` to the returned `ParsedScenario`.

```csharp
        // Always emitted: the generator cannot see OnTeardown calls (they happen at run time inside
        // DSL bodies), so emitting conditionally would let a registered cleanup fail silently. With
        // nothing registered the scheduler reports it NotTaken.
        var teardownIndex = _nextIndex++;
        _steps.Add(new ParsedStep
        {
            Index = teardownIndex,
            StepId = GenStableId.ForStep(_scenarioId, "teardown"),
            Phase = "Then",
            OperationName = "Teardown",
            HasResult = false,
            ResultTypeFqn = "object",
            InvokeCallText = "",
            DisplayNameTemplate = "Teardown",
            IsTeardown = true,
            DependsOn = [],
        });
```

```csharp
            TeardownPolicy = AttributeReader.TeardownPolicy(_method),
```

- [ ] **Step 8: Emit the new members** in `src/Freista.Generator/Emit/ScenarioEmitter.cs`. In `BuildNode`, beside the `IsSynthetic` block:

```csharp
        if (step.IsTeardown)
        {
            members.Add(Set("IsTeardown", LiteralExpression(SyntaxKind.TrueLiteralExpression)));
        }
```

In `BuildInvokeLambda`, extend the synthetic early-return so a teardown node also gets the no-op delegate (its `InvokeCallText` is empty and the scheduler never calls it):

```csharp
        if (step.IsSynthetic || step.IsTeardown)
```

And in the `ScenarioDefinition` initializer, after `Set("Timeout", …)`:

```csharp
                Set("TeardownPolicy", ParseExpression($"(global::Freista.Run){scenario.TeardownPolicy}")),
```

- [ ] **Step 9: Run the generator tests**

Run: `dotnet test test/Freista.Generator.Test/Freista.Generator.Test.csproj`
Expected: the new tests PASS. **Existing snapshots and lowering tests WILL fail** — every scenario now has one extra node. That is correct and expected.

- [ ] **Step 10: Update the node-count assertions.** In `ConditionalLoweringTests.cs`, every `Assert.Equal(N, def.Nodes.Count)` grows by one, and `results[^1]` in the end-to-end tests is now the teardown node rather than the last step. Fix each by adding one to the expected count; do **not** weaken a guard or merge assertion. Check `LinearLoweringTests.cs`, `ParallelLoweringTests.cs`, `ResourceLoweringTests.cs`, and `EdgeCaseLoweringTests.cs` for the same.

- [ ] **Step 11: Re-accept the snapshots.** Run the snapshot tests, diff each `.received.cs` against its `.verified.cs`, confirm the ONLY change is the appended teardown node plus the `TeardownPolicy` line on the definition, then copy each received over its verified name and re-run.

Run: `dotnet test test/Freista.Generator.Test/Freista.Generator.Test.csproj`
Expected: PASS.

- [ ] **Step 12: Full solution green**

Run: `dotnet build Freista.slnx` then `dotnet test Freista.slnx`
Expected: 0 warnings, all pass. `Freista.Mtp.Test`'s discovery and numbering tests may also need `+1` on counts — the teardown node IS discovered, which is the intended behaviour.

- [ ] **Step 13: Commit**

```bash
jj commit -m "feat(generator): emit a teardown node and the scenario teardown policy"
```
```bash
jj bookmark set feat/scenario-conditionals -r @-
```

---

### Task 4: MTP coverage, sample, and docs

**Files:**
- Test: `test/Freista.Mtp.Test/FreistaDiscovererTests.cs`
- Modify: `samples/AppointmentTests/AppointmentDsl.cs`, `README.md`

**Interfaces:**
- Consumes: everything above. No production changes are expected in `Freista.Mtp` — the teardown node is an ordinary node to discovery, numbering, and the sink. These tests exist to prove that and to catch a regression that hides it.

- [ ] **Step 1: Write the failing tests** — append to `test/Freista.Mtp.Test/FreistaDiscovererTests.cs`, adding `bool teardown = false` → `IsTeardown = teardown` to the local `Node` helper

```csharp
    [Fact]
    public void Teardown_nodes_are_discovered_unlike_synthetic_ones()
    {
        var definition = Definition(nodes:
        [
            Node(0, "a", "step a"),
            Node(1, "m", "«merge»", synthetic: true),
            Node(2, "t", "Teardown", teardown: true),
        ]);

        var nodes = FreistaDiscoverer.BuildNodes(definition);

        Assert.Equal(2, nodes.Count);
        Assert.Contains(nodes, n => n.DisplayName.Contains("Teardown", StringComparison.Ordinal));
    }

    [Fact]
    public void Teardown_node_takes_the_final_step_number()
    {
        var definition = Definition(nodes:
        [
            Node(0, "a", "step a"),
            Node(1, "b", "step b"),
            Node(2, "t", "Teardown", teardown: true),
        ]);

        var nodes = FreistaDiscoverer.BuildNodes(definition);

        Assert.Equal("3. Teardown", nodes[^1].DisplayName);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test test/Freista.Mtp.Test/Freista.Mtp.Test.csproj`
Expected: FAIL — compile error, the helper has no `teardown` parameter.

- [ ] **Step 3: Add the helper parameter, then re-run**

Run: `dotnet test test/Freista.Mtp.Test/Freista.Mtp.Test.csproj`
Expected: PASS with no production change. If either fails, `IsTeardown` has been wrongly conflated with `IsSynthetic` somewhere — fix that, do not weaken the test.

- [ ] **Step 4: Register a cleanup in the sample.** In `samples/AppointmentTests/AppointmentDsl.cs`, inside `PatientExists`, before the return:

```csharp
            ctx?.OnTeardown(() =>
            {
                // A real suite would delete the row here; the sample only has to prove the closure
                // captures the created object and runs after the scenario.
                ctx.Log($"cleaned up patient {name}");
                return Task.CompletedTask;
            });
```

- [ ] **Step 5: Run the sample**

Run: `dotnet run --project samples/AppointmentTests/AppointmentTests.csproj`
Expected: exit code 0. Each scenario now reports one extra step, `Teardown`, numbered last.

- [ ] **Step 6: Document it in `README.md`.** Add after the "Logging" section:

```markdown
### Teardown

Cleanup is registered by the step that created the thing, so the closure captures both the object and
the connection it needs:

```csharp
[StepName("patient {name} exists")]
[return: Created]
public static async Task<Patient> PatientExists(string name, ScenarioContext? ctx = null)
{
    var patient = await Db.InsertPatient(name);
    ctx?.OnTeardown(() => Db.DeletePatient(patient.Id));
    return patient;
}
```

Every scenario reports a final `Teardown` step, so a cleanup that throws fails visibly instead of
being swallowed. `[Teardown(Run.OnSuccess)]` on the scenario leaves state intact when the test failed;
`Run.Never` disables cleanup entirely. A registration marked `Cleanup.Required` ignores that policy
and runs regardless — including after cancellation — for things whose absence is a leak rather than a
choice:

```csharp
ctx?.OnTeardown(Cleanup.Required, () => container.StopAsync());
```
```

- [ ] **Step 7: Full solution green**

Run: `dotnet build Freista.slnx` then `dotnet test Freista.slnx`
Expected: 0 warnings, all pass.

- [ ] **Step 8: Commit**

```bash
jj commit -m "docs: teardown coverage for discovery, the sample, and the README"
```
```bash
jj bookmark set feat/scenario-conditionals -r @-
```

## Self-Review

- **Spec coverage.** Surface (Task 1, 3), two-level policy (1, 2, 3), reporting as a discovered numbered node (3, 4), reverse-topological order (2), collect-and-continue on error (2), cancellation survival for required cleanups (2), untaken branches registering nothing (2), unconditional emission (3), the reserved final slot — *deliberately not implemented*, called out in the spec as design headroom only.
- **Not covered by design:** the DI-scope disposal slot, per the spec's "Reserved final slot" section; there is nothing to dispose yet.
- **Type consistency.** `TeardownLog.Add(int, Cleanup, Func<Task>)`, `TeardownRegistration(int, int, Cleanup, Func<Task>)`, `ScenarioContext.AttachTeardown(TeardownLog, int)`, `ScenarioContext.OnTeardown(Func<Task>)` / `(Cleanup, Func<Task>)`, `ScenarioNode.IsTeardown`, `ScenarioDefinition.TeardownPolicy` — used consistently across Tasks 1–4.
- **Known ripple:** Task 3 changes every scenario's node count by one, so lowering tests, snapshots, and some MTP counts move. That is called out with instructions rather than left to be discovered.
