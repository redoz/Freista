# Rename PUnit → Freista Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the project, packages, namespaces, types, diagnostics, build knobs, and living docs from **PUnit** to **Freista** (Old Norse "to put to the test") across all code + build + README/CLAUDE, leaving the historical design docs as dated records.

**Architecture:** A mechanical-but-broad rename driven by a precise 4-rule replacement matrix (below). Because the four cases use different letter-casing and one maps to a *different* string (`PUNIT###` → `FRST###`, not `Freista###`), they are applied as distinct passes: **Task 1** does the three non-`PUnit`-Pascal special cases (diagnostic IDs, the report token, lowercase `punit`) so they aren't mangled; **Task 2** does the bulk case-sensitive `PUnit`→`Freista` plus all folder/file/project/`.slnx` renames and regenerates the Verify snapshots; **Task 3** rewrites the README origin prose + CLAUDE.md and runs a final no-stray-`PUnit` audit and `dotnet pack` sanity. The public DSL surface (`Given`/`When`/`Then`/`[Scenario]`/`[StepName]`/`ScenarioContext`) contains no "PUnit", so consumer source only changes `using PUnit;` → `using Freista;`.

**Tech Stack:** .NET 10 / C# 14, Microsoft.Testing.Platform, Roslyn incremental generator (netstandard2.0), Verify snapshot tests, `dotnet` CLI, `jj` for VCS, Git Bash for the scripted replacements.

## Global Constraints

- **PREREQUISITE — quiescent tree.** A repo-wide rename conflicts with *every* in-flight PUnit-named change. Before starting, confirm there is **no unmerged PUnit work** (e.g. the `punit010` lineage workspace noted in project memory must be merged to `main` or abandoned first). Do the rename on a clean `main` with no other branches pending. If unmerged PUnit work exists, STOP and surface it — do not start.
- **The replacement matrix (apply EXACTLY these — case-sensitive):**
  | # | From | To | Casing | Where |
  |---|---|---|---|---|
  | R1 | `PUNIT000`…`PUNIT010` (and any `PUNIT###`) | `FRST000`…`FRST010` | ALL-CAPS, **prefix→FRST**, digits unchanged | diagnostic IDs |
  | R2 | `__PUNIT_REPORT_JSON__` | `__FREISTA_REPORT_JSON__` | ALL-CAPS token | report JSON token |
  | R3 | `punit` | `freista` | lowercase whole-word | Uids / lowercase ids |
  | R4 | `PUnit` | `Freista` | PascalCase | everything else: namespaces, types, MSBuild props, `<Product>`, file/folder names, `.slnx`, template title, README/CLAUDE |
  R1/R2/R3 target distinct casings from R4, so R4's blind `PUnit`→`Freista` never touches them. Apply R1–R3 in Task 1, R4 in Task 2.
