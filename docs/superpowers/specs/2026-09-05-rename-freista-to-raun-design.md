# Rename: Freista → Raun

- **Date:** 2026-09-05
- **Status:** Done the same day.
- **Why:** A web search for "Freista" is dominated by an adult content creator's Fansly and X
  accounts. A test framework whose name you cannot search for at work is not a name. Nothing had
  been published to nuget.org yet — only GitHub Packages previews — so the rename cost was one
  mechanical pass, the second one this project has had (see `2026-06-22-rename-punit-to-freista-design.md`).

## The name

**Raun** — Old Norse *raun*: a trial, a test, proof gained by experience. Same semantic family as
*freista* (to try, to put to the test), shorter, and the noun rather than the verb: a scenario run
*is* the trial.

Vetted before choosing, against the alternatives Vitna (to bear witness), Granska (Swedish: to
scrutinize), Sanna (to prove true), Kanna, and Profa:

| Check | Result for Raun |
|---|---|
| Google | Scots word for fish roe, a UN academy, a musician. Nothing embarrassing, nothing dominant. |
| nuget.org | no package ids containing `raun` |
| GitHub | one unrelated 16-star repository |

Kanna lost to a 2.4k-star Swift library, Profa to profanity-filter noise, Sanna to being a common
first name, Granska to a KTH grammar checker of the same name; Vitna was the runner-up.

## What changed

Mechanical, case-preserving replacement in every tracked file except the two historical
PUnit→Freista rename documents, then path renames deepest-first:

- `FREISTA` → `RAUN`, `Freista` → `Raun`, `freista` → `raun`.
- Diagnostic prefix `FRST` → `RAUN` (`RAUN000`…`RAUN014`), analyzer category `Raun.Usage`,
  `AnalyzerReleases.*.md` rows.
- Projects, folders, solution (`Raun.slnx`), namespaces, type names (`RaunTestApplication`,
  `RaunAspire`, `RaunRunLoop`, …), MSBuild property `RaunGenerateProgram`, buildTransitive
  `Raun.Mtp.props/.targets`, generated hint names `RaunScenarios.g.cs` / `RaunProgram.g.cs`, the
  HTML report token and default filename `raun-report.html`, the attachment temp directory
  `raun-mtp`, package ids `Raun`, `Raun.Mtp`, `Raun.Aspire`, repository URLs.
- Verify snapshot files renamed to match the new hint name; contents updated by the same pass.

Stable ids did not move: `StableId` hashes the user's scenario method names, never the framework's.

## Not part of the pass

- The GitHub repository rename (`redoz/Freista` → `redoz/Raun`) and deleting the `Freista*`
  packages already on GitHub Packages. Both are account actions, done by hand.
- The two historical rename documents keep their original wording.
