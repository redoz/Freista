# C2 Resource-Aware Scheduling — Findings (deferred to v2)

- **Date:** 2026-09-04
- **Status:** **Deferred to v2.** Not designed, not planned. This document exists only so the
  analysis below is not re-derived from scratch, because it changes what C2 can be.
- **Trigger to revisit:** a real scenario that needs safe concurrent access to a shared fixture, or
  a decision to make the resource docs honest (see "Dead weight" below).

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

- **`Access`** (`src/Freista/Resources/Access.cs`) — zero typed uses anywhere in the repo. Its own
  doc comment references `LockAsync` and `[Requires<T>]`, **neither of which exists**.
- **`ISingletonResource<TSelf>`** — declared once; its only other mention is a comment in the
  analyzer.
- `LockMode` and `ResourceClaim.Reduce` are used only by tests and the trace path. They are the
  scaffolding C2 would have consumed.

## What resources actually are today

A **tracing and lineage** feature. The scheduler has no resource awareness at all — it forwards
`Effects` and `Lineage` to the report and nothing else. Nothing prevents two steps from mutating the
same identity concurrently. That is fine and useful; it is simply not what the docs currently claim.
