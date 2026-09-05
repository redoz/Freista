# Scenario Conditionals Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a `[Scenario]` body use `if`/`else`, where the condition is an awaited phase-marker call, so a step can cause a later sibling step not to run.

**Architecture:** `DependsOn` keeps its all-of meaning. Two additions carry the branch: a `Guard(ConditionIndex, WhenValue)` list on `ScenarioNode` (a node runs only when every guard holds), and synthetic **merge (phi) nodes** — the single place any-of semantics exist — that the generator inserts where a local has two definitions. The generator lowers the condition as an ordinary node and emits a compile-time coercion (`EvaluateCondition`) so the scheduler never performs user-defined conversions on a boxed `object?`. A new `StepStatus.NotTaken` distinguishes "this branch was not chosen" from "skipped because a dependency blew up".

**Tech Stack:** C# 14 / .NET 10, Roslyn incremental source generator (netstandard2.0), Microsoft.Testing.Platform, xUnit + Verify snapshots, jj for version control.

**Spec:** `docs/superpowers/specs/2026-09-03-scenario-conditionals-design.md` (read it before starting; it is the authority).

## Global Constraints

- **Version control: `jj` only.** Never run `git commit/add/branch/checkout/reset/rebase/stash/merge/push`. Read-only `git status`/`log`/`diff` is fine. Commit each task with `jj commit -m "..."`. **No `Co-Authored-By` and no tooling trailers of any kind.**
- Conventional-commit prefixes: `feat(scope):`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`.
- **Build/test:** `dotnet build Raun.slnx` and `dotnet test Raun.slnx`. The test projects use Microsoft.Testing.Platform — `dotnet test --nologo` FAILS with `Unknown option '--nologo'`. Never pass `--nologo`.
- **Baseline: 262 tests, all passing, zero warnings.** Every task must end green; the count only grows.
- **TDD:** behavioural tests are written and seen to fail before the implementation in the same task.
- **Never green for a not-taken branch.** `StepStatus.NotTaken` is never mapped to `Passed`/`PassedTestNodeStateProperty`.
- **New diagnostics must be registered** in `src/Raun.Generator/AnalyzerReleases.Unshipped.md` or the analyzer release-tracking analyzer fails the build.
- **Non-goals (do not build):** loops (`for`/`foreach`/`while`/`do`), `switch`, `try`/`catch`, `goto`, HTML-report decision/merge diamonds, a runnable-source execution model.

## Design decisions resolved beyond the spec

Two points the spec leaves implicit; both are settled here and must be implemented as written.

1. **Pass-through nodes for a bare `if` with no `else`.** The spec says a no-`else` merge takes "the arm's node and the parent definition" as sources, but also that `Validate()` must require merge sources to be *mutually exclusive* (guarded on the same condition with opposite `WhenValue`). A raw parent definition is unguarded, so both would be `Passed` and the "exactly one Passed" merge rule would break. Resolution: the generator emits a **synthetic pass-through node** for the missing arm — `IsSynthetic = true`, `Guards = [Guard(cond, !armValue)]`, `MergeSources = [parentDefIndex]`. A one-source merge is an alias: it passes that source's output through. The real merge's sources are then `[armNode, passThrough]`, which are mutually exclusive, and `Validate()` needs no exception.
2. **HTML report rendering of `NotTaken`.** Distinct rendering (and the decision/merge diamonds) is a separate spec. `HtmlReportModelBuilder.StatusText` maps `StepStatus.NotTaken` to the existing `"skipped"` string, so the report template, its JS, and the HTML snapshot are untouched; the distinction survives in the step's `SkipReason` (`"not taken: {condition}"`). Do not restyle the template in this plan.

## File Structure

| File | Change |
|---|---|
| `src/Raun/Model/Guard.cs` | **new** — `readonly record struct Guard(int ConditionIndex, bool WhenValue)` |
| `src/Raun/Model/StepStatus.cs` | add `NotTaken` |
| `src/Raun/Model/ScenarioNode.cs` | add `Guards`, `MergeSources`, `IsSynthetic`, `EvaluateCondition` |
| `src/Raun/Model/ScenarioDefinition.cs` | `Validate()` invariants for guards + merges; cycle walk covers merge sources |
| `src/Raun/Scheduling/ScenarioScheduler.cs` | guard resolution, merge resolution, `NotTaken` propagation, `ApplySkipAsync` → `ApplyTerminalAsync` |
| `src/Raun.Generator/Lowering/Ir.cs` | `ParsedStep.Guards`/`MergeSources`/`IsSynthetic`/`ConditionCoercionType` |
| `src/Raun.Generator/Lowering/Binding.cs` | `FromAssignment` accepts a bare `IdentifierNameSyntax` (re-assignment inside an arm) |
| `src/Raun.Generator/Lowering/ScenarioParser.cs` | `if`/`else` walk, guard stack, scoped definition map, phi/pass-through insertion |
| `src/Raun.Generator/Emit/ScenarioEmitter.cs` | emit the four new node members (only when non-default, so existing snapshots are byte-identical) |
| `src/Raun.Generator/Analysis/Descriptors.cs` | narrow RAUN003; add RAUN011, RAUN012 |
| `src/Raun.Generator/Analysis/ScenarioAnalyzer.cs` | walk `if`, report RAUN011/RAUN012 |
| `src/Raun.Generator/AnalyzerReleases.Unshipped.md` | register RAUN011/RAUN012, reword RAUN003 |
| `src/Raun.Mtp/RaunDiscoverer.cs` | skip synthetic nodes |
| `src/Raun.Mtp/ScenarioStepNumbering.cs` | skip synthetic nodes when numbering |
| `src/Raun.Mtp/MtpReportSink.cs` | skip synthetic nodes; map `NotTaken` |
| `src/Raun.Mtp/HtmlReport/HtmlReportModelBuilder.cs` | `StatusText` handles `NotTaken` |
| `test/Raun.Test/ModelTests.cs`, `SchedulerTests.cs` | new invariant + scheduler tests |
| `test/Raun.Generator.Test/ConditionalLoweringTests.cs` | **new** |
| `test/Raun.Generator.Test/SampleSources.cs`, `AnalyzerTests.cs`, `GeneratorSnapshotTests.cs` | conditional DSL + scenarios, RAUN003/011/012, new snapshot |
| `test/Raun.Mtp.Test/RaunDiscovererTests.cs`, `ScenarioStepNumberingTests.cs`, `MtpReportSinkTests.cs`, `RunLoopTests.cs` | synthetic exclusion + `NotTaken` mapping + end-to-end |
| `samples/AppointmentTests/AppointmentDsl.cs`, `Scenarios.cs` | a conditional scenario (also the spike target) |
| `README.md` | "Supported scenario subset (v1)" |

---

### Task 1: Domain model — Guard, merge sources, IsSynthetic, NotTaken, Validate

**Files:**
- Create: `src/Raun/Model/Guard.cs`
- Modify: `src/Raun/Model/StepStatus.cs`, `src/Raun/Model/ScenarioNode.cs`, `src/Raun/Model/ScenarioDefinition.cs`
- Test: `test/Raun.Test/ModelTests.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces:
  - `public readonly record struct Guard(int ConditionIndex, bool WhenValue)` in `Raun.Model`.
  - `ScenarioNode.Guards` → `IReadOnlyList<Guard>` (default `[]`).
  - `ScenarioNode.MergeSources` → `IReadOnlyList<int>` (default `[]`); non-empty marks a merge/pass-through node.
  - `ScenarioNode.IsSynthetic` → `bool` (default `false`).
  - `ScenarioNode.EvaluateCondition` → `Func<object?, bool>?` (default `null`).
  - `StepStatus.NotTaken` (appended last, after `Skipped`).

- [ ] **Step 1: Write the failing tests** — append to `test/Raun.Test/ModelTests.cs`. Note the existing private `Node`/`Def` helpers stay as they are; these tests build nodes inline where they need the new members.

```csharp
    private static ScenarioNode Cond(int index, params int[] dependsOn) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "Given",
        OperationName = $"Cond{index}",
        DisplayNameTemplate = $"cond {index}",
        DependsOn = dependsOn,
        Invoke = (_, _) => Task.FromResult<object?>(true),
        EvaluateCondition = static o => (bool)o!,
    };

    private static ScenarioNode Guarded(int index, Guard[] guards, params int[] dependsOn) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "When",
        OperationName = $"Op{index}",
        DisplayNameTemplate = $"op {index}",
        DependsOn = dependsOn,
        Guards = guards,
        Invoke = (_, _) => Task.FromResult<object?>(null),
    };

    private static ScenarioNode Merge(int index, params int[] sources) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "When",
        OperationName = "Merge",
        DisplayNameTemplate = "«merge»",
        DependsOn = [],
        MergeSources = sources,
        IsSynthetic = true,
        Invoke = (_, _) => Task.FromResult<object?>(null),
    };

    [Fact]
    public void Validate_accepts_a_guarded_graph_with_a_merge()
    {
        var def = Def(
            Cond(0),
            Guarded(1, [new Guard(0, true)], 0),
            Guarded(2, [new Guard(0, false)], 0),
            Merge(3, 1, 2));

        def.Validate(); // does not throw
    }

    [Fact]
    public void Validate_rejects_an_out_of_range_guard_condition()
    {
        var def = Def(Cond(0), Guarded(1, [new Guard(9, true)], 0));

        var ex = Assert.Throws<InvalidOperationException>(def.Validate);
        Assert.Contains("guard", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_rejects_a_guard_on_a_node_without_a_condition_evaluator()
    {
        // Node 0 is a plain step: it has no EvaluateCondition, so it cannot gate a branch.
        var def = Def(Node(0), Guarded(1, [new Guard(0, true)], 0));

        var ex = Assert.Throws<InvalidOperationException>(def.Validate);
        Assert.Contains("EvaluateCondition", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_merge_sources_that_are_not_mutually_exclusive()
    {
        // Both sources are guarded on the SAME value, so both could pass — a double-write.
        var def = Def(
            Cond(0),
            Guarded(1, [new Guard(0, true)], 0),
            Guarded(2, [new Guard(0, true)], 0),
            Merge(3, 1, 2));

        var ex = Assert.Throws<InvalidOperationException>(def.Validate);
        Assert.Contains("mutually exclusive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_rejects_an_out_of_range_merge_source()
    {
        var def = Def(Cond(0), Merge(1, 7));

        Assert.Throws<InvalidOperationException>(def.Validate);
    }

    [Fact]
    public void Validate_detects_a_cycle_through_merge_sources()
    {
        // Merge sources are real edges: a cycle through them must be caught like a DependsOn cycle.
        var a = Merge(0, 1);
        var b = Merge(1, 0);
        var def = Def(a, b);

        var ex = Assert.Throws<InvalidOperationException>(def.Validate);
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_accepts_a_single_source_pass_through()
    {
        // A one-source merge is an alias (the no-`else` pass-through) — exclusivity is vacuous.
        var def = Def(Cond(0), Merge(1, 0));

        def.Validate(); // does not throw
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Raun.Test/Raun.Test.csproj --filter "FullyQualifiedName~ModelTests"`
Expected: FAIL — compile errors, `Guard` / `Guards` / `MergeSources` / `IsSynthetic` / `EvaluateCondition` do not exist.

- [ ] **Step 3: Create `src/Raun/Model/Guard.cs`**

```csharp
namespace Raun.Model;

/// <summary>
/// A branch condition a node is gated on: the node runs only when the node at
/// <see cref="ConditionIndex"/> passed AND its evaluated condition equals <see cref="WhenValue"/>
/// (<see langword="true"/> for the <c>if</c> arm, <see langword="false"/> for the <c>else</c> arm).
/// Nested <c>if</c>s stack guards; all of them must hold.
/// </summary>
public readonly record struct Guard(int ConditionIndex, bool WhenValue);
```

- [ ] **Step 4: Add `NotTaken` to `src/Raun/Model/StepStatus.cs`** (append after `Skipped`, keeping existing member order so no numeric value shifts)

```csharp
    /// <summary>Not run because the branch it belongs to was not chosen. Distinct from
    /// <see cref="Skipped"/>: nothing went wrong — the condition simply decided otherwise.
    /// Never reported as a pass.</summary>
    NotTaken,
```

- [ ] **Step 5: Add the four members to `src/Raun/Model/ScenarioNode.cs`** (after `GroupId`, before `Invoke`)

