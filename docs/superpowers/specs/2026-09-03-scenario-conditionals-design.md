# Scenario Conditionals — Design

- **Date:** 2026-09-03
- **Status:** Design approved in brainstorming; implementation plan pending.
- **Scope:** `src/Freista` (model, scheduler), `src/Freista.Generator` (lowering, analysis),
  `src/Freista.Mtp` (discovery, numbering, report sink), `samples/AppointmentTests`, all three test
  projects.
- **Out of scope:** rendering decision/merge diamonds in the HTML report (see "Non-Goals").

## Problem

`[Scenario]` bodies reject all control flow (FRST003). A step is an ordinary `async Task<T>` method,
so a step can already loop, retry, or poll *internally* — but a step cannot cause a **later, sibling
step to not run**. That is the actual gap, and only `if`/`else` fills it.

This is why loops are not part of this design. `foreach`, `while`, and retry/polling are all
expressible as a single step whose implementation loops; the graph never needs to know. Conditionals
are the one construct that is genuinely a graph shape.

## Non-Goals

- **Loops** (`for`, `foreach`, `while`, `do`). Belongs inside a step's implementation.
- **`switch`, `try`/`catch`, `goto`.** No demand, and `goto` in particular would force a real CFG
  (see "Why not full SSA").
- **HTML report diamonds.** This design produces the branch data that makes UML decision/merge
  diamonds drawable — a previously deferred item — but rendering them is a separate spec. Kept out
  deliberately to bound this work.
- **A runnable-source execution model.** Considered and rejected; see "Alternatives considered".

## Surface

A condition must be an **awaited phase-marker call** whose result is usable as a C# condition:

```csharp
[Scenario("senior patient books an appointment")]
public static async Task Booking()
{
    var patient = await Given.PatientExists("Jane");

    if (await Given.PatientAge(age => age > 65))
        await When.ApplySeniorDiscount(patient);

    await Then.InvoiceIsCorrect(patient);
}
```

Two consequences fall out of this rule and are the reason it was chosen:

1. **The condition is an ordinary node.** It is discovered, named, timed, logged, and reports
   pass/fail exactly like every other step. No new machinery evaluates user code outside a step, and
   no new failure surface is introduced.
2. **Observability is free.** Every branch in every report is explained by a real row, without a
   marker function or a fallback that prints raw expression source.

Predicate arguments (`age => age > 65`) are what keep this from requiring a DSL method per
threshold: `PatientAge` once, not `PatientIsOver60`/`PatientIsOver65`/`PatientIsUnder18`. Argument
validation already admits them — `ScenarioAnalyzer` rejects locals that are not step outputs, and a
lambda parameter is an `IParameterSymbol`, not an `ILocalSymbol`.

### Rejected surfaces

- **Bare expressions** (`if (patient.Age > 65)`) — the decision becomes invisible in the report, and
  arbitrary user code would have to run inside the scheduler with no defined place for its
  exceptions to be reported.
- **A `Decision("label", expr)` marker** — workable (the body is generator source and never
  executes, so the eagerly-evaluated argument is not a problem), but it introduces DSL vocabulary
  that awaited step calls already provide.

### Condition result type

Anything usable in a C# `if`: `bool`, a type with an implicit conversion to `bool`, or a type
defining `operator true`. Restricting to literal `bool` would make the DSL stricter than the
language for no benefit. `bool?` is correctly rejected, matching C#.

The scheduler must not perform user-defined conversions on a boxed `object?`. Instead the
**generator emits the coercion** so Roslyn compiles it:

```csharp
// on ScenarioNode
public Func<object?, bool>? EvaluateCondition { get; init; }

// emitted for a condition node returning Task<MyResult>
EvaluateCondition = static o => ((MyResult)o!) ? true : false,
```

`x ? true : false` is the single spelling covering all three cases; Roslyn selects the mechanism at
compile time. No reflection, no runtime type tests, and an unusable type fails at generated-code
compile time as a backstop behind FRST011.

