# Lineage attributes redesign — subject-side declaration

**Date:** 2026-06-24
**Status:** Design — pending review

## Problem

Lineage relations (one subject *references* or *consumes* another) are declared on
the **target parameter**, pointing back at the **subject** that holds the relation.
You annotate the inputs, but every annotation describes the *output*. It reads
backwards:

```csharp
[return: Creates]
Task<Appointment> Book(
    [References(Subject.Return)] User user,   // "user is referenced-BY the return"
    [Consumes(Subject.Return)]  Slot slot)    // "slot is consumed-BY the return"
{ ... }
```

To know what an Appointment is made of, you have to scan every parameter and mentally
invert each `Subject.Return`. The relation belongs on the thing that owns it.

## Goal

Move lineage to the **subject side**: the produced/mutated resource declares which
inputs flowed into it, via named properties on its lifecycle-role attribute.

```csharp
[return: Created(References = [nameof(user)], Consumes = [nameof(slot)])]
Task<Appointment> Book(User user, Slot slot) { ... }
```

Reads forward: *"creates an Appointment that references `user` and consumes `slot`."*
This applies to **every role that establishes a subject**, not just create.

## Design

### Two axes, separated

The current seven attributes conflate two unrelated ideas. Split them:

**Lifecycle role** — what the step does to *this* resource (exactly one per
return/parameter):

| Attribute | Position | Meaning | New subject node? |
|---|---|---|---|
| `[Created]` | return / method | step fabricates a brand-new resource | yes (provenance: new) |
| `[Loaded]`  | return / method | step pulls in a **pre-existing** resource | yes (provenance: existed) |
| `[Edited]`  | parameter / return / method | step mutates an existing resource | no |
| `[Read]`    | parameter | step observes, no change | no |
| `[Deleted]` | parameter | step removes a resource | no |

**Lineage** — how *this* subject relates to other subjects. No longer attributes;
they become **array properties** on the subject-establishing roles:

| Property | Records |
|---|---|
| `References = [nameof(x), ...]` | a `Reference` relation: this subject → x |
| `Consumes   = [nameof(x), ...]` | a `Consume` relation: this subject → x |

`References` / `Consumes` exist **only** on `[Created]`, `[Loaded]`, `[Edited]`. Because
they are absent from `[Read]` / `[Deleted]`, declaring lineage on a non-subject role is
a compile error by construction — no runtime/analyzer check needed for that case.

