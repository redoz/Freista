# C2 Resource-Aware Scheduling — Findings

- **Date:** 2026-09-04 (resolved 2026-09-05)
- **Status:** **Resolved.** Lock-based C2 (two-phase locking + wound-wait) is rejected for good, not
  deferred: it trades deadlocks for nondeterministic re-execution of real side effects, and half the
  declared claims can never be locked anyway. What ships instead is **conflict detection** — a
  compile-time rule (RAUN013) plus a scenario-scoped runtime ledger — designed in
  `2026-09-05-resource-conflict-detection-design.md`. Cross-scenario coordination (type-level
  admission control) is deferred until scenarios run concurrently at all.
- **Open question below ("serialize or fail") is answered:** fail inside a scenario, serialize across
  scenarios. See the design's "Who owns a conflict decides the response".
- The "Dead weight" listed below was deleted on 2026-09-05.

The analysis that follows is kept verbatim because it is what the decision rests on.

## The finding that reframes C2

**Resource claims are recorded inside the step, after the DSL call has already run.** The generator
emits:

```csharp
var __r = await When.CreateAppointment(patient, slot);   // the call runs FIRST
await __ctx.Resources.Create(__r);                       // the claim is recorded after
```

This splits declared claims into two kinds with very different schedulability:

| Claim | Resolves to | Known before the step runs? |
|---|---|---|
| Parameter role — `[Read]`, `[Edited]`, `[Deleted]` on a parameter | `__inputs.Get<Patient>(0)` — a **prior step's output** | **Yes.** Lockable. |
| Return role — `[Created]`, `[Loaded]`, `[Edited]` on the return | `__r` — **this step's own output** | **No.** Never lockable in advance. |

You cannot take a lock on a row you have not created yet. So **roughly half the declared claims are
unschedulable by construction**, and any C2 that locks can only lock on parameter-role claims.

This also means the current XML docs promise something impossible: `LifecycleVerb.Create` is
annotated "exclusive in C2", and `CreatedAttribute` says the same. Whatever happens to C2, that
wording should be corrected — it describes a guarantee the design cannot provide.

A partial escape exists but was not pursued: let a step declare its produced identity *ahead* of
time when the key is derivable from arguments (`[Created(Key = nameof(name))]`). That buys back some
return-role claims at the cost of new DSL surface, and only for keys that happen to be argument-derived.

## The open question, unanswered

If two concurrent steps claim the same identity, should C2 **serialize** them or **fail**?

For a *test* framework the second is a real contender, and it was not obvious before writing it down:
silently serializing a conflict hides a bug in the scenario and changes its timing, whereas an error
naming both steps teaches the author to add a dependency or re-declare one claim as `[Read]`.
Serializing is what the existing machinery was built for; failing is cheaper and arguably more
honest. This is the first thing to settle if C2 is revived.

## Dead weight this uncovered

Independent of C2, and safe to delete whenever someone is in the area:

- **`Access`** (`src/Raun/Resources/Access.cs`) — zero typed uses anywhere in the repo. Its own
  doc comment references `LockAsync` and `[Requires<T>]`, **neither of which exists**.
- **`ISingletonResource<TSelf>`** — declared once; its only other mention is a comment in the
  analyzer.
- `LockMode` and `ResourceClaim.Reduce` are used only by tests and the trace path. They are the
  scaffolding C2 would have consumed.

## What resources actually are today

A **tracing and lineage** feature. The scheduler has no resource awareness at all — it forwards
`Effects` and `Lineage` to the report and nothing else. Nothing prevents two steps from mutating the
same identity concurrently. That is fine and useful; it is simply not what the docs currently claim.
