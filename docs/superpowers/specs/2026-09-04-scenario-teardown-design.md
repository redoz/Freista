# Scenario Teardown — Design

- **Date:** 2026-09-04
- **Status:** Design approved in brainstorming; implementation plan pending.
- **Scope:** `src/Freista` (context, scheduler, model), `src/Freista.Generator` (emit the teardown
  node), `src/Freista.Mtp` (report the node), `samples/AppointmentTests`, the three test projects.
- **Out of scope:** a user-facing DI registration API, C2 resource-aware scheduling, OTEL
  correlation. Each is its own spec.

## Problem

Freista has no teardown of any kind — no dispose, cleanup, or lifecycle hook anywhere in the
runtime. An integration test creates a tenant, a container, a database row, and nothing removes it.

The obvious answers do not transfer:

- **xUnit's model does not apply.** `[Scenario]` marks a **static** method whose body is *source for
  the generator and never executed*. There is no instance to implement `IAsyncLifetime` on, and no
  method invocation to bracket. The unit of execution is a step, not a scenario.
- **Deriving teardown from `[Created]` does not work.** The resource model records a resource's
  *identity* (type + key), not how to delete it or what connection to delete it on. Deriving cleanup
  would require a registered deleter per resource type plus a resolved connection — a whole
  resource-provider abstraction, layered on a DI story that does not exist yet.

## Surface

Cleanup is **registered as a closure by the step that creates the thing**. The closure captures both
the object and the connection, because it is written where both are already in scope. Nothing is
derived and nothing is wired.

```csharp
[StepName("patient {name} exists")]
[return: Created]
public static async Task<Patient> PatientExists(string name, ScenarioContext? ctx = null)
{
    var patient = await Db.InsertPatient(name);
    ctx?.OnTeardown(() => Db.DeletePatient(patient.Id));
    return patient;
}

[StepName("a postgres container is running")]
[return: Created]
public static async Task<Container> PostgresIsRunning(ScenarioContext? ctx = null)
{
    var container = await Docker.Start("postgres:17");
    ctx?.OnTeardown(Cleanup.Required, () => container.StopAsync());
    return container;
}
```

```csharp
[Scenario("customer books an appointment")]
[Teardown(Run.OnSuccess)]        // Always (default when the attribute is absent) | OnSuccess | Never
public static async Task Booking() { ... }
```

### Two levels, one direction

- **The scenario attribute** decides `Always` / `OnSuccess` / `Never` for **ordinary** cleanups.
- **A registration is ordinary or required.** `Cleanup.Required` runs regardless of the scenario
  attribute, `Run.Never` included.

Registrations do **not** carry `Always`/`OnSuccess` — that is the scenario's decision. What a
registration may assert is that it is not optional. The distinction is not a policy difference but a
kind difference: leaving database rows behind for inspection is a *choice*; leaving a container
running or a connection pool unreleased is a *leak*, and no scenario-level debugging switch should be
able to cause one.

`Run.Never` therefore reliably means "leave the state alone" for everything a person would want to
inspect, while still releasing process-level resources.

### API

```csharp
public enum Cleanup { Optional, Required }

public enum Run { Always, OnSuccess, Never }

// on ScenarioContext
public void OnTeardown(Func<Task> cleanup);                   // ordinary
public void OnTeardown(Cleanup kind, Func<Task> cleanup);     // explicit kind
```

The enum is `Cleanup`, not `Teardown`: a type named `Teardown` in scope makes `[Teardown(...)]`
ambiguous with `TeardownAttribute` (CS1614).

## Reporting

**One `Teardown` node per scenario, emitted by the generator and discovered like any other step.**
Every registered closure runs inside it; if any throws, that node fails carrying all collected
errors.

```
1. Given patient Jane exists        PASS   8ms
2. Given an available slot exists   PASS   4ms
3. When creating an appointment     PASS  40ms
4. Then the appointment exists      PASS  12ms
5. Teardown (2 actions)             FAIL  30ms
   └ DeletePatient: FK constraint violation
```

This is forced by discovery, not chosen for convenience: MTP asks for the test list **before** the
run, and closure registrations only come into existence *during* it. A per-closure test node could
never be discovered, would have no stable uid, and could not be re-run by filter. A single
pre-declared node per scenario is the finest granularity that survives that constraint — and it is
enough, because the thing that must not be silent is a *failing* cleanup.

The node's status:

| Situation | Status |
|---|---|
| Cleanups ran, none threw | `Passed` |
| One or more threw | `Failed`, message listing every error |
| Policy skipped real registrations (and none were required) | `NotTaken` |
| **Nothing was registered at all** | **`Passed`** |

`NotTaken` for a suppressed teardown is deliberate: cleanups existed and `Run.Never` (or a failed
scenario under `OnSuccess`) chose not to run them, which has real consequences for the leftover
state, and reporting that green would say something false.

The last row is a **correction made during implementation**. The design originally said `NotTaken`
here too, but since the node is emitted for every scenario, that would put a permanent non-passing
node in every scenario of every suite that never uses teardown — it broke every existing
"all steps passed" assertion in the repo the moment the node was emitted, which is exactly what
users would experience. Nothing to clean up is success, the same as a step with an empty body.
Suppression and vacuity are different things and now report differently.