The condition node retains its original output, so `var r = await Given.Thing(); if (r) ...` still
binds `r` to the real value for later steps. Coercion applies only to the guard.

## Graph model

`DependsOn` continues to mean **all of**. Every readiness and skip rule in `ScenarioScheduler` reads
it; making it sometimes-any-of would be a lasting source of bugs. Two additions instead.

### Guards

```csharp
public readonly record struct Guard(int ConditionIndex, bool WhenValue);

// on ScenarioNode
public IReadOnlyList<Guard> Guards { get; init; } = [];
```

`ConditionIndex` points at the condition node. `WhenValue` is `true` for the `if` arm, `false` for
`else`. Nested `if`s stack guards; all must hold. A node runs when its `DependsOn` have all passed
**and** every guard is satisfied.

### Merge (phi) nodes

A local assigned in both arms is a phi. The generator emits one synthetic merge node; consumers stay
ordinary all-of nodes and `IStepInputs.Get<T>(int)` is unchanged.

```
2  Given is priority        (condition)
3  When create urgent       guard: [2 == true]
4  When create standard     guard: [2 == false]
5  «merge appt»             synthetic, sources: [3, 4]
6  Then appointment exists  DependsOn: [5]   appt = Get<Appointment>(5)
```

The merge node is the **only** place any-of semantics exist.

### `IsSynthetic`

Merge nodes are graph plumbing, not business steps. `FreistaDiscoverer.BuildNodes` and
`ScenarioStepNumbering` must exclude them, so users never see a gap in "1, 2, 3". The HTML report
retains them — it wants the merge diamond.

### `StepStatus.NotTaken`

A new domain status, distinct from `Skipped`. The report must distinguish "this branch was not
chosen" from "this was skipped because a dependency blew up", regardless of what MTP can express.
It is **never green**.

## Generator lowering

The parser becomes a recursive walk over a guard stack:

- On `if (await Given.C(...))`: lower the condition as an ordinary node, push `Guard(condIndex,
  true)`, walk the arm, pop. Same for `else` with `false`.
- Nodes created inside inherit the current guard stack; nested `if`s produce multiple guards;
  `else if` is just nesting.
- Tuples, arrays, and LINQ groups work unchanged inside an arm and inherit guards like anything else.

### Joining after the `if`

Because `DependsOn` means all-of, **arm nodes must never become the source-order dependency of a
following statement** — a step after the `if` would then wait on a node that may never run. The
post-`if` frontier is the merge nodes when the branch produced any, and otherwise the condition
node. The condition always runs, so the frontier is always live.

### Single-assignment / definition map

The existing lowering is already single-assignment by construction: each step output is bound to
`__rN` for producing node N, `IdentifierReplacer` rewrites uses, and FRST007 forbids locals that are
not step outputs. `if` introduces the first case of one local having **two** definitions — which is
exactly what SSA exists for, and a merge node is a phi node.

The parser therefore holds a scoped definition map:

```
Dictionary<ILocalSymbol, int>   // local -> defining node index
entering an arm:    push a child map
leaving both arms:  diff the child maps against the parent
```

The diff *is* phi insertion:

| Diff result | Meaning | Action |
|---|---|---|
| Same definition in both arms | untouched by the branch | nothing |
| Different definition in each arm | genuine phi | insert merge node, write its index to parent map |
| Defined in one arm only, exists in parent | conditional overwrite | wrap the parent definition in a pass-through node guarded on the opposite value, then merge (see "Merge readiness") |
| Defined in one arm only, new local | branch-local | drop; C# scoping already forbids the use |

Nesting works because the maps chain.

### Why not full SSA

Dominance frontiers, iterated phi placement, and a real CFG exist to find merge points in
unstructured code with `goto`. Freista has structured control flow only, so every merge point is
syntactically obvious — it is the closing brace. Building a CFG to rediscover that would be pure
ceremony. Adopt the SSA vocabulary and the definition map; skip the machinery.