- **Rename scope (DECIDED):** code + build (`src/**`, `test/**`, `samples/**`, `Directory.Build.props`, `Directory.Packages.props`, `*.slnx`, `.editorconfig`) **and living docs only** (`README.md`, `CLAUDE.md`). **Do NOT touch `docs/**`** (historical specs/plans/handoffs are dated records — they stay as written, including this plan and any quoted `__PUNIT_REPORT_JSON__`).
- **DSL surface unchanged:** `Given`/`When`/`Then`/`[Scenario]`/`[StepName]`/`ScenarioContext` keep their names. Only the namespace + package id change.
- **Verify snapshots WILL change** (the generated code's namespace/class rename). This is expected and correct — regenerate and accept them (Task 2). This is the opposite of the report-feature plans where the snapshot must NOT change.
- **0-warning build** (`dotnet build PUnit.slnx -warnaserror`, becoming `Freista.slnx` after Task 2). Full suite **248/248** (`dotnet test … -c Debug`, no `--nologo` — MTP rejects it).
- **VCS: `jj` only** — never `git` mutations. Rename files/folders with plain shell `mv` (jj auto-snapshots the rename); never `git mv`. Commit with `jj commit -m "…"`. **No `Co-Authored-By` / tooling trailers.** Do not move `main` (a separate finishing step does that with consent).

---

## Inventory (the touchpoints, for reference)

| Area | Files | Rule(s) |
|---|---|---|
| Diagnostic IDs + category | `src/PUnit.Generator/Analysis/Descriptors.cs`, `AnalyzerReleases.Shipped.md`, `AnalyzerReleases.Unshipped.md`, `test/PUnit.Generator.Test/AnalyzerTests.cs`, `GeneratorSafety.cs`/`ScenarioParser.cs` comments, `.editorconfig` (if it sets `dotnet_diagnostic.PUNIT*`) | R1 (IDs), R4 (category `PUnit.Usage`→`Freista.Usage`, in Task 2) |
| Report token | `src/PUnit.Mtp/HtmlReport/HtmlReportSink.cs` (`JsonToken` const), `report-template.html:310`, `test/PUnit.Mtp.Test/HtmlReportSinkTests.cs` | R2 |
| Lowercase ids | `src/PUnit.Mtp/HtmlReport/HtmlReportOptionsProvider.cs` (`Uid => "punit.mtp.htmlreport"`), framework `ExtensionUid`/Uid if lowercase | R3 |
| Namespaces / usings / types | ~503 `.cs` matches; PUnit-prefixed **types**: `PUnitTestFramework`, `PUnitTestApplication`, `PUnitDiscoverer`, `PUnitRunLoop`, `PUnitProgram`, `PUnitGenerated`, `PUnitScenarios` (generated), test classes | R4 |
| MSBuild knobs | `PUnitRoslynVersion`, `PUnitGenerateProgram` (Directory.Build.props + AppointmentTests.csproj + `CompilerVisibleProperty`) | R4 |
| Build/package metadata | `Directory.Build.props` (`<Product>PUnit</Product>`, Roslyn-variant comment), each `.csproj` `<Description>` | R4 |
| Consumer MSBuild | `src/PUnit.Mtp/buildTransitive/PUnit.Mtp.props` + `.targets` (filename **must** match package id) | R4 (file + content) |
| Template UI strings | `report-template.html:7,296` ("PUnit run report") | R4 |
| Folders / projects / solution | `src/PUnit*`, `test/PUnit*`, all `.csproj`, `PUnit.slnx` | R4 (renames) |
| Verify snapshots | `test/PUnit.Generator.Test/Snapshots/*#PUnitScenarios*.g.verified.cs` (6), `EntryPoint.verified.txt` | R4 (content + filename), regenerate |
| Living docs | `README.md` (incl. the "pun-it" origin paragraph — needs prose rewrite), `CLAUDE.md` | R4 + manual prose (Task 3) |

---

## Task 1: Diagnostic IDs (R1), report token (R2), lowercase ids (R3)

These three passes are surgical and keep the build green; doing them first means Task 2's blind `PUnit`→`Freista` can't mangle the ALL-CAPS/lowercase forms.

**Files:**
- Modify: `src/PUnit.Generator/Analysis/Descriptors.cs`, `src/PUnit.Generator/AnalyzerReleases.Shipped.md`, `src/PUnit.Generator/AnalyzerReleases.Unshipped.md`, `src/PUnit.Generator/GeneratorSafety.cs`, `src/PUnit.Generator/Lowering/ScenarioParser.cs`, `test/PUnit.Generator.Test/AnalyzerTests.cs`, `.editorconfig` (if present + references `PUNIT`), `src/PUnit.Mtp/HtmlReport/HtmlReportSink.cs`, `src/PUnit.Mtp/HtmlReport/report-template.html`, `test/PUnit.Mtp.Test/HtmlReportSinkTests.cs`, `src/PUnit.Mtp/HtmlReport/HtmlReportOptionsProvider.cs`

**Interfaces:**
- Produces: diagnostic IDs `FRST000..FRST010`; the report token `/*__FREISTA_REPORT_JSON__*/`; lowercase id `freista.mtp.htmlreport`. Task 2 must NOT reintroduce `PUNIT`/`__PUNIT_`/lowercase `punit`.

- [ ] **Step 1: R1 — rename diagnostic IDs `PUNIT###` → `FRST###`** (digits unchanged). Run from repo root:

```bash
cd /c/dev/punit
# diagnostic IDs live in code, the analyzer-release tracking files, tests, comments, and possibly .editorconfig.
FILES="src/PUnit.Generator/Analysis/Descriptors.cs \
  src/PUnit.Generator/AnalyzerReleases.Shipped.md src/PUnit.Generator/AnalyzerReleases.Unshipped.md \
  src/PUnit.Generator/GeneratorSafety.cs src/PUnit.Generator/Lowering/ScenarioParser.cs \
  test/PUnit.Generator.Test/AnalyzerTests.cs"
[ -f .editorconfig ] && FILES="$FILES .editorconfig"
sed -i -E 's/PUNIT([0-9]{3})/FRST\1/g' $FILES
# sanity: zero PUNIT### left, and FRST### now present
git grep -nE 'PUNIT[0-9]{3}' -- src test '.editorconfig' || echo "OK: no PUNIT### remain in code"
git grep -nE 'FRST[0-9]{3}' -- 'src/PUnit.Generator/Analysis/Descriptors.cs' | head -3
```

Note: `AnalyzerReleases.*.md` rows are `FRST000 | PUnit.Usage | Error | …` — the **category** `PUnit.Usage` stays PUnit here and is renamed to `Freista.Usage` by Task 2's R4 (in both `Descriptors.cs` and these files together), so they stay consistent at each task boundary. Do NOT change the category in this task.

- [ ] **Step 2: R2 — rename the report token** `__PUNIT_REPORT_JSON__` → `__FREISTA_REPORT_JSON__` in the three code spots:

```bash
sed -i 's/__PUNIT_REPORT_JSON__/__FREISTA_REPORT_JSON__/g' \
  src/PUnit.Mtp/HtmlReport/HtmlReportSink.cs \
  src/PUnit.Mtp/HtmlReport/report-template.html \
  test/PUnit.Mtp.Test/HtmlReportSinkTests.cs
git grep -n '__PUNIT_REPORT_JSON__' -- src test || echo "OK: token renamed in code (historical docs keep the old token by decision)"
```

- [ ] **Step 3: R3 — rename lowercase `punit` → `freista`** (the MTP option Uid, and any other lowercase id in code):

```bash
git grep -nw 'punit' -- src test | grep -v -E 'PUnit|PUNIT'   # show every lowercase 'punit' first (review the list)
sed -i 's/\bpunit\b/freista/g' src/PUnit.Mtp/HtmlReport/HtmlReportOptionsProvider.cs
# also catch dotted lowercase ids like "punit.mtp.htmlreport"
sed -i 's/punit\.mtp/freista.mtp/g' src/PUnit.Mtp/HtmlReport/HtmlReportOptionsProvider.cs
git grep -n 'punit' -- src test | grep -v -E 'PUnit|PUNIT' || echo "OK: no lowercase punit left in code"
```

(If Step 3's first grep surfaces lowercase `punit` in other files — e.g. a framework `ExtensionUid` literal — sed those too, then re-run the final grep.)

- [ ] **Step 4: Build + test (still green — these are string/ID renames only)**

```bash
dotnet build PUnit.slnx -warnaserror -c Debug 2>&1 | tail -3   # expect: 0 Warning(s) 0 Error(s)
dotnet test PUnit.slnx -c Debug 2>&1 | tail -4                 # expect: 248/248 (analyzer tests now assert FRST###; token test green)
```

Expected: 0 warnings; 248 passed. The `AnalyzerTests` assertions (`AssertHas(diagnostics, "FRST001")`) and the `HtmlReportSinkTests` token assertion (`DoesNotContain("__FREISTA_REPORT_JSON__"…)`) pass because Step 1–2 changed the descriptor IDs and the template token in lockstep.

- [ ] **Step 5: Commit**

```bash
jj commit -m "rename: diagnostic IDs PUNIT### -> FRST###, report token + lowercase mtp uid -> freista"
```

---

## Task 2: Bulk `PUnit`→`Freista` (R4) + folder/file/project/.slnx renames + snapshot regen

The atomic core. Rename the directory/file/project structure first, then a single case-sensitive `PUnit`→`Freista` over the code/build/living-doc file set, then regenerate the Verify snapshots. The tree does not build mid-task; it ends green.

**Files:** all of `src/**`, `test/**`, `samples/**`, `Directory.Build.props`, `Directory.Packages.props`, `README.md`, `CLAUDE.md`, `.editorconfig`, and `PUnit.slnx` (→ `Freista.slnx`). **Excludes `docs/**`.**

**Interfaces:**
- Produces: namespaces `Freista`, `Freista.Generator`, `Freista.Mtp`; types `FreistaTestFramework`/`FreistaTestApplication`/`FreistaDiscoverer`/`FreistaRunLoop`/`FreistaProgram`/`FreistaGenerated`/`FreistaScenarios`; MSBuild props `FreistaRoslynVersion`/`FreistaGenerateProgram`; package ids `Freista`/`Freista.Generator`/`Freista.Mtp`; `Freista.slnx`. Consumer entry point becomes `Freista.Mtp.FreistaTestApplication.RunAsync`.

- [ ] **Step 1: Rename folders + project files + buildTransitive + solution (plain `mv`, jj auto-snapshots)**

```bash
cd /c/dev/punit
# project folders
mv src/PUnit               src/Freista
mv src/PUnit.Generator     src/Freista.Generator
mv src/PUnit.Mtp           src/Freista.Mtp
mv test/PUnit.Test         test/Freista.Test
mv test/PUnit.Generator.Test test/Freista.Generator.Test
mv test/PUnit.Mtp.Test     test/Freista.Mtp.Test
# .csproj files (folder names already changed above)
mv src/Freista/PUnit.csproj                       src/Freista/Freista.csproj
mv src/Freista.Generator/PUnit.Generator.csproj   src/Freista.Generator/Freista.Generator.csproj
mv src/Freista.Mtp/PUnit.Mtp.csproj               src/Freista.Mtp/Freista.Mtp.csproj
mv test/Freista.Test/PUnit.Test.csproj            test/Freista.Test/Freista.Test.csproj
mv test/Freista.Generator.Test/PUnit.Generator.Test.csproj test/Freista.Generator.Test/Freista.Generator.Test.csproj
mv test/Freista.Mtp.Test/PUnit.Mtp.Test.csproj    test/Freista.Mtp.Test/Freista.Mtp.Test.csproj
# consumer-facing buildTransitive files — names MUST equal the package id (Freista.Mtp)
mv src/Freista.Mtp/buildTransitive/PUnit.Mtp.props   src/Freista.Mtp/buildTransitive/Freista.Mtp.props
mv src/Freista.Mtp/buildTransitive/PUnit.Mtp.targets src/Freista.Mtp/buildTransitive/Freista.Mtp.targets
# Verify snapshot files carry the generated class name in their filename (#PUnitScenarios)
for f in test/Freista.Generator.Test/Snapshots/*'#PUnitScenarios'*; do
  mv "$f" "${f/'#PUnitScenarios'/'#FreistaScenarios'}"
done
# solution file
mv PUnit.slnx Freista.slnx
ls src test                       # confirm: Freista* folders, no PUnit* folders
```

- [ ] **Step 2: Bulk case-sensitive `PUnit`→`Freista` over the code/build/living-doc tree (NOT docs/)**

```bash
cd /c/dev/punit
find src test samples README.md CLAUDE.md Directory.Build.props Directory.Packages.props Freista.slnx \
  -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' -o -name '*.targets' \
             -o -name '*.slnx' -o -name '*.md' -o -name '*.html' -o -name '*.txt' \
             -o -name '*.json' -o -name '*.editorconfig' \) \
  -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
| xargs -0 sed -i 's/PUnit/Freista/g'
[ -f .editorconfig ] && sed -i 's/PUnit/Freista/g' .editorconfig
# audits: no PUnit (Pascal) anywhere in code/build; PUNIT/punit already gone from Task 1
git grep -n 'PUnit' -- src test samples README.md CLAUDE.md '*.slnx' Directory.Build.props Directory.Packages.props || echo "OK: no Pascal PUnit left in code/build/living-docs"
git grep -n 'Freista.slnx\|src/Freista/Freista.csproj' -- Freista.slnx | head   # .slnx project paths updated to new folders/files
```

Note: this also rewrote the README/CLAUDE body and the Verify snapshot *contents* (`namespace PUnit`→`namespace Freista`). The README "pun-it" origin paragraph now reads oddly ("a test framework that's a Freista…") — that prose rewrite is Task 3.

- [ ] **Step 3: Restore + verify the Verify snapshots**

The snapshot file *contents* were sed'd to `Freista` and the *filenames* renamed to `#FreistaScenarios` in Steps 1–2, so they should already match the new generator output. Confirm by running the generator snapshot tests:

```bash
dotnet test test/Freista.Generator.Test -c Debug 2>&1 | tail -6
```

- If **green**: snapshots matched — done.
- If **red** (Verify mismatch → `*.received.*` files written): inspect the diff — it must be *only* incidental rename fallout, nothing semantic:
  ```bash
  for r in $(find test/Freista.Generator.Test/Snapshots -name '*.received.*'); do echo "== $r =="; diff "${r/.received./.verified.}" "$r" || true; done
  ```
  If the diff is rename-only, accept it (replace verified with received) and delete any stale `#PUnitScenarios` orphans, then re-run:
  ```bash
  find test/Freista.Generator.Test/Snapshots -name '*#PUnitScenarios*' -delete
  for r in $(find test/Freista.Generator.Test/Snapshots -name '*.received.*'); do mv "$r" "${r/.received./.verified.}"; done
  dotnet test test/Freista.Generator.Test -c Debug 2>&1 | tail -4
  ```

- [ ] **Step 4: Full build + suite green on the renamed tree**

```bash
dotnet build Freista.slnx -warnaserror -c Debug 2>&1 | tail -3   # expect 0 Warning(s) 0 Error(s)
dotnet test  Freista.slnx -c Debug 2>&1 | tail -4                # expect 248/248
```

If the build fails, the usual causes are: a missed `.slnx` project path (Step 2 grep), a buildTransitive file whose name ≠ package id (Step 1), or a stale `obj/` from the old name — `find src test samples -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +` then rebuild.

- [ ] **Step 5: Re-emit the report and eyeball the rename in the UI**

```bash
dotnet run --project samples/AppointmentTests -c Debug -- --report-html 2>&1 | tail -2
R="C:/dev/punit/samples/AppointmentTests/bin/Debug/net10.0/TestResults/punit-report.html"
npx playwright screenshot --browser=chromium --full-page --viewport-size=1180,1600 "file:///$R?theme=dark" rename-check.png 2>&1 | tail -1
```
Expect the header/title to read **"Freista run report"** and the page to render (the `__FREISTA_REPORT_JSON__` token was replaced — no raw token visible). (The output html filename stays `punit-report.html` unless you also renamed the sink's output path; check `HtmlReportPath`/sink for a `punit-report` literal and rename to `freista-report.html` if present — fold into Step 2's grep: `git grep -n 'punit-report' -- src` and sed if found.)

- [ ] **Step 6: Commit**

```bash
jj commit -m "rename: PUnit -> Freista across namespaces, types, projects, build, solution, and snapshots"
```

---

## Task 3: README cleanup + CLAUDE.md + final audit + pack sanity

**Files:** Modify `README.md`, `CLAUDE.md`; no code changes beyond doc prose.

- [ ] **Step 1: Delete the README naming-origin blockquote (no naming story at all).** Task 2's blind replace mangled the old "pun-it / Patrik" origin line into nonsense. **Remove it entirely** — do not replace it with a Freista/Old-Norse story; the README carries no naming anthology. Delete the whole origin blockquote (the line beginning `> The name? …`):

```bash
cd /c/dev/punit
sed -i '/^> The name?/d' README.md
git grep -nE 'pun-it|The name\?|naming' -- README.md || echo "OK: no naming origin left in README"
```

Then scan the README top matter for any *other* awkwardness the blind replace produced — in particular the stale `> Scenario tests for xUnit v3` tagline (the framework is now its own MTP host, not an xUnit add-on). Update that tagline to `> Scenario / integration tests for .NET — Given/When/Then steps, each reported as its own test, wired into a fork/join dependency graph.` Keep all code blocks (`using Freista;`, `dotnet add package Freista.Mtp`) intact. This is wording cleanup only — no naming story.

- [ ] **Step 2: Verify CLAUDE.md reads correctly.** Confirm the `## Project` line now says *"`Freista`: a Microsoft.Testing.Platform test framework …"* and the build/test commands reference `Freista.slnx`. Fix any awkward blind-replace artifacts.

- [ ] **Step 3: Final no-stray-`PUnit` audit.** The only remaining PUnit/PUNIT/punit in the repo should be in `docs/**` (historical, by decision) and this plan:

```bash
cd /c/dev/punit
echo "=== any PUnit/PUNIT/punit OUTSIDE docs/ (should be empty) ==="
git grep -nI -i punit -- ':!docs/' ':!*.png' || echo "CLEAN: no punit outside docs/"
echo "=== confirm new identifiers present ==="
git grep -nE 'FRST[0-9]{3}' -- src/Freista.Generator/Analysis/Descriptors.cs | head -1
git grep -n 'namespace Freista' -- src/Freista/*.cs | head -1
```

Expected: the first grep prints CLEAN (nothing outside `docs/`). If anything shows, sed/edit it (respecting the R1–R4 matrix) and re-run.

- [ ] **Step 4: Pack sanity — the NuGet ids + buildTransitive resolve under the new name**

```bash
dotnet pack src/Freista.Mtp/Freista.Mtp.csproj -c Release -o ./_packout 2>&1 | tail -4
ls _packout/                          # expect Freista.Mtp.*.nupkg (+ Freista.Generator if packed transitively)
rm -rf _packout                       # gitignored scratch anyway
```

Expect `Freista.Mtp.<version>.nupkg`. (If pack complains the buildTransitive file name doesn't match the package id, re-check Task 2 Step 1's `mv` of `buildTransitive/Freista.Mtp.{props,targets}`.)

- [ ] **Step 5: Build + full suite one last time**

```bash
dotnet build Freista.slnx -warnaserror -c Debug 2>&1 | tail -3   # 0 warnings
dotnet test  Freista.slnx -c Debug 2>&1 | tail -4                # 248/248
```

- [ ] **Step 6: Commit**

```bash
jj commit -m "rename: drop README naming-origin blockquote + CLAUDE.md + final PUnit->Freista audit"
```

---

## Self-review (run before handing off)

1. **Matrix coverage:** R1 diagnostic IDs (Task 1 §1) ✓; R2 token (Task 1 §2) ✓; R3 lowercase uid (Task 1 §3) ✓; R4 bulk + category `PUnit.Usage`→`Freista.Usage` (Task 2 §2, in Descriptors + AnalyzerReleases together) ✓. Folder/file/project/.slnx/buildTransitive/snapshot-filename renames (Task 2 §1) ✓. Snapshot content + regen (Task 2 §2–3) ✓. Living docs README/CLAUDE (Task 2 bulk + Task 3 prose) ✓. Historical `docs/**` untouched (excluded from every file set; final audit allows `docs/` only) ✓.
2. **Placeholder scan:** every step is a concrete command or a concrete edit; the one prose edit (README origin) gives the exact replacement text. No "TODO"/"handle the rest".
3. **Name consistency:** `FRST###` (not `FREISTA###`/`Freista###`) for diagnostics; `__FREISTA_REPORT_JSON__` token; `freista.mtp.htmlreport` uid; `Freista`/`Freista.Generator`/`Freista.Mtp` namespaces+packages; `FreistaTestApplication`/`FreistaTestFramework`/`FreistaScenarios` types; `FreistaRoslynVersion`/`FreistaGenerateProgram` MSBuild props; `Freista.slnx`. The buildTransitive files are named exactly `Freista.Mtp.props`/`.targets` to match the package id.
4. **Build-green boundaries:** Task 1 keeps the build green (ID/token/uid strings only). Task 2 is atomic (red mid-task, green at §4). Task 3 is docs + audit (green throughout). Each task ends with `-warnaserror` + 248/248.

---

## Execution Handoff

Two execution options:

1. **Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks (REQUIRED SUB-SKILL: superpowers:subagent-driven-development). Reviews here are audit-based (grep for stray `PUnit`, build/test green, snapshot diff is rename-only), not line-by-line of the large mechanical diff.
2. **Inline Execution** — execute in this session with checkpoints (REQUIRED SUB-SKILL: superpowers:executing-plans).

Either way: the per-task gate is `dotnet build Freista.slnx -warnaserror` (0 warnings) + `dotnet test Freista.slnx -c Debug` (248/248) + the stray-`PUnit` grep. `jj`-only, no trailers. After all tasks: update the `project-rename-freista` memory to "DONE / shipped", refresh `CLAUDE.md` is already done in Task 2/3, and land via `superpowers:finishing-a-development-branch` (advance `main` only with consent; local-only, no remote).
</content>