**Naming a target confers its role (behavior-preserving).** Being listed in a producer's
`References`/`Consumes` *is* the target parameter's role: it gets the `Reference`/`Consume`
lifecycle effect (the same shared lock as today) **and** the lineage relation. The target
stays a **bare parameter** with no attribute of its own — `Book(User user, Slot slot)`,
not `Book([Read] User user, ...)`. So `RAUN009` (every resource param must declare its
access) treats "named as some producer's Reference/Consume target" as a declared role.
This is exactly the old `Resources.Reference(target, subject)` runtime call relocated from
the target to the subject — identical effects, identical lineage, only the declaration
site moves. A target may instead be `Subject.Return` (the producer references/consumes the
step's own return), kept for the rare `[Edited]`-param case.

### Naming

Participle style (`Created` / `Loaded` / `Edited` / `Read` / `Deleted`). Attributes
describe the *state* of the element they annotate, which reads naturally on a
return/parameter noun, and `Created` vs `Loaded` encodes provenance in the name itself.

### Target tokens

Property values name the **input parameters** that flowed into the subject. The subject
is whatever the attribute is on, so `Subject.Return` is no longer needed to name the
subject. It is retained only as a valid *target* token for the rare case of an
`[Edited]` parameter that references/consumes the step's own return value:

```csharp
// acc, after editing, now references the thing this step returns
Task<Receipt> Settle([Edited(References = [Subject.Return])] Account acc) { ... }
```

### Worked examples (before → after)

```csharp
// 1. create referencing + consuming two inputs
[return: Creates]
Task<Appointment> Book([References(Subject.Return)] User user,
                       [Consumes(Subject.Return)]  Slot slot) { ... }
// becomes
[return: Created(References = [nameof(user)], Consumes = [nameof(slot)])]
Task<Appointment> Book(User user, Slot slot) { ... }

// 2. edited subject gains a reference to an input
Task Assign([Edits] Account acc, [References(nameof(acc))] User who) { ... }
// becomes
Task Assign([Edited(References = [nameof(who)])] Account acc, User who) { ... }

// 3. loaded (pre-existing) subject with provenance lineage
[return: Loaded(References = [nameof(owner)])]
Task<Account> OpenExisting(User owner) { ... }
```

Note example 2 also shows the multi-subject case collapsing: previously a single input
could be `[References(a, b)]` (referenced by two subjects); now each subject `a`, `b`
lists the input in its own `References`. Lineage distributes to its owners.

## Domain & runtime — unchanged

`ResourceLineageRelation { Subject, Target, Kind }`, `LifecycleVerb.Reference/Consume`,
and `ResourceContext.Reference/Consume(target, params subjects)` stay exactly as they
are. This is a **front-end-only** redesign: the relation that gets recorded is
identical; only the surface that declares it moves from target to subject.

## Generator changes

The flip is localized; the emission path is reused.

1. **Attribute definitions** (`ResourceRoleAttributes.cs`): rename `Creates/Loads/Edits/
   Reads/Deletes` → `Created/Loaded/Edited/Read/Deleted`. Delete `ReferencesAttribute`
   and `ConsumesAttribute`. Add `string[] References` and `string[] Consumes` properties
   to `Created`, `Loaded`, `Edited`.
2. **AttributeReader**: replace `ParameterSubjects` (read off `[References]/[Consumes]`
   constructor args) with a reader that pulls the `References`/`Consumes` **named
   properties** off a `Created/Loaded/Edited` attribute.
3. **ScenarioParser**: when a subject-establishing claim declares `References`/`Consumes`,
   synthesize one `Reference`/`Consume` resource claim per named target where
   `expression = target parameter's argument expression` and
   `SubjectExpressions = [the producer's resource expression]` (the return var `__r`, or
   the edited parameter). This **reuses** the existing claim shape, so `ScenarioEmitter`
   is untouched — it still emits `await __ctx.Resources.Reference(target, subject)`.
4. **ScenarioAnalyzer / RAUN010**: validation inverts. Each name in `References`/`Consumes`
   must resolve to a **parameter of the step** (or `Subject.Return`). A subject naming
   **itself** is an error. Update the RAUN010 message to the subject-side framing
   (e.g. *"'{0}' is not an input of step '{1}' — References/Consumes must name a
   parameter or Subject.Return"*). Add a self-reference diagnostic if it warrants a
   distinct ID; otherwise fold into RAUN010.

## Migration

- Update all sample/test usages (`SampleSources.cs`, `AnalyzerTests.cs`, others) to the
  new attributes and property syntax.
- Regenerate generator snapshot baselines.
- Sweep docs/README for the old `[References]`/`[Consumes]`/`Subject.Return` syntax.
- This is a **breaking DSL change** with no compatibility shim — pre-release project,
  full replacement (no deprecated alias path).

## Testing

- **Analyzer**: RAUN010 fires for an unknown target name; for a self-reference; passes
  for valid param targets and `Subject.Return`. Lineage props on `[Read]`/`[Deleted]`
  fail to compile (the properties don't exist) — assert via a does-not-compile fixture.
- **Generator (snapshot)**: `[Created]` with both `References` and `Consumes`; `[Edited]`
  param with `References`; `[Loaded]` with lineage; a step with two producers each
  carrying their own lineage; confirm emitted `Resources.Reference/Consume(target,
  subject)` calls match the pre-redesign relations.
- **Runtime/behavioral**: an end-to-end scenario asserting the recorded
  `ResourceLineageRelation` set is identical to what the old syntax produced (proves the
  redesign is surface-only).

## Out of scope

- New lineage *kinds* beyond Reference/Consume (the `...` in the original sketch). The
  property model makes adding more trivial later; none are added now.
- Any change to the HTML report's rendering of relations.