## Scheduler

`ScenarioScheduler` resolves skips (phase 1) then launches nodes whose dependencies all passed
(phase 2). Guards slot into both; merges need a third rule.

### Phase 1 — guard resolution

For each guard on a pending node, inspect `status[g.ConditionIndex]`:

| Condition status | Result |
|---|---|
| `Pending` / `Running` | unresolved — wait |
| `Passed` | evaluate `EvaluateCondition(output)`; if it differs from `g.WhenValue`, node becomes `NotTaken` |
| `Failed` / `Skipped` | node becomes `Skipped` — cascade, not a decision |
| `NotTaken` | node becomes `NotTaken` — the condition sat in a branch that was itself not chosen |

Row three is load-bearing. If the condition step **threw**, no branch was ever chosen; reporting the
arm as "not taken" would disguise a failure as a routine decision. A blown-up condition is a
dependency failure and must read as one.

Row four is the nested case, and it must *not* collapse into row three. A condition inside an
untaken outer arm never ran because a decision went the other way — nothing went wrong — so an
untaken nested branch reports `NotTaken` all the way down. Cascading it to `Skipped` would
reintroduce exactly the ambiguity `NotTaken` exists to remove.

### Phase 2 — launch

Launch when all dependencies passed **and** every guard is satisfied.

### `NotTaken` propagation

The dependency `switch` gains a `NotTaken` arm. When a node's only bad dependencies are `NotTaken`,
it becomes `NotTaken` with reason `not taken: {condition}` rather than `dependency skipped: X`.
Steps *within* an arm rarely need this — they carry the same guard and resolve independently — but
it matters for merges nested inside an outer untaken branch. `ApplySkipAsync` becomes
`ApplyTerminalAsync(index, status, reason)`.

### Merge readiness

**Guards resolve before merge sources.** A pass-through node is both guarded and a merge, so
resolving its sources first would let the parent value through on *both* sides of the branch and
defeat the mutual exclusion the merge depends on. Guard resolution applies to every node; only once
its guards hold does a merge consult its sources.

- ready when all sources are terminal
- exactly one `Passed` — merge passes, output = that source's output
- all sources `NotTaken` — merge is `NotTaken`
- any source `Failed`/`Skipped` — merge is `Skipped`, cascading the reason

A bare `if` with no `else` cannot merge directly against the parent definition: the parent is
**unguarded**, so it and the arm could both pass, violating the mutual-exclusion invariant below.
Instead the generator emits a synthetic **pass-through** node guarded on the opposite value, whose
single source is the parent definition. Sources become `[arm (c == true), passthrough (c == false)]`
— mutually exclusive by construction, no special case in the scheduler. A one-source merge simply
aliases its source.

Simulated time treats `NotTaken` as `Skipped` — zero duration.

### New `Validate()` invariants

`ScenarioDefinition.Validate()` is where graph invariants are asserted, so it must enforce rather
than assume:

- guard `ConditionIndex` values are in range
- any node referenced by a guard has a non-null `EvaluateCondition`
- **merge sources are mutually exclusive** — guarded on the same condition with opposite
  `WhenValue`. The generator guarantees this; a violation would otherwise surface as a baffling
  double-write instead of a clear error.

## MTP reporting

MTP's complete state vocabulary is `Discovered`, `InProgress`, `Passed`, `Failed`, `Error`,
`Timeout`, `Cancelled`, `Skipped`. There is **no** "not applicable" state.

`StepStatus.NotTaken` maps one of two ways, decided by a spike **during** implementation, not before:

1. **`Skipped` with a distinguishing reason** — `not taken: {condition} was false`, versus today's
   `dependency failed: X`. Guaranteed to work. Yellow.
2. **No terminal state at all** — the node keeps the `DiscoveredTestNodeStateProperty` published at
   discovery and never receives an update. Most runners render this as grey "Not Run", which is the
   desired semantic.