```csharp
    /// <summary>Branch conditions gating this node; ALL must hold for it to run. Empty for an
    /// unconditional node.</summary>
    public IReadOnlyList<Guard> Guards { get; init; } = [];

    /// <summary>
    /// For a synthetic merge (phi) node, the mutually-exclusive candidate producers whose outputs
    /// this node selects between — the ONLY any-of semantics in the graph. Exactly one may pass; its
    /// output becomes this node's output. A single source is a pass-through alias (the missing arm of
    /// a bare <c>if</c>). Empty for an ordinary node.
    /// </summary>
    public IReadOnlyList<int> MergeSources { get; init; } = [];

    /// <summary>True for generator plumbing (merge/pass-through nodes) that is not a business step:
    /// excluded from MTP discovery and step numbering, retained in the HTML report model.</summary>
    public bool IsSynthetic { get; init; }

    /// <summary>
    /// For a condition node, coerces this step's (boxed) output to the branch value. The generator
    /// emits <c>static o =&gt; ((T)o!) ? true : false</c> so Roslyn selects <c>bool</c>, an implicit
    /// conversion, or <c>operator true</c> at compile time — the scheduler never reflects. Null for a
    /// node that gates nothing.
    /// </summary>
    public Func<object?, bool>? EvaluateCondition { get; init; }
```

- [ ] **Step 6: Extend `Validate()` in `src/Raun/Model/ScenarioDefinition.cs`.** Inside the existing `for` loop over nodes, after the `DependsOn` checks, add the guard and merge checks; then make the cycle walk traverse merge sources too.

```csharp
            foreach (var guard in node.Guards)
            {
                if (guard.ConditionIndex < 0 || guard.ConditionIndex >= count)
                {
                    throw new InvalidOperationException(
                        $"Step {node.Index} ('{node.OperationName}') has a guard on out-of-range node {guard.ConditionIndex}.");
                }

                if (Nodes[guard.ConditionIndex].EvaluateCondition is null)
                {
                    throw new InvalidOperationException(
                        $"Step {node.Index} ('{node.OperationName}') is guarded on step {guard.ConditionIndex} "
                        + $"('{Nodes[guard.ConditionIndex].OperationName}'), which has no EvaluateCondition.");
                }
            }

            foreach (var source in node.MergeSources)
            {
                if (source < 0 || source >= count)
                {
                    throw new InvalidOperationException(
                        $"Merge step {node.Index} references out-of-range source {source}.");
                }

                if (source == node.Index)
                {
                    throw new InvalidOperationException($"Merge step {node.Index} references itself.");
                }
            }

            // Merge sources must be mutually exclusive — every pair must be guarded on a common
            // condition with opposite WhenValue — so at most one can pass. The generator guarantees
            // this; without the check a violation would surface as a baffling double-write.
            for (var a = 0; a < node.MergeSources.Count; a++)
            {
                for (var b = a + 1; b < node.MergeSources.Count; b++)
                {
                    if (!AreExclusive(Nodes[node.MergeSources[a]], Nodes[node.MergeSources[b]]))
                    {
                        throw new InvalidOperationException(
                            $"Merge step {node.Index} sources {node.MergeSources[a]} and {node.MergeSources[b]} "
                            + "are not mutually exclusive (no shared condition with opposite guard values).");
                    }
                }
            }
```

```csharp
    /// <summary>True when two candidate producers can never both run: some condition guards both
    /// with opposite <see cref="Guard.WhenValue"/>s.</summary>
    private static bool AreExclusive(ScenarioNode left, ScenarioNode right)
    {
        foreach (var l in left.Guards)
        {
            foreach (var r in right.Guards)
            {
                if (l.ConditionIndex == r.ConditionIndex && l.WhenValue != r.WhenValue)
                {
                    return true;
                }
            }
        }

        return false;
    }
```

In `HasCycle`, walk merge sources as well as dependencies:

```csharp
    private bool HasCycle(int index, int[] state)
    {
        state[index] = 1;
        foreach (var edge in Nodes[index].DependsOn.Concat(Nodes[index].MergeSources))
        {
            if (state[edge] == 1)
            {
                return true;
            }

            if (state[edge] == 0 && HasCycle(edge, state))
            {
                return true;
            }
        }

        state[index] = 2;
        return false;
    }
```

(Add `using System.Linq;` if the file does not already have implicit usings covering it — `Raun.csproj` uses implicit usings, so no change is expected.)

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test test/Raun.Test/Raun.Test.csproj --filter "FullyQualifiedName~ModelTests"`
Expected: PASS (all pre-existing ModelTests plus the 7 new ones).

- [ ] **Step 8: Build the whole solution**

Run: `dotnet build Raun.slnx`
Expected: 0 warnings, 0 errors. `MtpReportSink.MapState` and `HtmlReportModelBuilder.StatusText` still compile because both switch on `_ =>` / throw for unknown values — `NotTaken` is handled in Task 5.

- [ ] **Step 9: Commit**

```bash
jj commit -m "feat(model): guards, merge sources, IsSynthetic, and StepStatus.NotTaken"
```

---

### Task 2: Scheduler — guard resolution, merge readiness, NotTaken propagation

**Files:**
- Modify: `src/Raun/Scheduling/ScenarioScheduler.cs` (two-phase loop at lines 66–144; `ApplySkipAsync` at line 188)
- Test: `test/Raun.Test/SchedulerTests.cs`

**Interfaces:**
- Consumes: `Guard`, `ScenarioNode.Guards`/`MergeSources`/`EvaluateCondition`, `StepStatus.NotTaken` (Task 1).
- Produces: scheduler behaviour only — no new public API. `ApplySkipAsync(int, string)` becomes `ApplyTerminalAsync(int, StepStatus, string)` (a private local function).

Rules to implement (from the spec):

| Situation | Result |
|---|---|
| guard condition `Pending`/`Running` | unresolved — wait |
| guard condition `Passed`, `EvaluateCondition(output) != WhenValue` | node → `NotTaken`, reason `not taken: {condition op name}` |
| guard condition `Failed`/`Skipped`/`NotTaken` | node → `Skipped` (cascade, not a decision) |
| dependency `NotTaken` and no `Failed`/`Skipped` dependency | node → `NotTaken`, reason `not taken: {names}` |
| merge: all sources terminal, exactly one `Passed` | merge `Passed`, output = that source's output |
| merge: all sources `NotTaken` | merge `NotTaken` |
| merge: any source `Failed`/`Skipped` | merge `Skipped`, cascading the reason |

`NotTaken` gets zero duration in simulated time, exactly like `Skipped`.

- [ ] **Step 1: Write the failing tests** — append to `test/Raun.Test/SchedulerTests.cs`. Add these helpers next to the existing `Node`/`Def`/`Pass`:

```csharp
    private static ScenarioNode Cond(int index, bool value, int[]? dependsOn = null) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "Given",
        OperationName = $"Cond{index}",
        DisplayNameTemplate = $"cond {index}",
        DependsOn = dependsOn ?? [],
        Invoke = (_, _) => Task.FromResult<object?>(value),
        EvaluateCondition = static o => (bool)o!,
    };

    private static ScenarioNode ThrowingCond(int index, int[]? dependsOn = null) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "Given",
        OperationName = $"Cond{index}",
        DisplayNameTemplate = $"cond {index}",
        DependsOn = dependsOn ?? [],
        Invoke = (_, _) => throw new InvalidOperationException("boom"),
        EvaluateCondition = static o => (bool)o!,
    };

    private static ScenarioNode Arm(
        int index,
        Guard[] guards,
        Func<IStepInputs, ScenarioContext, Task<object?>> invoke,
        params int[] dependsOn) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "When",
        OperationName = $"Op{index}",
        DisplayNameTemplate = $"op {index}",
        DependsOn = dependsOn,
        Guards = guards,
        Invoke = invoke,
    };

    private static ScenarioNode MergeNode(int index, params int[] sources) => new()
    {
        Index = index,
        StepId = $"step-{index}",
        Phase = "When",
        OperationName = "Merge",
        DisplayNameTemplate = "«merge»",
        DependsOn = [],
        MergeSources = sources,
        IsSynthetic = true,
        Invoke = (_, _) => Task.FromResult<object?>(null),
    };