### Numbering and discovery need no changes

The teardown node takes the **last** step number (`5.` above). That falls out for free: the
generator emits it last, so it holds the highest `Index`, and `ScenarioStepNumbering.Compute`
already orders by `Index`.

It must **not** reuse `IsSynthetic`, which means "in the graph, hidden from discovery and numbering"
(merge nodes). The teardown node is the inverse — generator-emitted but user-visible. It carries its
own `IsTeardown` marker, read only by the **scheduler** and the report. Conflating the two flags
would silently hide the node from CI, which is the one outcome this design exists to prevent.

## Runtime model

### Where registrations aggregate

There is no scenario-scoped context today — `ScenarioContext` is deliberately per-step so
concurrent siblings cannot corrupt each other's logs. So the **scheduler** owns a thread-safe
`TeardownLog` and passes each step's context a reference to it. `OnTeardown` appends
`(OwningStepIndex, Sequence, Cleanup, Func<Task>)`. The log is the synchronized object; the context
stays per-step and unchanged in shape.

### Order

**Reverse topological order of the owning step; reverse registration order within a step.**

Registration order alone is not usable: steps run concurrently, so it is nondeterministic across
parallel branches. Every registration knows its owning step, and the scheduler already holds the
DAG, so reverse-dependency order is both deterministic and correct — the appointment is torn down
before the patient it references.

### Execution

After the DAG drains, the scheduler runs the teardown node:

1. Decide the scenario outcome: **success = every non-teardown node is `Passed` or `NotTaken`**.
   `NotTaken` is not failure.
2. Select the closures to run — all `Cleanup.Required`, plus ordinary ones when the policy allows.
3. Run them in order. **A throwing cleanup is caught and recorded; the remaining cleanups still
   run.** Aborting on the first error would leak everything behind it, which is the wrong trade for
   containers and rows.
4. Publish the node's status per the table above.

### Cancellation and timeout

**`Cleanup.Required` closures run on a fresh `CancellationToken`, not the scenario's.** A scenario
killed by timeout is precisely when a container leaks, so the cancelled token must not suppress the
cleanup that exists to prevent it. Each required cleanup gets its own bounded timeout so a hung one
cannot wedge the run.

Ordinary cleanups follow the policy; a cancelled scenario is not a success, so `Run.OnSuccess`
skips them.

### Untaken branches need no special case

A step inside a branch that was not taken never executed, so it never registered a closure. The
conditionals work handles this by construction — there is nothing to detect and nothing to suppress.

### Reserved final slot

When a per-scenario DI scope eventually exists, disposing it is itself a required cleanup and must
run **after** every user cleanup, since those may hold objects resolved from it. The execution order
therefore reserves a final slot for framework-owned cleanups. This design leaves the slot; it does
not populate it.

## Testing

TDD, behavioural tests first, following the existing project split.

| Project | Coverage |
|---|---|
| `Freista.Test` | `TeardownLog` ordering (reverse-topological across parallel branches, reverse sequence within a step); policy selection for each `Run` value crossed with each `Cleanup` kind; a throwing cleanup does not stop the rest; required cleanups run after cancellation and after a scenario timeout; ordinary cleanups skipped on failure under `OnSuccess`; a step in an untaken branch registers nothing. |
| `Freista.Generator.Test` | The teardown node is emitted last, carries `IsTeardown`, and is absent when the scenario registers no teardown *and* has no attribute — decide once and test it; snapshot of a scenario with `[Teardown]`. |
| `Freista.Mtp.Test` | The teardown node **is** discovered (unlike a merge node) and takes the final number; `Failed` carries every collected error; `NotTaken` under `Run.Never`; never `Passed` when a cleanup threw. |
| `samples/AppointmentTests` | A scenario registering both an ordinary and a required cleanup, visible end-to-end. |

## Open question deferred to the plan

Whether the teardown node is emitted for **every** scenario or only for scenarios that could
register a cleanup. The generator cannot know whether a step registers one — registration is a
runtime call inside a DSL method body, invisible to lowering. So the node is emitted unconditionally
and reports `NotTaken` when nothing registered. The alternative (emit only when `[Teardown]` is
present) makes the common case — a step registering cleanup in a scenario with no attribute — fail
silently, which is unacceptable. **Emit unconditionally.**

## Alternatives considered

- **A `[Teardown]` method per scenario holding the cleanup code.** The original proposal. Rejected
  because the scenario body never executes, so its locals do not exist at runtime — a teardown
  method has no way to reach the appointment it is meant to delete, nor the connection to delete it
  on. Every variant (bind parameters by name, a mutable bag, a scenario-level instance) reintroduces
  the wiring the closure form eliminates.
- **A non-static scenario class instantiated per scenario, xUnit style.** Rejected: it reintroduces
  shared mutable state across concurrently-running steps, which `ScenarioContext` avoids by
  construction. xUnit does not face this because it has no intra-test parallelism.
- **Deriving teardown from `[Created]` lineage.** Rejected; see "Problem".
- **One test node per registered closure, published dynamically.** Rejected: undiscovered uids have
  unverified runner support and cannot be resolved by `--filter`.