Option 2 is preferred but unverified: a discovered node with no terminal state may trip the run
loop's accounting or the `dotnet test` summary. The spike runs against `samples/AppointmentTests`;
option 1 is the fallback. `StepStatus.NotTaken` exists in the domain model either way, so nothing
downstream depends on the outcome.

**A not-taken branch is never reported green.**

## Analyzer rules

| ID | Rule |
|---|---|
| FRST003 | Narrowed from "no control flow" to `for`/`foreach`/`while`/`do`/`switch`/`try`/`goto`. The message must point at the real answer: put the loop or retry **inside a step**. |
| FRST011 | *(new)* Condition must be an awaited phase-marker call whose result is usable as a C# condition. |
| FRST012 | *(new)* A conditionally-assigned local has no step-produced definition on some path. `Appointment appt = null!; if (c) appt = await When.Create();` type-checks in C#, but the merge's "parent definition" source is an initializer, not a node, so there is nothing to merge against. Reassignment *within* one arm is not an error — the definition map handles it, last definition wins. |
| FRST009 | Verify behaviour inside branches. A resource `[Created]` in an untaken arm never exists, so its lineage claim never records — expected to work as it does for a skipped step today, but it is the interaction most likely to surprise us and gets explicit tests rather than an assumption. |

## Testing

TDD, behavioural tests first, following the existing project split.

| Project | Coverage |
|---|---|
| `Freista.Test` | `SchedulerTests`: guard resolution; all four merge outcomes; `NotTaken` propagation; **condition-throws produces `Skipped`, not `NotTaken`**. `ModelTests`: the new `Validate()` invariants. |
| `Freista.Generator.Test` | New `ConditionalLoweringTests.cs` beside `LinearLoweringTests`/`ParallelLoweringTests`; `SampleSources` gains a conditional DSL and scenarios; `AnalyzerTests` gains FRST011/FRST012 and the narrowed FRST003; snapshot re-accept. |
| `Freista.Mtp.Test` | `FreistaDiscovererTests`: synthetic merge nodes are not discovered. `ScenarioStepNumberingTests`: numbering skips synthetics. `MtpReportSinkTests`: `NotTaken` mapping. `RunLoopTests`: end-to-end, both arms. |
| `samples/AppointmentTests` | A conditional scenario — the living demo and the spike target. |

## Alternatives considered

### Runnable scenario bodies

Execute the `[Scenario]` body directly instead of lowering it, with steps registering themselves as
they run. Control flow would then be free — `if`, loops, `try`, everything — and the generator and
its analyzer (~2,400 lines each of source and tests) would largely disappear.

Rejected. What the static model buys, in order of weight:

1. **Discovery without execution.** MTP requests the test list before a run (Test Explorer
   population, `--list-tests`, filter resolution). A runnable model cannot enumerate steps without
   executing the body, which means standing up containers and databases. It also breaks the stable
   `{ScenarioId}:{StepId}` uid contract (`FreistaDiscoverer.cs:16`) that lets a single failed step be
   re-run by filter — the node would not exist until the run reached it.
2. **Partial-failure continuation.** A failed step skips its transitive dependents while independent
   branches keep running. A running body cannot do this; a throw unwinds everything after it. Poison
   values would be needed, and execution would still walk sequentially through the dead region.
3. **Compile-time diagnostics.** FRST001–FRST012 become runtime errors.

Parallelism is **not** an argument: Freista is sequential by default, and the tuple/array forms
would be plain `Task.WhenAll` in a runnable model.

"Every business step is its own test, discoverable and individually re-runnable before anything
runs" is the product, and it is the one thing a runnable model structurally cannot deliver.

### Record-then-execute hybrid

Run the body once with stubbed steps to build the graph, then execute for real. Rejected: it fails
on exactly this feature. A stubbed condition has no truthful value, so the recording pass picks an
arbitrary branch and discovers the wrong graph. Conditionals are precisely what recording cannot see.
