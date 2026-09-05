# Resource conflict detection — Implementation plan

**Spec:** `docs/superpowers/specs/2026-09-05-resource-conflict-detection-design.md`
**Goal:** Ship RAUN013 (compile-time parallel-access conflicts) and the scenario-scoped runtime
conflict ledger, with parameter-role claims emitted before the DSL call. No locks, no waiting.

Each task: failing test first, minimal implementation, `dotnet test Raun.slnx` green, one `jj commit`.

## Task 1 — RAUN013 analyzer rule

Files: `src/Raun.Generator/Analysis/Descriptors.cs`, `ScenarioAnalyzer.cs`,
`test/Raun.Generator.Test/AnalyzerTests.cs`.

1. Add `Descriptors.ConflictingParallelAccess` ("RAUN013", Error) and register it in
   `SupportedDiagnostics`.
2. Tests (self-contained sources, as the RAUN009 tests do): supported-diagnostic; tuple Edit/Edit;
   tuple Edit/Read; tuple Read/Read clean; different locals clean; sequential clean; lineage target vs
   Edited; LINQ count 2 self-conflict; LINQ count 1 clean; named-argument matching; all existing
   `SampleSources` scenarios stay clean.
3. Implement `AnalyzeGroupConflicts(context, IReadOnlyList<InvocationExpressionSyntax> elements,
   stepOutputs)` called from the tuple and array branches of `AnalyzeAwaited`, and a LINQ variant
   called from `AnalyzeLinqArray` with the constant count. Argument→parameter matching mirrors
   `ScenarioParser.FindArgument`. Role lookup via `AttributeReader.ParameterRole` and
   `AttributeReader.ProducerLineage`.

## Task 2 — `ResourceLedger` + `ResourceConflictException`

Files: `src/Raun/Resources/ResourceLedger.cs`, `ResourceConflictException.cs`,
`test/Raun.Test/Resources/ResourceLedgerTests.cs`.

1. Tests per spec "Ledger".
2. Ledger computes transitive ancestors over `DependsOn ∪ MergeSources ∪ Guards.ConditionIndex`
   with `bool[,]` or `BitArray[]`; `Claim` under one lock.

## Task 3 — Wire the ledger through `ResourceContext`, `ScenarioContext`, `ScenarioScheduler`

Files: `ResourceContext.cs`, `ScenarioContext.cs`, `Scheduling/ScenarioScheduler.cs`,
`test/Raun.Test/SchedulerTests.cs`.

1. Scheduler tests per spec "Scheduler".
2. `ResourceContext.AttachLedger` (internal) and the pre-record `Claim`; `ScenarioContext.AttachLedger`
   forwards; scheduler builds one ledger per run and attaches in `RunNodeAsync`.
3. Update `LockMode`, `ResourceContext`, `ScenarioContext.Resources` docs.

## Task 4 — Generator: parameter-role claims before the call

Files: `src/Raun.Generator/Emit/ScenarioEmitter.cs`, `test/Raun.Generator.Test/ResourceLoweringTests.cs`,
`Snapshots/GeneratorSnapshotTests.Resource_scenario#RaunScenarios.g.verified.cs`.

1. Test: emitted source has `Resources.Edit(__inputs...)` before `var __r = await When.Suspend`, and
   `Resources.Edit(__r)` after. Existing effect-order tests keep passing.
2. Split `step.ResourceClaims` into pre-call (no `__r` in `Expression` or `SubjectExpressions`) and
   post-call; emit around `callStmt`.
3. Re-verify the snapshot (accept the `.received` as `.verified` after reviewing the diff).

## Task 5 — Docs, findings status, final verification

1. Findings doc status → resolved; pointer to the spec.
2. `dotnet build Raun.slnx` → 0 warnings. `dotnet test Raun.slnx` → all green, count > 386.
3. Both samples run: AppointmentTests 28/29 (unchanged expectation), AspireAppointments 9/9.