```

```csharp
    [Fact]
    public async Task True_condition_runs_the_if_arm_and_leaves_the_else_arm_not_taken()
    {
        var ifRan = false;
        var elseRan = false;
        var def = Def(
            Cond(0, true),
            Arm(1, [new Guard(0, true)], (_, _) => { ifRan = true; return Task.FromResult<object?>(null); }, 0),
            Arm(2, [new Guard(0, false)], (_, _) => { elseRan = true; return Task.FromResult<object?>(null); }, 0));

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.True(ifRan);
        Assert.False(elseRan);
        Assert.Equal(StepStatus.Passed, results[1].Status);
        Assert.Equal(StepStatus.NotTaken, results[2].Status);
        Assert.Contains("not taken", results[2].SkipReason);
    }

    [Fact]
    public async Task False_condition_runs_the_else_arm()
    {
        var def = Def(
            Cond(0, false),
            Arm(1, [new Guard(0, true)], Pass(), 0),
            Arm(2, [new Guard(0, false)], Pass(), 0));

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.Equal(StepStatus.NotTaken, results[1].Status);
        Assert.Equal(StepStatus.Passed, results[2].Status);
    }

    [Fact]
    public async Task Nested_guards_all_must_hold()
    {
        // Guarded on cond0 == true AND cond1 == false; cond1 is true, so the node is not taken.
        var def = Def(
            Cond(0, true),
            Cond(1, true, [0]),
            Arm(2, [new Guard(0, true), new Guard(1, false)], Pass(), 1));

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.Equal(StepStatus.NotTaken, results[2].Status);
    }

    [Fact]
    public async Task Condition_that_throws_skips_both_arms_rather_than_marking_them_not_taken()
    {
        // Load-bearing: a blown-up condition chose no branch. Reporting an arm as "not taken" would
        // disguise a failure as a routine decision.
        var def = Def(
            ThrowingCond(0),
            Arm(1, [new Guard(0, true)], Pass(), 0),
            Arm(2, [new Guard(0, false)], Pass(), 0));

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.Equal(StepStatus.Failed, results[0].Status);
        Assert.Equal(StepStatus.Skipped, results[1].Status);
        Assert.Equal(StepStatus.Skipped, results[2].Status);
        Assert.Contains("dependency failed", results[1].SkipReason);
    }

    [Fact]
    public async Task Merge_passes_with_the_output_of_the_single_passing_source()
    {
        var def = Def(
            Cond(0, false),
            Arm(1, [new Guard(0, true)], Pass("if-value"), 0),
            Arm(2, [new Guard(0, false)], Pass("else-value"), 0),
            MergeNode(3, 1, 2),
            new ScenarioNode
            {
                Index = 4,
                StepId = "step-4",
                Phase = "Then",
                OperationName = "Consume",
                DisplayNameTemplate = "consume",
                DependsOn = [3],
                Invoke = (inputs, _) => Task.FromResult<object?>(inputs.Get<string>(3)),
            });

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.Equal(StepStatus.Passed, results[3].Status);
        Assert.Equal(StepStatus.Passed, results[4].Status);
    }

    [Fact]
    public async Task Merge_is_not_taken_when_every_source_is_not_taken()
    {
        // Both arms sit inside an outer branch that was not taken.
        var def = Def(
            Cond(0, false),
            Cond(1, true, [0]) with { Guards = [new Guard(0, true)] },
            Arm(2, [new Guard(0, true), new Guard(1, true)], Pass("a"), 1),
            Arm(3, [new Guard(0, true), new Guard(1, false)], Pass("b"), 1),
            MergeNode(4, 2, 3));

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.Equal(StepStatus.NotTaken, results[4].Status);
    }

    [Fact]
    public async Task Merge_is_skipped_when_a_source_failed()
    {
        var def = Def(
            Cond(0, true),
            Arm(1, [new Guard(0, true)], (_, _) => throw new InvalidOperationException("boom"), 0),
            Arm(2, [new Guard(0, false)], Pass("b"), 0),
            MergeNode(3, 1, 2));

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.Equal(StepStatus.Failed, results[1].Status);
        Assert.Equal(StepStatus.Skipped, results[3].Status);
        Assert.Contains("Op1", results[3].SkipReason);
    }

    [Fact]
    public async Task Single_source_merge_passes_the_parent_definition_through()
    {
        // The bare-`if` pass-through: a one-source merge is an alias for that source.
        var def = Def(
            Node(0, Pass("parent")),
            MergeNode(1, 0),
            new ScenarioNode
            {
                Index = 2,
                StepId = "step-2",
                Phase = "Then",
                OperationName = "Consume",
                DisplayNameTemplate = "consume",
                DependsOn = [1],
                Invoke = (inputs, _) => Task.FromResult<object?>(inputs.Get<string>(1)),
            });

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.Equal(StepStatus.Passed, results[1].Status);
        Assert.Equal(StepStatus.Passed, results[2].Status);
    }

    [Fact]
    public async Task Dependent_of_a_not_taken_node_is_not_taken_not_skipped()
    {
        var def = Def(
            Cond(0, false),
            Arm(1, [new Guard(0, true)], Pass(), 0),
            Node(2, Pass(), [1]));

        var results = await WithTimeout(new ScenarioScheduler().RunAsync(def));

        Assert.Equal(StepStatus.NotTaken, results[1].Status);
        Assert.Equal(StepStatus.NotTaken, results[2].Status);
        Assert.Contains("not taken", results[2].SkipReason);
    }

    [Fact]
    public async Task Not_taken_step_carries_started_at_and_zero_duration()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var def = Def(
            Cond(0, false),
            Arm(1, [new Guard(0, true)], Pass(), 0));

        var results = await WithTimeout(new ScenarioScheduler(timeProvider: clock).RunAsync(def));

        Assert.Equal(StepStatus.NotTaken, results[1].Status);
        Assert.NotEqual(default, results[1].StartedAt);
        Assert.Equal(TimeSpan.Zero, results[1].Duration);
    }

    [Fact]
    public async Task Not_taken_nodes_do_not_raise_a_step_starting_callback()
    {
        // A branch that was never chosen never "started"; the MTP sink relies on this so it can leave
        // the node in its discovered state instead of stranding it InProgress.
        var observer = new RecordingObserver();
        var def = Def(
            Cond(0, false),
            Arm(1, [new Guard(0, true)], Pass(), 0));

        await WithTimeout(new ScenarioScheduler().RunAsync(def, observer: observer));

        Assert.Single(observer.Started);
        Assert.Equal(2, observer.Finished.Count);
        Assert.Contains(observer.Finished, r => r.Status == StepStatus.NotTaken);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Raun.Test/Raun.Test.csproj --filter "FullyQualifiedName~SchedulerTests"`
Expected: FAIL — guarded nodes never launch (their guards are ignored, so both arms run) and merge nodes stall the scheduler ("Scenario scheduler stalled with unresolved steps").

- [ ] **Step 3: Rewrite phase 1 (skip/guard/merge resolution)** in `RunAsync`. Replace the body of the `foreach (var i in pending.ToArray())` loop under comment `// 1. Resolve skips:` with:

```csharp
                if (cancellationToken.IsCancellationRequested)
                {
                    await ApplyTerminalAsync(i, StepStatus.Skipped, "scenario canceled").ConfigureAwait(false);
                    progressed = true;
                    continue;
                }

                var node = nodes[i];

                // 1a. Merge (phi) nodes: any-of over mutually exclusive sources.
                if (node.MergeSources.Count > 0)
                {
                    if (TryResolveMerge(node, out var mergeStatus, out var mergeReason, out var mergeOutput))
                    {
                        if (mergeStatus == StepStatus.Passed)
                        {
                            pending.Remove(i);
                            outputs[i] = mergeOutput;
                            status[i] = StepStatus.Passed;
                            await ApplyMergePassAsync(i).ConfigureAwait(false);
                        }
                        else
                        {
                            await ApplyTerminalAsync(i, mergeStatus, mergeReason!).ConfigureAwait(false);
                        }

                        progressed = true;
                    }

                    continue;
                }

                var anyUnresolved = false;
                List<string>? failed = null;
                List<string>? skipped = null;
                List<string>? notTaken = null;

                foreach (var dep in node.DependsOn)
                {
                    switch (status[dep])
                    {
                        case StepStatus.Pending:
                        case StepStatus.Running:
                            anyUnresolved = true;
                            break;
                        case StepStatus.Failed:
                            (failed ??= []).Add(nodes[dep].OperationName);
                            break;
                        case StepStatus.Skipped:
                            (skipped ??= []).Add(nodes[dep].OperationName);
                            break;
                        case StepStatus.NotTaken:
                            (notTaken ??= []).Add(nodes[dep].OperationName);
                            break;
                    }
                }

                // 1b. Guards. A resolved-false guard is a decision (NotTaken); a guard whose condition
                // failed/was skipped/was itself not taken is a cascade (Skipped) — no branch was chosen.
                var guardNotTaken = (string?)null;
                foreach (var guard in node.Guards)
                {
                    switch (status[guard.ConditionIndex])
                    {
                        case StepStatus.Pending:
                        case StepStatus.Running:
                            anyUnresolved = true;
                            break;
                        case StepStatus.Passed:
                            if (EvaluateGuard(nodes[guard.ConditionIndex], outputs[guard.ConditionIndex]) != guard.WhenValue)
                            {
                                guardNotTaken ??= nodes[guard.ConditionIndex].OperationName;
                            }

                            break;
                        default: // Failed / Skipped / NotTaken
                            (skipped ??= []).Add(nodes[guard.ConditionIndex].OperationName);
                            break;
                    }
                }

                if (anyUnresolved)
                {
                    continue;
                }

                if (failed is not null || skipped is not null)
                {
                    await ApplyTerminalAsync(i, StepStatus.Skipped, BuildSkipReason(failed, skipped)).ConfigureAwait(false);
                    progressed = true;
                }
                else if (guardNotTaken is not null)
                {
                    await ApplyTerminalAsync(i, StepStatus.NotTaken, $"not taken: {guardNotTaken}").ConfigureAwait(false);
                    progressed = true;
                }
                else if (notTaken is not null)
                {
                    await ApplyTerminalAsync(
                        i, StepStatus.NotTaken, $"not taken: {string.Join(", ", notTaken)}").ConfigureAwait(false);
                    progressed = true;
                }
```

Note the ordering: `Failed`/`Skipped` beats `NotTaken`, which beats launching. A guard that resolved false wins over a merely `NotTaken` dependency because it names the actual condition.

- [ ] **Step 4: Gate phase 2 on guards.** In the launch loop, replace the readiness test:

```csharp
                    if (node.DependsOn.All(d => status[d] == StepStatus.Passed)
                        && node.Guards.All(g => status[g.ConditionIndex] == StepStatus.Passed
                            && EvaluateGuard(nodes[g.ConditionIndex], outputs[g.ConditionIndex]) == g.WhenValue))
```

Also guard the loop against merge nodes so they are never invoked — add immediately after `var node = nodes[i];` inside the launch loop:

```csharp
                    if (node.MergeSources.Count > 0)
                    {
                        continue; // resolved in phase 1, never invoked
                    }
```

- [ ] **Step 5: Replace `ApplySkipAsync` with `ApplyTerminalAsync`, and add the merge helpers** (all local functions inside `RunAsync`, alongside the existing one). `ApplyTerminalAsync` is the old body with the status threaded through, and it skips the *starting* callback for `NotTaken`.

```csharp
        async Task ApplyTerminalAsync(int i, StepStatus terminal, string reason)
        {
            pending.Remove(i);
            status[i] = terminal;
            var node = nodes[i];
            var name = FormatName(node, inputs);

            // A not-taken branch never started, so no observer sees it start — that is what lets a
            // reporter leave the node in its discovered state instead of stranding it "in progress".
            if (observer is not null && terminal != StepStatus.NotTaken)
            {
                await observer.OnStepStartingAsync(
                    new StepContext { Node = node, DisplayName = name })
                    .ConfigureAwait(false);
            }

            var startedAt = _timeProvider.GetUtcNow();
            if (_simulatedTime)
            {
                var startOffset = StartOffset(node, simFinishOffset!);
                simStartOffset![i] = startOffset;
                simFinishOffset![i] = startOffset; // skipped and not-taken steps have zero duration
                startedAt = simBase + startOffset;
            }

            var result = new StepResult
            {
                Node = node,
                DisplayName = name,
                Status = terminal,
                StartedAt = startedAt,
                SkipReason = reason,
            };
            results[i] = result;
            if (observer is not null)
            {
                await observer.OnStepFinishedAsync(result).ConfigureAwait(false);
            }
        }

        // A merge resolves once ALL its sources are terminal: exactly one Passed => pass with that
        // source's output; every source NotTaken => NotTaken; any Failed/Skipped => Skipped, cascading.
        bool TryResolveMerge(ScenarioNode node, out StepStatus resolved, out string? reason, out object? output)
        {
            resolved = StepStatus.Passed;
            reason = null;
            output = null;
            var passedIndex = -1;
            List<string>? bad = null;

            foreach (var source in node.MergeSources)
            {
                switch (status[source])
                {
                    case StepStatus.Pending:
                    case StepStatus.Running:
                        return false;
                    case StepStatus.Passed:
                        passedIndex = source;
                        break;
                    case StepStatus.NotTaken:
                        break;
                    default: // Failed / Skipped
                        (bad ??= []).Add(nodes[source].OperationName);
                        break;
                }
            }

            if (bad is not null)
            {
                resolved = StepStatus.Skipped;
                reason = $"dependency failed: {string.Join(", ", bad)}";
                return true;
            }

            if (passedIndex < 0)
            {
                resolved = StepStatus.NotTaken;
                reason = "not taken: no branch produced a value";
                return true;
            }

            output = outputs[passedIndex];
            return true;
        }

        async Task ApplyMergePassAsync(int i)
        {
            var node = nodes[i];
            var name = FormatName(node, inputs);
            var startedAt = _timeProvider.GetUtcNow();
            if (_simulatedTime)
            {
                var startOffset = MergeStartOffset(node, simFinishOffset!);
                simStartOffset![i] = startOffset;
                simFinishOffset![i] = startOffset; // a merge is instantaneous
                startedAt = simBase + startOffset;
            }

            var result = new StepResult
            {
                Node = node,
                DisplayName = name,
                Status = StepStatus.Passed,
                StartedAt = startedAt,
            };
            results[i] = result;
            if (observer is not null)
            {
                await observer.OnStepFinishedAsync(result).ConfigureAwait(false);
            }
        }
```

Add the two static helpers next to `StartOffset`:

```csharp
    /// <summary>Coerces a condition node's boxed output to its branch value using the generator-emitted
    /// <see cref="ScenarioNode.EvaluateCondition"/>. <see cref="ScenarioDefinition.Validate"/> has
    /// already proven it is non-null for every guarded condition.</summary>
    private static bool EvaluateGuard(ScenarioNode condition, object? output)
        => condition.EvaluateCondition!(output);

    /// <summary>A merge's simulated start offset: the MAX of its sources' finish offsets (it has no
    /// DependsOn edges of its own).</summary>
    private static TimeSpan MergeStartOffset(ScenarioNode node, TimeSpan[] finishOffsets)
    {
        var offset = TimeSpan.Zero;
        foreach (var source in node.MergeSources)
        {
            if (finishOffsets[source] > offset)
            {
                offset = finishOffsets[source];
            }
        }

        return offset;
    }
```

Finally, replace the two remaining `ApplySkipAsync(...)` call sites (the cancellation one and the dependency-skip one) with `ApplyTerminalAsync(i, StepStatus.Skipped, ...)` — Step 3 already does this; verify no `ApplySkipAsync` reference remains.

- [ ] **Step 6: Run the scheduler tests**

Run: `dotnet test test/Raun.Test/Raun.Test.csproj --filter "FullyQualifiedName~SchedulerTests"`
Expected: PASS — all pre-existing tests plus the 11 new ones.

- [ ] **Step 7: Run the full runtime test project** (simulated-time tests share the offset bookkeeping just touched)

Run: `dotnet test test/Raun.Test/Raun.Test.csproj`
Expected: PASS.

- [ ] **Step 8: Full solution green**

Run: `dotnet build Raun.slnx` then `dotnet test Raun.slnx`
Expected: 0 warnings; all tests pass.

- [ ] **Step 9: Commit**

```bash
jj commit -m "feat(scheduler): guard resolution, merge nodes, and NotTaken propagation"
```

---

### Task 3: Generator lowering — guard stack, definition map, phi insertion, emit

**Files:**
- Modify: `src/Raun.Generator/Lowering/Ir.cs`, `src/Raun.Generator/Lowering/Binding.cs`, `src/Raun.Generator/Lowering/ScenarioParser.cs`, `src/Raun.Generator/Emit/ScenarioEmitter.cs`
- Modify: `test/Raun.Generator.Test/SampleSources.cs`
- Create: `test/Raun.Generator.Test/ConditionalLoweringTests.cs`

**Interfaces:**
- Consumes: `Guard`, `ScenarioNode.Guards`/`MergeSources`/`IsSynthetic`/`EvaluateCondition` (Task 1); scheduler semantics (Task 2).
- Produces:
  - `ParsedStep.Guards` → `IReadOnlyList<ParsedGuard>` where `internal readonly record struct ParsedGuard(int ConditionIndex, bool WhenValue)`.
  - `ParsedStep.MergeSources` → `IReadOnlyList<int>`; `ParsedStep.IsSynthetic` → `bool`; `ParsedStep.ConditionCoercionType` → `string?` (fully-qualified result type of a node used as a condition).
  - `Binding.FromAssignment` returns `Binding.Single(name)` for a bare identifier on the left.

Lowering rules:
- `if (await Given.C(...)) { ... } else { ... }` — lower the condition as an ordinary step (it may also bind a local); set its `ConditionCoercionType` to its `ResultTypeFqn`; push `ParsedGuard(condIndex, true)`, walk the then-arm, pop; same with `false` for the else-arm (`else if` is just a nested `if` in the else-arm).
- Inside an arm the source-order frontier starts at the condition node, so intra-arm ordering is preserved; **arm nodes never enter a following statement's `DependsOn`** (that would make an all-of dependency on a branch that may not run). After the `if`, the frontier is the inserted merge nodes if any, else `[condIndex]`.
- Definition map: `_vars` is snapshotted before each arm and restored after, so each arm gets a child map. Diffing the two child maps against the parent yields phi insertion:

| Diff result | Action |
|---|---|
| same definition in both arms | nothing |
| different definition in each arm | merge node over the two arm definitions |
| defined in one arm only, exists in parent | merge node over the arm definition and a **pass-through** node (synthetic, guarded on the opposite value, `MergeSources = [parentDef]`) |
| defined in one arm only, new local | drop (C# scoping already forbids the use) |

- [ ] **Step 1: Add the conditional DSL and scenarios to `test/Raun.Generator.Test/SampleSources.cs`**

```csharp
    // A DSL with condition steps: an awaited phase-marker call whose result is usable as a C#
    // condition. `IsPriority` returns bool; `HasCapacity` returns a type with `operator true`, proving
    // the generator emits the coercion rather than the scheduler unboxing to bool.
    public const string ConditionalDsl =
        """
        using System.Threading.Tasks;
        using Raun;

        namespace CondDemo;

        public sealed record Patient(string Name);
        public sealed record Appointment(string Kind);

        public readonly struct Capacity
        {
            public Capacity(bool value) => Value = value;
            public bool Value { get; }
            public static bool operator true(Capacity c) => c.Value;
            public static bool operator false(Capacity c) => !c.Value;
        }

        public static class CondDsl
        {
            extension(Given)
            {
                [StepName("patient {name} exists")]
                public static async Task<Patient> PatientExists(string name)
                {
                    await Task.Yield();
                    return new Patient(name);
                }

                [StepName("the patient is priority")]
                public static async Task<bool> IsPriority()
                {
                    await Task.Yield();
                    return true;
                }

                [StepName("the clinic has capacity")]
                public static async Task<Capacity> HasCapacity()
                {
                    await Task.Yield();
                    return new Capacity(true);
                }
            }

            extension(When)
            {
                [StepName("creating an urgent appointment")]
                public static async Task<Appointment> CreateUrgent(Patient patient)
                {
                    await Task.Yield();
                    return new Appointment("urgent");
                }

                [StepName("creating a standard appointment")]
                public static async Task<Appointment> CreateStandard(Patient patient)
                {
                    await Task.Yield();
                    return new Appointment("standard");
                }

                [StepName("notifying the patient")]
                public static Task Notify(Patient patient) => Task.CompletedTask;
            }

            extension(Then)
            {
                [StepName("the appointment should exist")]
                public static Task AppointmentExists(Appointment appointment) => Task.CompletedTask;
            }
        }
        """;

    // if/else, both arms defining `appointment` => a phi at the closing brace.
    public const string IfElseScenario =
        """

        public static class IfElseScenarios
        {
            [Scenario("priority routing")]
            public static async Task Routing()
            {
                var patient = await Given.PatientExists("Jane");

                Appointment appointment;
                if (await Given.IsPriority())
                    appointment = await When.CreateUrgent(patient);
                else
                    appointment = await When.CreateStandard(patient);

                await Then.AppointmentExists(appointment);
            }
        }
        """;

    // A bare `if` with no else and no assignment: the arm's step is simply guarded.
    public const string BareIfScenario =
        """

        public static class BareIfScenarios
        {
            [Scenario("notify priority patients")]
            public static async Task Notify()
            {
                var patient = await Given.PatientExists("Jane");

                if (await Given.IsPriority())
                    await When.Notify(patient);

                await Then.AppointmentExists(await-less-placeholder);
            }
        }
        """;

    // A bare `if` that conditionally OVERWRITES a local defined before the branch: the merge takes the
    // arm's definition and a synthetic pass-through of the parent definition.
    public const string ConditionalOverwriteScenario =
        """

        public static class OverwriteScenarios
        {
            [Scenario("upgrade to urgent when priority")]
            public static async Task Upgrade()
            {
                var patient = await Given.PatientExists("Jane");
                var appointment = await When.CreateStandard(patient);

                if (await Given.IsPriority())
                    appointment = await When.CreateUrgent(patient);

                await Then.AppointmentExists(appointment);
            }
        }
        """;

    // Nested ifs: the inner arm carries BOTH guards.
    public const string NestedIfScenario =
        """

        public static class NestedIfScenarios
        {
            [Scenario("nested routing")]
            public static async Task Routing()
            {
                var patient = await Given.PatientExists("Jane");

                if (await Given.IsPriority())
                {
                    if (await Given.HasCapacity())
                        await When.Notify(patient);
                }
            }
        }
        """;

    // A condition whose result type is not bool but defines `operator true`.
    public const string OperatorTrueScenario =
        """

        public static class OperatorTrueScenarios
        {
            [Scenario("capacity routing")]
            public static async Task Routing()
            {
                var patient = await Given.PatientExists("Jane");

                if (await Given.HasCapacity())
                    await When.Notify(patient);
            }
        }
        """;
```

Fix `BareIfScenario` before running anything — it must be a compiling scenario body. Use exactly:

```csharp
    public const string BareIfScenario =
        """

        public static class BareIfScenarios
        {
            [Scenario("notify priority patients")]
            public static async Task Notify()
            {
                var patient = await Given.PatientExists("Jane");

                if (await Given.IsPriority())
                    await When.Notify(patient);
            }
        }
        """;
```

- [ ] **Step 2: Write the failing tests** — create `test/Raun.Generator.Test/ConditionalLoweringTests.cs`

```csharp
using Raun.Model;
using Xunit;

namespace Raun.Generator.Test;

/// <summary>
/// `if`/`else` lowers into guarded nodes plus synthetic merge (phi) nodes. `DependsOn` keeps its
/// all-of meaning throughout: a following statement never depends on an arm's node, only on the
/// condition or on a merge.
/// </summary>
public class ConditionalLoweringTests
{
    private static ScenarioDefinition Lower(string scenario)
    {
        var result = GeneratorHarness.Run(SampleSources.ConditionalDsl + scenario);
        result.AssertCompiles();
        return Assert.Single(result.Definitions());
    }

    [Fact]
    public void If_else_lowers_condition_arms_and_a_merge()
    {
        var def = Lower(SampleSources.IfElseScenario);

        // 0 PatientExists, 1 IsPriority (condition), 2 CreateUrgent, 3 CreateStandard,
        // 4 «merge appointment», 5 AppointmentExists
        Assert.Equal(6, def.Nodes.Count);

        Assert.NotNull(def.Nodes[1].EvaluateCondition);
        Assert.Equal([new Guard(1, true)], def.Nodes[2].Guards);
        Assert.Equal([new Guard(1, false)], def.Nodes[3].Guards);

        Assert.True(def.Nodes[4].IsSynthetic);
        Assert.Equal([2, 3], def.Nodes[4].MergeSources);
        Assert.Empty(def.Nodes[4].DependsOn);

        // The consumer joins on the merge, never on an arm.
        Assert.Equal([4], def.Nodes[5].DependsOn);
        Assert.Empty(def.Nodes[5].Guards);
    }

    [Fact]
    public void Condition_node_is_an_ordinary_discoverable_step()
    {
        var def = Lower(SampleSources.IfElseScenario);

        Assert.False(def.Nodes[1].IsSynthetic);
        Assert.Equal("IsPriority", def.Nodes[1].OperationName);
        Assert.Equal("the patient is priority", def.Nodes[1].DisplayNameTemplate);
        Assert.Equal("Given", def.Nodes[1].Phase);
    }

    [Fact]
    public void Bare_if_guards_the_arm_and_inserts_no_merge()
    {
        var def = Lower(SampleSources.BareIfScenario);

        // 0 PatientExists, 1 IsPriority, 2 Notify — nothing is assigned, so there is no phi.
        Assert.Equal(3, def.Nodes.Count);
        Assert.Equal([new Guard(1, true)], def.Nodes[2].Guards);
        Assert.DoesNotContain(def.Nodes, n => n.IsSynthetic);
    }

    [Fact]
    public void Conditional_overwrite_merges_the_arm_against_a_pass_through_of_the_parent()
    {
        var def = Lower(SampleSources.ConditionalOverwriteScenario);

        // 0 PatientExists, 1 CreateStandard (parent def), 2 IsPriority, 3 CreateUrgent (arm),
        // 4 pass-through of 1 guarded false, 5 «merge appointment», 6 AppointmentExists
        Assert.Equal(7, def.Nodes.Count);

        Assert.Equal([new Guard(2, true)], def.Nodes[3].Guards);

        Assert.True(def.Nodes[4].IsSynthetic);
        Assert.Equal([new Guard(2, false)], def.Nodes[4].Guards);
        Assert.Equal([1], def.Nodes[4].MergeSources);

        Assert.True(def.Nodes[5].IsSynthetic);
        Assert.Equal([3, 4], def.Nodes[5].MergeSources);
        Assert.Equal([5], def.Nodes[6].DependsOn);
    }

    [Fact]
    public void Nested_ifs_stack_guards()
    {
        var def = Lower(SampleSources.NestedIfScenario);

        // 0 PatientExists, 1 IsPriority, 2 HasCapacity, 3 Notify
        Assert.Equal([new Guard(1, true)], def.Nodes[2].Guards);
        Assert.Equal([new Guard(1, true), new Guard(2, true)], def.Nodes[3].Guards);
    }

    [Fact]
    public void Operator_true_condition_type_is_coerced_by_generated_code()
    {
        var def = Lower(SampleSources.OperatorTrueScenario);

        Assert.NotNull(def.Nodes[1].EvaluateCondition);
        Assert.True(def.Nodes[1].EvaluateCondition!(NewCapacity(true)));
        Assert.False(def.Nodes[1].EvaluateCondition!(NewCapacity(false)));

        static object NewCapacity(bool value)
        {
            // The Capacity struct lives in the generated compilation, so build it reflectively from
            // the type the condition node's own output carries.
            var type = typeof(object).Assembly is null ? null : null;
            return CapacityFactory.Create(value);
        }
    }

    [Fact]
    public async Task If_arm_runs_and_else_arm_is_not_taken_end_to_end()
    {
        var result = GeneratorHarness.Run(SampleSources.ConditionalDsl + SampleSources.IfElseScenario);
        result.AssertCompiles();

        var results = await result.Definitions().Single().RunAsync();

        Assert.Equal(StepStatus.Passed, results[2].Status);      // CreateUrgent (IsPriority == true)
        Assert.Equal(StepStatus.NotTaken, results[3].Status);    // CreateStandard
        Assert.Equal(StepStatus.Passed, results[4].Status);      // merge
        Assert.Equal(StepStatus.Passed, results[5].Status);      // AppointmentExists
    }

    [Fact]
    public async Task Conditional_overwrite_executes_and_the_consumer_sees_the_arm_value()
    {
        var result = GeneratorHarness.Run(
            SampleSources.ConditionalDsl + SampleSources.ConditionalOverwriteScenario);
        result.AssertCompiles();

        var results = await result.Definitions().Single().RunAsync();

        Assert.All(results, r => Assert.True(
            r.Status is StepStatus.Passed or StepStatus.NotTaken,
            $"step {r.Node.Index} was {r.Status}: {r.SkipReason}{r.Exception}"));
        Assert.Equal(StepStatus.NotTaken, results[4].Status);   // pass-through (condition was true)
        Assert.Equal(StepStatus.Passed, results[5].Status);     // merge took the arm value
    }
}
```

Replace the placeholder inside `Operator_true_condition_type_is_coerced_by_generated_code` with a version that does not need a factory — the emitted coercion is exercised end-to-end instead:

```csharp
    [Fact]
    public async Task Operator_true_condition_gates_the_arm_end_to_end()
    {
        // Capacity is not bool; it defines `operator true`. The generator emits
        // `static o => ((Capacity)o!) ? true : false`, so Roslyn — not the scheduler — resolves it.
        var result = GeneratorHarness.Run(
            SampleSources.ConditionalDsl + SampleSources.OperatorTrueScenario);
        result.AssertCompiles();

        var results = await result.Definitions().Single().RunAsync();

        Assert.Equal(StepStatus.Passed, results[1].Status);   // HasCapacity
        Assert.Equal(StepStatus.Passed, results[2].Status);   // Notify ran: the guard held
    }
```

- [ ] **Step 3: Run the new tests to verify they fail**

Run: `dotnet test test/Raun.Generator.Test/Raun.Generator.Test.csproj --filter "FullyQualifiedName~ConditionalLoweringTests"`
Expected: FAIL — `ScenarioParser.ParseStatement` returns `false` for `IfStatementSyntax`, so no scenario is emitted and `Assert.Single(result.Definitions())` throws.

- [ ] **Step 4: Extend the IR** in `src/Raun.Generator/Lowering/Ir.cs`

```csharp
/// <summary>A lowered branch guard: the node runs only when node <see cref="ConditionIndex"/> passed
/// and its condition evaluates to <see cref="WhenValue"/>. Mirrors <c>Raun.Model.Guard</c>.</summary>
internal readonly record struct ParsedGuard(int ConditionIndex, bool WhenValue);
```

and on `ParsedStep`:

```csharp
    /// <summary>Branch guards gating this step; all must hold. Empty for an unconditional step.</summary>
    public IReadOnlyList<ParsedGuard> Guards { get; init; } = [];

    /// <summary>Mutually exclusive candidate producers for a merge (phi) node, or the single source of
    /// a pass-through alias. Empty for an ordinary step.</summary>
    public IReadOnlyList<int> MergeSources { get; init; } = [];

    /// <summary>True for generator plumbing (merge/pass-through) rather than a business step.</summary>
    public bool IsSynthetic { get; init; }

    /// <summary>When this step is used as an <c>if</c> condition, its fully-qualified result type — the
    /// cast target in the emitted <c>EvaluateCondition</c> coercion. Null otherwise.</summary>
    public string? ConditionCoercionType { get; init; }
```

- [ ] **Step 5: Accept re-assignment in `src/Raun.Generator/Lowering/Binding.cs`.** Add, at the top of `FromAssignment` (before the `DeclarationExpressionSyntax` case):

```csharp
        // appointment = await When.X(...)  — re-assignment of an existing step-output local. This is
        // how an `if` arm redefines a local; the parser's definition map turns the two definitions
        // into a phi.
        if (left is IdentifierNameSyntax identifier)
        {
            return Single(identifier.Identifier.Text);
        }
```

- [ ] **Step 6: Teach `ScenarioParser` the `if` walk.** In `src/Raun.Generator/Lowering/ScenarioParser.cs`:

Add a guard-stack field next to `_prevFrontier`:

```csharp
    // Guards accumulated by the enclosing if/else arms; every step created inherits a snapshot.
    private readonly List<ParsedGuard> _guards = [];
```

Extend `ParseStatement`:

```csharp
    private bool ParseStatement(StatementSyntax statement)
    {
        return statement switch
        {
            LocalDeclarationStatementSyntax local => ParseLocalDeclaration(local),
            ExpressionStatementSyntax expr => ParseExpressionStatement(expr),
            IfStatementSyntax ifStatement => ParseIf(ifStatement),
            BlockSyntax block => ParseBlock(block),
            _ => false,
        };
    }

    private bool ParseBlock(BlockSyntax block)
    {
        foreach (var statement in block.Statements)
        {
            if (!ParseStatement(statement))
            {
                return false;
            }
        }

        return true;
    }
```

Add the `if` lowering. `_steps` is append-only and `_nextIndex` monotonic, so an index captured here stays valid.

```csharp
    /// <summary>
    /// Lowers `if (await Given.C(...)) A else B`. The condition is an ordinary node; each arm is
    /// walked with an extra guard pushed. Locals defined differently by the two arms become phi
    /// (merge) nodes at the closing brace — a definition map diff, which is all SSA needs when the
    /// control flow is structured (every merge point IS the closing brace).
    /// </summary>
    private bool ParseIf(IfStatementSyntax statement)
    {
        if (statement.Condition is not AwaitExpressionSyntax { Expression: InvocationExpressionSyntax call })
        {
            return false; // RAUN011
        }

        var condition = BuildStep(call, groupId: null, _prevFrontier);
        if (condition is null || !condition.HasResult)
        {
            return false; // RAUN011: a condition must produce a value
        }

        // The condition node carries the coercion the scheduler calls; do it by replacing the step in
        // place (ParsedStep is a record).
        MarkAsCondition(condition);

        var parentVars = new Dictionary<string, VarSource>(_vars);

        var thenVars = WalkArm(statement.Statement, condition.Index, whenValue: true, parentVars);
        if (thenVars is null)
        {
            return false;
        }

        Dictionary<string, VarSource>? elseVars = null;
        if (statement.Else is { } elseClause)
        {
            elseVars = WalkArm(elseClause.Statement, condition.Index, whenValue: false, parentVars);
            if (elseVars is null)
            {
                return false;
            }
        }

        // Rejoin: start from the parent map, then insert a phi for every local the arms disagree on.
        _vars.Clear();
        foreach (var pair in parentVars)
        {
            _vars[pair.Key] = pair.Value;
        }

        var frontier = new List<int>();
        foreach (var name in DifferingLocals(parentVars, thenVars, elseVars))
        {
            var mergeIndex = InsertMerge(name, condition.Index, parentVars, thenVars, elseVars);
            if (mergeIndex < 0)
            {
                return false;
            }

            frontier.Add(mergeIndex);
        }

        // A following statement must never depend on an arm's node (DependsOn is all-of and an arm may
        // not run); it joins on the merges, or on the condition when there are none.
        _prevFrontier = frontier.Count > 0 ? frontier : [condition.Index];
        return true;
    }

    private void MarkAsCondition(ParsedStep condition)
    {
        var position = _steps.FindIndex(s => s.Index == condition.Index);
        _steps[position] = _steps[position] with { ConditionCoercionType = condition.ResultTypeFqn };
    }

    /// <summary>Walks one arm with <paramref name="whenValue"/> pushed onto the guard stack, on a child
    /// copy of the definition map. Returns that child map, or null when the arm is unsupported.</summary>
    private Dictionary<string, VarSource>? WalkArm(
        StatementSyntax arm, int conditionIndex, bool whenValue, Dictionary<string, VarSource> parentVars)
    {
        var savedFrontier = _prevFrontier;
        _vars.Clear();
        foreach (var pair in parentVars)
        {
            _vars[pair.Key] = pair.Value;
        }

        _guards.Add(new ParsedGuard(conditionIndex, whenValue));
        _prevFrontier = [conditionIndex];
        var ok = ParseStatement(arm);
        _guards.RemoveAt(_guards.Count - 1);
        _prevFrontier = savedFrontier;

        return ok ? new Dictionary<string, VarSource>(_vars) : null;
    }

    /// <summary>Locals whose definition differs between the arms (or between an arm and the parent) —
    /// exactly the set that needs a phi. A local defined only inside one arm and absent from the parent
    /// is branch-local: C# scoping already forbids its later use, so it is dropped.</summary>
    private static IEnumerable<string> DifferingLocals(
        Dictionary<string, VarSource> parentVars,
        Dictionary<string, VarSource> thenVars,
        Dictionary<string, VarSource>? elseVars)
    {
        var names = new SortedSet<string>(System.StringComparer.Ordinal);
        foreach (var name in thenVars.Keys)
        {
            names.Add(name);
        }

        if (elseVars is not null)
        {
            foreach (var name in elseVars.Keys)
            {
                names.Add(name);
            }
        }

        foreach (var name in names)
        {
            var inThen = thenVars.TryGetValue(name, out var thenSource);
            var inElse = elseVars is not null && elseVars.TryGetValue(name, out var elseSource0);
            var inParent = parentVars.TryGetValue(name, out var parentSource);

            if (!inParent && !(inThen && inElse))
            {
                continue; // branch-local
            }

            var thenDef = inThen ? thenSource : parentSource;
            var elseDef = elseVars is not null && elseVars.TryGetValue(name, out var elseSource)
                ? elseSource
                : parentSource;

            if (!thenDef.Equals(elseDef))
            {
                yield return name;
            }
        }
    }

    /// <summary>
    /// Inserts the phi for one local: a synthetic merge over the two arm definitions. When an arm did
    /// not redefine the local, that side is a synthetic PASS-THROUGH node — guarded on the opposite
    /// value, aliasing the parent definition — so the merge's sources stay mutually exclusive (what
    /// <c>ScenarioDefinition.Validate</c> requires) and the parent value flows through when the arm is
    /// not taken. Arrays are not mergeable; returns -1 (the analyzer rejects the shape).
    /// </summary>
    private int InsertMerge(
        string name,
        int conditionIndex,
        Dictionary<string, VarSource> parentVars,
        Dictionary<string, VarSource> thenVars,
        Dictionary<string, VarSource>? elseVars)
    {
        var thenDef = Side(thenVars, whenValue: true);
        var elseDef = Side(elseVars, whenValue: false);
        if (thenDef < 0 || elseDef < 0)
        {
            return -1;
        }

        var producer = _steps.First(s => s.Index == thenDef);
        var index = _nextIndex++;
        var merge = new ParsedStep
        {
            Index = index,
            StepId = GenStableId.ForStep(_scenarioId, "merge:" + name + ":" + index),
            Phase = producer.Phase,
            OperationName = "Merge",
            HasResult = true,
            ResultTypeFqn = producer.ResultTypeFqn,
            InvokeCallText = "",
            DisplayNameTemplate = "«merge " + name + "»",
            MergeSources = [thenDef, elseDef],
            IsSynthetic = true,
            Guards = [.. _guards],
            DependsOn = [],
        };

        _steps.Add(merge);
        _vars[name] = VarSource.Scalar(index);
        return index;

        int Side(Dictionary<string, VarSource>? armVars, bool whenValue)
        {
            if (armVars is not null && armVars.TryGetValue(name, out var armSource))
            {
                return armSource.IsArray ? -1 : armSource.Index;
            }

            if (!parentVars.TryGetValue(name, out var parentSource) || parentSource.IsArray)
            {
                return -1;
            }

            return InsertPassThrough(name, conditionIndex, whenValue, parentSource.Index);
        }
    }

    /// <summary>The missing arm of a bare <c>if</c>: a synthetic node guarded on the opposite value that
    /// aliases the parent definition, so the merge sees two mutually exclusive sources.</summary>
    private int InsertPassThrough(string name, int conditionIndex, bool whenValue, int parentDef)
    {
        var producer = _steps.First(s => s.Index == parentDef);
        var index = _nextIndex++;
        var guards = new List<ParsedGuard>(_guards) { new(conditionIndex, !whenValue) };
        _steps.Add(new ParsedStep
        {
            Index = index,
            StepId = GenStableId.ForStep(_scenarioId, "phi:" + name + ":" + index),
            Phase = producer.Phase,
            OperationName = "Unchanged",
            HasResult = true,
            ResultTypeFqn = producer.ResultTypeFqn,
            InvokeCallText = "",
            DisplayNameTemplate = "«" + name + " unchanged»",
            MergeSources = [parentDef],
            IsSynthetic = true,
            Guards = guards,
            DependsOn = [],
        });

        return index;
    }
```

Finally, have `BuildStep` stamp the current guard stack onto every step it creates — add to the `ParsedStep` initializer in `BuildStep`:

```csharp
            Guards = [.. _guards],
```

- [ ] **Step 7: Emit the new members** in `src/Raun.Generator/Emit/ScenarioEmitter.cs`. In `BuildNode`, after the `Set("DependsOn", ...)` line but before `Set("Invoke", ...)`, add conditional members so a scenario with no conditionals emits byte-identical output (existing snapshots must not move):

```csharp
        if (step.Guards.Count > 0)
        {
            members.Add(Set("Guards", GuardArray(step.Guards)));
        }

        if (step.MergeSources.Count > 0)
        {
            members.Add(Set("MergeSources", IntArray(step.MergeSources)));
        }

        if (step.IsSynthetic)
        {
            members.Add(Set("IsSynthetic", LiteralExpression(SyntaxKind.TrueLiteralExpression)));
        }

        if (step.ConditionCoercionType is { } coercionType)
        {
            // static __o => ((T)__o!) ? true : false — Roslyn picks bool / implicit conversion /
            // operator true at COMPILE time, so the scheduler never reflects over the boxed output.
            members.Add(Set("EvaluateCondition", ParseExpression(
                $"static __o => (({coercionType})__o!) ? true : false")));
        }
```

`Set("Invoke", ...)` must stay in the list for every node (the property is `required`). For a synthetic node `InvokeCallText` is empty, so give it a no-op body — in `BuildInvokeLambda`, at the very top:

```csharp
        if (step.IsSynthetic)
        {
            // A merge/pass-through never runs: the scheduler resolves it from its sources. The
            // delegate exists only to satisfy the required member.
            return ParenthesizedLambdaExpression()
                .WithModifiers(TokenList(Token(SyntaxKind.StaticKeyword)))
                .WithParameterList(ParameterList(SeparatedList(new[]
                {
                    Parameter(Identifier("__inputs")),
                    Parameter(Identifier("__ctx")),
                })))
                .WithExpressionBody(ParseExpression(
                    "global::System.Threading.Tasks.Task.FromResult<object?>(null)"));
        }
```

Add the guard-array helper next to `IntArray`:

```csharp
    /// <summary>Builds <c>new global::Raun.Model.Guard[] { new(i, true), … }</c>.</summary>
    private static ArrayCreationExpressionSyntax GuardArray(IEnumerable<ParsedGuard> guards)
        => ArrayCreationExpression(
            ArrayType(ParseTypeName("global::Raun.Model.Guard"))
                .WithRankSpecifiers(SingletonList(
                    ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(
                        OmittedArraySizeExpression())))))
            .WithInitializer(InitializerExpression(
                SyntaxKind.ArrayInitializerExpression,
                SeparatedList<ExpressionSyntax>(guards.Select(g =>
                    (ExpressionSyntax)ParseExpression(
                        $"new global::Raun.Model.Guard({g.ConditionIndex}, {(g.WhenValue ? "true" : "false")})")))));
```

- [ ] **Step 8: Run the conditional lowering tests**

Run: `dotnet test test/Raun.Generator.Test/Raun.Generator.Test.csproj --filter "FullyQualifiedName~ConditionalLoweringTests"`
Expected: PASS. If a node-index assertion is off by one, print the actual graph (`def.Nodes.Select(n => $"{n.Index} {n.OperationName} guards={n.Guards.Count} merge=[{string.Join(",", n.MergeSources)}]")`) and correct the *test's* expected indices only after confirming the shape matches the spec's worked example — do not weaken the guard/merge assertions.

- [ ] **Step 9: Run the whole generator test project — existing snapshots must not move**

Run: `dotnet test test/Raun.Generator.Test/Raun.Generator.Test.csproj`
Expected: PASS, including all `GeneratorSnapshotTests` with their current `.verified.cs` files (the new node members are emitted only when non-default). If a snapshot moved, the conditional emission in Step 7 is wrong — fix the emitter, do not accept the snapshot.

- [ ] **Step 10: Full solution green**

Run: `dotnet build Raun.slnx` then `dotnet test Raun.slnx`
Expected: 0 warnings; all tests pass. `AnalyzerTests.RAUN003_control_flow` still passes — the analyzer is untouched until Task 4.

- [ ] **Step 11: Commit**

```bash
jj commit -m "feat(generator): lower if/else into guarded nodes and phi merges"
```

---

### Task 4: Analyzer — narrow RAUN003, add RAUN011 and RAUN012

**Files:**
- Modify: `src/Raun.Generator/Analysis/Descriptors.cs`, `src/Raun.Generator/Analysis/ScenarioAnalyzer.cs`, `src/Raun.Generator/AnalyzerReleases.Unshipped.md`
- Test: `test/Raun.Generator.Test/AnalyzerTests.cs`

**Interfaces:**
- Consumes: the supported `if` shape from Task 3.
- Produces: `Descriptors.UnsupportedLoop` (RAUN003, reworded), `Descriptors.InvalidCondition` (RAUN011), `Descriptors.UnmergeableLocal` (RAUN012), both added to `SupportedDiagnostics`.

- [ ] **Step 1: Write the failing tests.** In `test/Raun.Generator.Test/AnalyzerTests.cs`, replace `RAUN003_control_flow` and append the rest:

```csharp
    [Fact]
    public async Task RAUN003_loops_are_still_rejected()
    {
        var diagnostics = await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    foreach (var i in new[] { 1, 2 }) { await Given.AvailableSlot(); }
                }
            }
            """);

        AssertHas(diagnostics, "RAUN003");
    }

    [Fact]
    public async Task RAUN003_while_switch_and_try_are_still_rejected()
    {
        AssertHas(await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    while (true) { await Given.AvailableSlot(); }
                }
            }
            """), "RAUN003");

        AssertHas(await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    try { await Given.AvailableSlot(); } catch { }
                }
            }
            """), "RAUN003");
    }

    [Fact]
    public async Task RAUN003_message_points_at_putting_the_loop_inside_a_step()
    {
        var diagnostics = await Analyze(
            """
            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    for (var i = 0; i < 2; i++) { await Given.AvailableSlot(); }
                }
            }
            """);

        var loop = Assert.Single(diagnostics, d => d.Id == "RAUN003");
        Assert.Contains("inside a step", loop.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RAUN003_no_longer_fires_on_a_supported_if()
    {
        var diagnostics = await GeneratorHarness.AnalyzeAsync(
            SampleSources.ConditionalDsl + SampleSources.IfElseScenario);

        Assert.DoesNotContain(diagnostics, d => d.Id == "RAUN003");
    }

    [Fact]
    public void RAUN011_and_RAUN012_are_supported_diagnostics()
    {
        var analyzer = new Raun.Generator.Analysis.ScenarioAnalyzer();

        Assert.Contains(analyzer.SupportedDiagnostics, d => d.Id == "RAUN011");
        Assert.Contains(analyzer.SupportedDiagnostics, d => d.Id == "RAUN012");
    }

    [Fact]
    public async Task Supported_conditional_scenarios_produce_no_diagnostics()
    {
        Assert.Empty(await GeneratorHarness.AnalyzeAsync(
            SampleSources.ConditionalDsl + SampleSources.IfElseScenario));
        Assert.Empty(await GeneratorHarness.AnalyzeAsync(
            SampleSources.ConditionalDsl + SampleSources.BareIfScenario));
        Assert.Empty(await GeneratorHarness.AnalyzeAsync(
            SampleSources.ConditionalDsl + SampleSources.NestedIfScenario));
        Assert.Empty(await GeneratorHarness.AnalyzeAsync(
            SampleSources.ConditionalDsl + SampleSources.OperatorTrueScenario));
        Assert.Empty(await GeneratorHarness.AnalyzeAsync(
            SampleSources.ConditionalDsl + SampleSources.ConditionalOverwriteScenario));
    }

    [Fact]
    public async Task RAUN011_bare_expression_condition()
    {
        var source = SampleSources.ConditionalDsl +
            """

            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    var patient = await Given.PatientExists("Jane");
                    if (patient.Name.Length > 3)
                        await When.Notify(patient);
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "RAUN011");
    }

    [Fact]
    public async Task RAUN011_awaited_non_dsl_condition()
    {
        var source = SampleSources.ConditionalDsl +
            """

            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    var patient = await Given.PatientExists("Jane");
                    if (await Task.FromResult(true))
                        await When.Notify(patient);
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "RAUN011");
    }

    [Fact]
    public async Task RAUN011_condition_result_is_not_usable_as_a_condition()
    {
        // A step returning a resource-ish value has no conversion to bool and no operator true.
        var source = SampleSources.ConditionalDsl +
            """

            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    if (await Given.PatientExists("Jane"))
                        await Given.DatabaseIsCleanPlaceholder();
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "RAUN011");
    }

    [Fact]
    public async Task RAUN012_conditional_assignment_to_a_non_step_local()
    {
        // `appointment` is initialized by a non-step expression, so the merge has no parent NODE to
        // merge against — only an initializer.
        var source = SampleSources.ConditionalDsl +
            """

            public static class S
            {
                [Scenario] public static async Task Bad()
                {
                    var patient = await Given.PatientExists("Jane");
                    Appointment appointment = null!;
                    if (await Given.IsPriority())
                        appointment = await When.CreateUrgent(patient);

                    await Then.AppointmentExists(appointment);
                }
            }
            """;

        AssertHas(await GeneratorHarness.AnalyzeAsync(source), "RAUN012");
    }

    [Fact]
    public async Task RAUN012_does_not_fire_on_reassignment_within_one_arm()
    {
        // Two definitions inside the SAME arm are fine — the definition map keeps the last one.
        var source = SampleSources.ConditionalDsl +
            """

            public static class S
            {
                [Scenario("double assign")] public static async Task Ok()
                {
                    var patient = await Given.PatientExists("Jane");
                    var appointment = await When.CreateStandard(patient);
                    if (await Given.IsPriority())
                    {
                        appointment = await When.CreateUrgent(patient);
                        appointment = await When.CreateUrgent(patient);
                    }

                    await Then.AppointmentExists(appointment);
                }
            }
            """;

        Assert.DoesNotContain(await GeneratorHarness.AnalyzeAsync(source), d => d.Id == "RAUN012");
    }
```

In `RAUN011_condition_result_is_not_usable_as_a_condition`, drop the placeholder call — the body only needs the bad `if`:

```csharp
                    if (await Given.PatientExists("Jane"))
                        await When.Notify(await Given.PatientExists("Jane"));
```

Replace that whole arm with a simpler legal statement instead — `await When.Notify(patient)` requires a `patient`, so declare it first:

```csharp
                    var patient = await Given.PatientExists("Jane");
                    if (await Given.PatientExists("Bob"))
                        await When.Notify(patient);
```

- [ ] **Step 2: Run the analyzer tests to verify they fail**

Run: `dotnet test test/Raun.Generator.Test/Raun.Generator.Test.csproj --filter "FullyQualifiedName~AnalyzerTests"`
Expected: FAIL — RAUN003 still fires on every `if`; RAUN011/RAUN012 do not exist.

- [ ] **Step 3: Update `Descriptors.cs`.** Reword RAUN003 and add the two new descriptors:

```csharp
    public static readonly DiagnosticDescriptor UnsupportedControlFlow = new(
        "RAUN003",
        "Unsupported control flow in scenario",
        "Loops and other control flow are not supported in scenario bodies — put the loop, retry, or polling inside a step. Only if/else (on an awaited phase-marker condition) shapes the graph",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
```

```csharp
    public static readonly DiagnosticDescriptor InvalidCondition = new(
        "RAUN011",
        "Scenario condition must be an awaited phase-marker call",
        "An 'if' condition in a scenario must be an awaited phase-marker call (Given/When/Then, or any type implementing Raun.IPhase) whose result is usable as a C# condition (bool, an implicit conversion to bool, or 'operator true')",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnmergeableLocal = new(
        "RAUN012",
        "Conditionally assigned local has no step-produced definition",
        "'{0}' is assigned inside a branch but has no step-produced definition outside it, so there is nothing to merge against — give it a prior step output, or assign it in every branch",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
```

- [ ] **Step 4: Register the rules** in `src/Raun.Generator/AnalyzerReleases.Unshipped.md` (release tracking is enforced at build time). Update the RAUN003 note and append:

```
RAUN003 | Raun.Usage | Error | Unsupported control flow in scenario (loops, switch, try, goto)
...
RAUN011 | Raun.Usage | Error | Scenario condition must be an awaited phase-marker call
RAUN012 | Raun.Usage | Error | Conditionally assigned local has no step-produced definition
```

(Keep the table's existing column order and alignment; RAUN010's note text is unchanged.)

- [ ] **Step 5: Update `ScenarioAnalyzer.cs`.** Add both descriptors to `SupportedDiagnostics`, remove `IfStatementSyntax` from the RAUN003 pattern list, and handle `if` and blocks:

```csharp
            case BlockSyntax block:
                foreach (var inner in block.Statements)
                {
                    AnalyzeStatement(context, inner, stepOutputs);
                }

                return;

            case IfStatementSyntax ifStatement:
                AnalyzeIf(context, ifStatement, stepOutputs);
                return;

            case ForStatementSyntax or ForEachStatementSyntax
                or WhileStatementSyntax or DoStatementSyntax or SwitchStatementSyntax
                or TryStatementSyntax or UsingStatementSyntax or LockStatementSyntax
                or GotoStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax
                or ThrowStatementSyntax or YieldStatementSyntax or LabeledStatementSyntax
                or FixedStatementSyntax or CheckedStatementSyntax or UnsafeStatementSyntax
                or LocalFunctionStatementSyntax or ReturnStatementSyntax:
                Report(context, Descriptors.UnsupportedControlFlow, statement.GetLocation());
                return;
```

```csharp
    /// <summary>
    /// An `if` is supported when its condition is an awaited phase-marker call whose result is usable
    /// as a C# condition. Each arm is analyzed with the same step-output set; an assignment inside an
    /// arm to a local that is not already a step output is RAUN012 (nothing to merge against).
    /// </summary>
    private static void AnalyzeIf(
        SyntaxNodeAnalysisContext context,
        IfStatementSyntax statement,
        HashSet<ILocalSymbol> stepOutputs)
    {
        if (statement.Condition is not AwaitExpressionSyntax { Expression: InvocationExpressionSyntax invocation }
            || invocation.Expression is not MemberAccessExpressionSyntax member
            || SymbolHelpers.PhaseOf(member.Expression, context.SemanticModel) is null)
        {
            Report(context, Descriptors.InvalidCondition, statement.Condition.GetLocation());
        }
        else
        {
            AnalyzeDslCall(context, invocation, stepOutputs, Descriptors.NotADslCall);

            if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
                && SymbolHelpers.TryUnwrapReturn(method.ReturnType, out var resultType)
                && (resultType is null || !IsUsableAsCondition(resultType, context.SemanticModel.Compilation)))
            {
                Report(context, Descriptors.InvalidCondition, statement.Condition.GetLocation());
            }
        }

        AnalyzeArm(context, statement.Statement, stepOutputs);
        if (statement.Else is { } elseClause)
        {
            AnalyzeArm(context, elseClause.Statement, stepOutputs);
        }
    }

    private static void AnalyzeArm(
        SyntaxNodeAnalysisContext context,
        StatementSyntax arm,
        HashSet<ILocalSymbol> stepOutputs)
    {
        // An arm may redefine an existing step-output local; it may not introduce a merge target that
        // has no step-produced definition outside the branch.
        foreach (var assignment in arm.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment is { Left: IdentifierNameSyntax identifier, Right: AwaitExpressionSyntax }
                && context.SemanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol local
                && !stepOutputs.Contains(local))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.UnmergeableLocal, identifier.GetLocation(), identifier.Identifier.Text));
            }
        }

        AnalyzeStatement(context, arm, stepOutputs);
    }

    /// <summary>
    /// True when <paramref name="type"/> can drive a C# <c>if</c>: it is <c>bool</c>, defines
    /// <c>operator true</c>, or has an implicit conversion to <c>bool</c>. <c>bool?</c> is correctly
    /// rejected — C# rejects it too.
    /// </summary>
    private static bool IsUsableAsCondition(ITypeSymbol type, Compilation compilation)
    {
        if (type.SpecialType == SpecialType.System_Boolean)
        {
            return true;
        }

        if (type.GetMembers("op_True").Any())
        {
            return true;
        }

        var boolType = compilation.GetSpecialType(SpecialType.System_Boolean);
        var conversion = compilation.ClassifyConversion(type, boolType);
        return conversion.IsImplicit && conversion.IsUserDefined;
    }
```

Note: the RAUN012 loop must run **before** `AnalyzeStatement(context, arm, ...)` adds the arm's own declarations to `stepOutputs`, which is why it is written first above. Arms declaring new locals with `var x = await ...` still register through `AnalyzeLocalDeclaration`.

- [ ] **Step 6: Run the analyzer tests**

Run: `dotnet test test/Raun.Generator.Test/Raun.Generator.Test.csproj --filter "FullyQualifiedName~AnalyzerTests"`
Expected: PASS.

- [ ] **Step 7: Full solution green**

Run: `dotnet build Raun.slnx` then `dotnet test Raun.slnx`
Expected: 0 warnings (including no `RS2008`/release-tracking warning for the new rule ids); all tests pass.

- [ ] **Step 8: Commit**

```bash
jj commit -m "feat(analyzer): narrow RAUN003 to loops; add RAUN011 and RAUN012 for conditionals"
```

---

### Task 5: MTP wiring — exclude synthetics, map NotTaken, and run the reporting spike

**Files:**
- Modify: `src/Raun.Mtp/RaunDiscoverer.cs`, `src/Raun.Mtp/ScenarioStepNumbering.cs`, `src/Raun.Mtp/MtpReportSink.cs`, `src/Raun.Mtp/HtmlReport/HtmlReportModelBuilder.cs`
- Test: `test/Raun.Mtp.Test/RaunDiscovererTests.cs`, `ScenarioStepNumberingTests.cs`, `MtpReportSinkTests.cs`, `RunLoopTests.cs`

**Interfaces:**
- Consumes: `ScenarioNode.IsSynthetic`, `StepStatus.NotTaken` (Task 1); the scheduler's suppressed start callback for `NotTaken` (Task 2).
- Produces: discovery/numbering that ignore synthetic nodes; a sink that publishes nothing for a synthetic node and maps `NotTaken` per the spike's outcome.

**The spike (spec "MTP reporting").** `StepStatus.NotTaken` maps one of two ways:

1. `SkippedTestNodeStateProperty` with reason `not taken: {condition}` — guaranteed to work, renders yellow.
2. **No terminal state at all** — the node keeps its `DiscoveredTestNodeStateProperty` and never receives an update, which most runners render as grey "Not Run".

Option 2 is preferred but unverified; it may trip the run loop's accounting or the `dotnet test` summary. Implement option 2 first and run the spike below. **Fall back to option 1 if any of these appear:** the sample run hangs, exits non-zero with all steps healthy, the summary's total does not equal the number of *discovered* nodes minus nothing (i.e. the runner errors on an un-updated node), or MTP logs a warning about a node without a terminal state. Record the outcome in a one-line comment above `MapState`.

- [ ] **Step 1: Write the failing tests**

`test/Raun.Mtp.Test/RaunDiscovererTests.cs` — add a `synthetic` flag to the local `Node` helper (`bool synthetic = false` → `IsSynthetic = synthetic`) and:

```csharp
    [Fact]
    public void Synthetic_merge_nodes_are_not_discovered()
    {
        var definition = Definition(
            nodes:
            [
                Node(0, "a", "step a"),
                Node(1, "m", "«merge appt»", synthetic: true),
                Node(2, "b", "step b"),
            ]);

        var nodes = RaunDiscoverer.BuildNodes(definition);

        Assert.Equal(2, nodes.Count);
        Assert.DoesNotContain(nodes, n => n.DisplayName.Contains("merge", StringComparison.Ordinal));
    }

    [Fact]
    public void Numbering_has_no_gap_where_a_synthetic_node_sits()
    {
        var definition = Definition(
            nodes:
            [
                Node(0, "a", "step a"),
                Node(1, "m", "«merge appt»", synthetic: true),
                Node(2, "b", "step b"),
            ]);

        var nodes = RaunDiscoverer.BuildNodes(definition);

        Assert.Equal("1. step a", nodes[0].DisplayName);
        Assert.Equal("2. step b", nodes[1].DisplayName);
    }
```

`test/Raun.Mtp.Test/ScenarioStepNumberingTests.cs` — add `bool synthetic = false` to its `Node` helper too, and:

```csharp
    [Fact]
    public void Synthetic_nodes_consume_no_number_and_leave_no_gap()
    {
        var labels = ScenarioStepNumbering.Compute(
            Def(Node(0), Node(1, synthetic: true), Node(2), Node(3, synthetic: true), Node(4)));

        Assert.Equal("1", labels[0]);
        Assert.Equal("2", labels[2]);
        Assert.Equal("3", labels[4]);
    }

    [Fact]
    public void Synthetic_nodes_still_get_a_label_for_the_html_report()
    {
        // The HTML report keeps merge nodes (it wants the merge diamond), so a label must exist —
        // it simply does not consume a top-level number.
        var labels = ScenarioStepNumbering.Compute(Def(Node(0), Node(1, synthetic: true), Node(2)));

        Assert.True(labels.ContainsKey(1));
    }
```

`test/Raun.Mtp.Test/MtpReportSinkTests.cs` — add `bool synthetic = false` to its `Node` helper and:

```csharp
    [Fact]
    public async Task Not_taken_step_is_never_reported_as_passed()
    {
        var def = Definition(id: "s", nodes: [Node(0, "a", "step a")]);
        var (sink, bus) = NewSink();

        await sink.HandleAsync(new StepFinished(def, new StepResult
        {
            Node = def.Nodes[0],
            DisplayName = "step a",
            Status = StepStatus.NotTaken,
            StartedAt = TestInstant,
            SkipReason = "not taken: IsPriority",
        }));

        Assert.Empty(bus.Messages
            .Select(m => m.TestNode)
            .SelectMany(n => n.Properties.OfType<PassedTestNodeStateProperty>()));
    }

    [Fact]
    public async Task Synthetic_merge_nodes_publish_no_updates()
    {
        var merge = Node(0, "m", "«merge appt»", synthetic: true);
        var def = Definition(id: "s", nodes: [merge]);
        var (sink, bus) = NewSink();

        await sink.HandleAsync(new StepFinished(def, new StepResult
        {
            Node = merge,
            DisplayName = "«merge appt»",
            Status = StepStatus.Passed,
            StartedAt = TestInstant,
        }));

        Assert.Empty(bus.Messages);
    }
```

(Use whatever the existing tests in this file use to feed the sink — they call the sink's public `IRunEventSink` entry point; match their exact call shape rather than inventing one. If the existing tests construct `StepStarted`/`StepFinished` and call `sink.HandleAsync(...)`, do the same.)

`test/Raun.Mtp.Test/RunLoopTests.cs` — an end-to-end pair, using the local `Node` helper extended with `Guard[]? guards = null`, `int[]? mergeSources = null`, `bool synthetic = false`, and `Func<object?, bool>? evaluate = null`:

```csharp
    [Fact]
    public async Task Conditional_scenario_runs_the_taken_arm_and_reports_the_other_as_not_green()
    {
        var def = Definition("c", "Conditional",
            Node(0, "cond", "is priority",
                invoke: (_, _) => Task.FromResult<object?>(true),
                evaluate: static o => (bool)o!),
            Node(1, "urgent", "create urgent", dependsOn: [0], guards: [new Guard(0, true)]),
            Node(2, "standard", "create standard", dependsOn: [0], guards: [new Guard(0, false)]),
            Node(3, "merge", "«merge appt»", mergeSources: [1, 2], synthetic: true));

        var finished = await RunAndCollect(def);   // helper mirroring the file's existing run helpers

        Assert.Equal(StepStatus.Passed, finished[1].Status);
        Assert.Equal(StepStatus.NotTaken, finished[2].Status);
        Assert.DoesNotContain(finished, r => r.Status == StepStatus.Passed && r.Node.StepId == "standard");
    }
```

Reuse this file's existing end-to-end run helper rather than adding `RunAndCollect` if one already exists; the assertion set is what matters.

- [ ] **Step 2: Run the MTP tests to verify they fail**

Run: `dotnet test test/Raun.Mtp.Test/Raun.Mtp.Test.csproj`
Expected: FAIL — synthetics are discovered and numbered; the sink publishes for them; `NotTaken` hits `MapState`'s `_ =>` arm and produces an `ErrorTestNodeStateProperty`.

- [ ] **Step 3: Exclude synthetics from discovery** — in `src/Raun.Mtp/RaunDiscoverer.cs`, `BuildNodes`:

```csharp
        foreach (var step in definition.Nodes)
        {
            // Merge/pass-through nodes are graph plumbing, not business steps: discovering them would
            // put "«merge appt»" in the user's test list. The HTML report keeps them.
            if (step.IsSynthetic)
            {
                continue;
            }

            nodes.Add(BuildNode(definition, step, labels));
        }
```

- [ ] **Step 4: Skip synthetics when numbering** — in `src/Raun.Mtp/ScenarioStepNumbering.cs`, `Compute`, inside the `foreach`, before the `GroupId` branches:

```csharp
            if (node.IsSynthetic)
            {
                // Consumes no number (users must not see a gap in 1, 2, 3) but still gets a label so
                // the HTML report can render it.
                assignments.Add((node.Index, nextTop, 0));
                continue;
            }
```

Because the label of a synthetic reuses the *previous* top-level number, `Format` would render a duplicate — acceptable, since synthetics never reach a runner's tree. Add that as a comment.

- [ ] **Step 5: Update `MtpReportSink`.** In `OnStepStartedAsync` and `OnStepFinishedAsync`, return early for synthetics; extend `MapState` with the spike's **option 2** first:

```csharp
    protected override async ValueTask OnStepStartedAsync(StepStarted e)
    {
        if (e.Context.Node.IsSynthetic)
        {
            return;
        }
        ...
    }

    protected override async ValueTask OnStepFinishedAsync(StepFinished e)
    {
        var result = e.Result;
        if (result.Node.IsSynthetic)
        {
            return;
        }

        // SPIKE (option 2): a not-taken branch receives no terminal state at all, so it keeps the
        // DiscoveredTestNodeStateProperty published at discovery and renders as grey "Not Run". The
        // scheduler does not raise OnStepStarting for NotTaken, so the node is never left InProgress.
        if (result.Status == StepStatus.NotTaken)
        {
            return;
        }
        ...
    }
```

Leave `MapState` otherwise unchanged; `NotTaken` never reaches it under option 2.

- [ ] **Step 6: Handle `NotTaken` in the HTML report model** — `src/Raun.Mtp/HtmlReport/HtmlReportModelBuilder.cs`:

```csharp
            // NotTaken renders with the existing "skipped" styling; the distinction survives in the
            // step's SkipReason ("not taken: …"). Distinct rendering (and decision/merge diamonds) is
            // a separate spec — deliberately out of scope here.
            StepStatus.NotTaken => "skipped",
```

- [ ] **Step 7: Run the MTP tests**

Run: `dotnet test test/Raun.Mtp.Test/Raun.Mtp.Test.csproj`
Expected: PASS. The `HtmlReportModelBuilderTests` verified snapshot must not move (no conditional scenario feeds it).

- [ ] **Step 8: Run the spike against the sample.** This needs Task 6's sample scenario, so do it in this order: add ONLY the sample scenario + DSL steps from Task 6 Step 1 now (temporarily, uncommitted), run the spike, then decide.

Run: `dotnet build Raun.slnx`
Run: `dotnet run --project samples/AppointmentTests/AppointmentTests.csproj`
Expected (option 2 healthy): exit code 0; the untaken arm's step appears in neither the passed nor the failed tally, or appears as "not run"/"skipped"; no MTP warning or error about a node lacking a terminal state; the process exits promptly.

Run: `dotnet test samples/AppointmentTests/AppointmentTests.csproj`
Expected: exit code 0, no error mentioning an un-updated or unknown test node.

- [ ] **Step 9: Decide and record the spike outcome.**
  - **Healthy** → keep option 2. Replace the `// SPIKE (option 2)` comment with: `// Verified 2026-09-03 against samples/AppointmentTests: MTP tolerates a discovered node that never receives a terminal state; it renders as "Not Run".`
  - **Unhealthy** (any symptom in Step 8) → switch to option 1: delete the early return for `NotTaken` in `OnStepFinishedAsync`, restore the start callback path (no scheduler change needed — the scheduler already suppresses `OnStepStarting` for `NotTaken`, so publish the finished update alone, which MTP accepts), and add to `MapState`:

```csharp
        // Fallback (option 1): MTP has no "not applicable" state, so a not-taken branch is reported
        // Skipped with a reason that names the deciding condition — never green.
        StepStatus.NotTaken => new SkippedTestNodeStateProperty(result.SkipReason ?? "not taken"),
```

  Record which option shipped in a comment above `MapState`, with the symptom that decided it.

- [ ] **Step 10: Revert the temporary sample edit** (Task 6 adds it properly): `jj diff samples/` should show the sample changes; leave them in place only if you are going straight into Task 6 in the same working copy. If reverting, use `jj restore samples/AppointmentTests`.

- [ ] **Step 11: Full solution green**

Run: `dotnet build Raun.slnx` then `dotnet test Raun.slnx`
Expected: 0 warnings; all tests pass.

- [ ] **Step 12: Commit**

```bash
jj commit -m "feat(mtp): exclude synthetic merge nodes from discovery and map NotTaken"
```

---

### Task 6: Sample scenario, snapshot, and docs

**Files:**
- Modify: `samples/AppointmentTests/AppointmentDsl.cs`, `samples/AppointmentTests/Scenarios.cs`
- Modify: `test/Raun.Generator.Test/GeneratorSnapshotTests.cs`
- Create: `test/Raun.Generator.Test/Snapshots/GeneratorSnapshotTests.Conditional_scenario#RaunScenarios.g.verified.cs` (by accepting the received file)
- Modify: `README.md`

**Interfaces:**
- Consumes: everything from Tasks 1–5.
- Produces: the living demo (also the Task 5 spike target) and the updated public description of the supported subset.

- [ ] **Step 1: Add condition steps to `samples/AppointmentTests/AppointmentDsl.cs`.** In the `extension(Given)` block:

```csharp
        [StepName("Given the patient is a priority case")]
        public static Task<bool> PatientIsPriority([Read] Patient patient, ScenarioContext? ctx = null)
        {
            ctx?.SimulateElapsed(TimeSpan.FromMilliseconds(180));
            // A deterministic demo rule: names starting with a letter before 'M' are priority.
            return Task.FromResult(patient.Name.Length > 0 && char.ToUpperInvariant(patient.Name[0]) < 'M');
        }
```

In the `extension(When)` block, a second creation step so both arms produce an `Appointment`:

```csharp
        [StepName("When creating an urgent appointment")]
        [return: Created(References = [nameof(patient)], Consumes = [nameof(slot)])]
        public static Task<Appointment> CreateUrgentAppointment(Patient patient, Slot slot, ScenarioContext? ctx = null)
        {
            ctx?.SimulateElapsed(TimeSpan.FromMilliseconds(410));
            return Task.FromResult(new Appointment(patient, slot));
        }
```

- [ ] **Step 2: Add the conditional scenario to `samples/AppointmentTests/Scenarios.cs`**

```csharp
    // Conditionals: the condition is an ordinary awaited step, so it is discovered, timed, and
    // reported like any other. Exactly one arm runs; the other is reported not-taken (never green),
    // and the two definitions of `appointment` merge at the closing brace.
    [Scenario("priority patients get an urgent appointment")]
    public static async Task BookingWithPriorityRouting()
    {
        var patient = await Given.PatientExists("Alice");
        var slot = await Given.AvailableSlot();

        Appointment appointment;
        if (await Given.PatientIsPriority(patient))
            appointment = await When.CreateUrgentAppointment(patient, slot);
        else
            appointment = await When.CreateAppointment(patient, slot);

        await Then.AppointmentExists(appointment);
    }
```

- [ ] **Step 3: Run the sample and confirm the shape**

Run: `dotnet run --project samples/AppointmentTests/AppointmentTests.csproj`
Expected: exit code 0; the new scenario's steps are numbered contiguously with no `«merge appointment»` entry in the test list; the untaken arm is not reported as passed.

- [ ] **Step 4: Add the snapshot test** in `test/Raun.Generator.Test/GeneratorSnapshotTests.cs`

```csharp
    [Fact]
    public Task Conditional_scenario() =>
        Verify(GeneratorHarness.RunDriver(SampleSources.ConditionalDsl + SampleSources.IfElseScenario))
            .UseDirectory("Snapshots");
```

- [ ] **Step 5: Run it, review the received output, and accept it**

Run: `dotnet test test/Raun.Generator.Test/Raun.Generator.Test.csproj --filter "FullyQualifiedName~GeneratorSnapshotTests"`
Expected: the five existing snapshot tests PASS unchanged; `Conditional_scenario` FAILS with a new `.received.cs`. Read the received file and confirm it contains: `Guards = new global::Raun.Model.Guard[] { new global::Raun.Model.Guard(1, true) }` on the if-arm node, `(1, false)` on the else-arm node, `MergeSources`/`IsSynthetic = true` on the merge node, and `EvaluateCondition = static __o => ((bool)__o!) ? true : false` on the condition node. Then accept it by copying `GeneratorSnapshotTests.Conditional_scenario#RaunScenarios.g.received.cs` over the `.verified.cs` name in `test/Raun.Generator.Test/Snapshots/` and re-running the filter — expected: PASS.

- [ ] **Step 6: Update `README.md`.** Replace the "Supported scenario subset (v1)" control-flow bullet (currently line 78–79) and add a conditionals bullet:

```markdown
- `if`/`else` shapes the graph when the condition is an awaited `Given`/`When`/`Then` call whose
  result is usable as a C# condition (`bool`, an implicit conversion to `bool`, or `operator true`).
  The condition is an ordinary step — discovered, timed, and reported like any other. Exactly one arm
  runs; steps in the other are reported **not taken**, never green. A local assigned in both arms is
  merged automatically.
- Loops (`for`/`foreach`/`while`/`do`), `switch`, `try`/`catch`, and `goto` are rejected with a
  diagnostic: put the loop, retry, or polling **inside a step**.
```

Add the conditionals design spec to the links at the end of that section:

```markdown
See [the design spec](docs/scenario-graph-extension-design.md), the
[conditionals design](docs/superpowers/specs/2026-09-03-scenario-conditionals-design.md), and the
[implementation plan](docs/superpowers/plans/2026-06-03-scenario-graph-extension.md).
```

- [ ] **Step 7: Full solution green**

Run: `dotnet build Raun.slnx` then `dotnet test Raun.slnx`
Expected: 0 warnings; all tests pass; the test count is the 262 baseline plus every test added in Tasks 1–6.

- [ ] **Step 8: Commit**

```bash
jj commit -m "docs: conditional sample scenario, generator snapshot, and README subset update"
```

## Self-Review

**1. Spec coverage**

| Spec section | Task |
|---|---|
| Surface: condition is an awaited phase-marker call | 3 (parser), 4 (RAUN011) |
| Condition result type / `EvaluateCondition` coercion | 1 (property), 3 (emit), 4 (RAUN011 type check) |
| Condition retains its output for later steps | 3 (`BuildStep` binds normally; the coercion is guard-only) |
| Guards, `DependsOn` stays all-of | 1, 2, 3 |
| Merge (phi) nodes, `IStepInputs.Get<T>(int)` unchanged | 1, 2, 3 |
| `IsSynthetic` excluded from discovery + numbering, kept in HTML report | 1, 5 |
| `StepStatus.NotTaken`, never green | 1, 2, 5 |
| Guard stack, nesting, `else if`, tuples/arrays inside arms | 3 |
| Definition map + phi insertion table | 3 |
| Phase 1 guard resolution incl. condition-threw ⇒ Skipped | 2 |
| Phase 2 launch gating | 2 |
| `NotTaken` propagation, `ApplySkipAsync`→`ApplyTerminalAsync` | 2 |
| Merge readiness (4 outcomes), bare `if` pass-through | 2, 3 |
| Simulated time treats `NotTaken` as zero duration | 2 |
| New `Validate()` invariants (3) | 1 |
| MTP mapping spike, option 1 fallback | 5 |
| RAUN003 narrowed; RAUN011; RAUN012 | 4 |
| RAUN009 behaviour inside branches (explicit tests, not an assumption) | **gap — see below** |
| Testing split across the three projects + sample | 1–6 |

**Gap found and closed:** the spec's analyzer table asks for explicit tests that a resource `[Created]` in an untaken arm never records its lineage claim. Add to Task 3, Step 2 (`ConditionalLoweringTests`) before implementing:

```csharp
    [Fact]
    public async Task Resource_claims_in_an_untaken_arm_are_never_recorded()
    {
        // RAUN009 interaction: a [Created] resource inside a branch that was not taken never exists,
        // so no effect and no lineage relation is recorded — exactly as for a skipped step today.
        var result = GeneratorHarness.Run(SampleSources.ConditionalDsl + SampleSources.IfElseScenario);
        result.AssertCompiles();

        var results = await result.Definitions().Single().RunAsync();

        var untaken = results[3];
        Assert.Equal(StepStatus.NotTaken, untaken.Status);
        Assert.Empty(untaken.Effects);
        Assert.Empty(untaken.Lineage);
    }
```

(The `ConditionalDsl` steps carry no role attributes, so this asserts the terminal-node contract — `StepResult`s produced by `ApplyTerminalAsync` carry no effects at all. That is the guarantee the spec wants; if a role-bearing conditional DSL is wanted later it is a follow-up.)

**2. Placeholder scan:** none. Every code step carries the actual code. The two `SampleSources` snippets that were shown twice in Task 3 Step 1 (`BareIfScenario`) and Task 4 Step 1 (the RAUN011 bad-condition body) are corrections shown inline — use the second, corrected form in both cases.

**3. Type consistency:** `Guard(int ConditionIndex, bool WhenValue)` (runtime) and `ParsedGuard(int, bool)` (generator IR) are used consistently; `MergeSources` is `IReadOnlyList<int>` everywhere; `EvaluateCondition` is `Func<object?, bool>?` in the model, emitted as `static __o => ((T)__o!) ? true : false`, and read only through `EvaluateGuard`; `ApplyTerminalAsync(int, StepStatus, string)` replaces `ApplySkipAsync(int, string)` at all three call sites; `IsSynthetic` is the single spelling in model, IR, discoverer, numbering, and sink.
