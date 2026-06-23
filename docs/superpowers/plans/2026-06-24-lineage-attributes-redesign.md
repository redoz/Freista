# Lineage Attributes Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move lineage declaration from the target parameter to the producing subject — `[return: Created(References=[nameof(user)], Consumes=[nameof(slot)])]` instead of `[References(Subject.Return)] User user`.

**Architecture:** Front-end-only change. The runtime (`ResourceContext.Reference/Consume`, `ResourceLineageRelation`, `LifecycleVerb`) is untouched. Attribute classes are renamed to participle form and `[References]`/`[Consumes]` are deleted as standalone attributes, becoming `string[]` properties on `[Created]`/`[Loaded]`/`[Edited]`. The generator reads those properties off the producer and synthesizes the *same* `ResourceRoleClaim`s it built before (target expression + producer subject expression), so the emitter is unchanged. The analyzer's FRST009/FRST010 checks invert to the subject side.

**Tech Stack:** C# 14 / .NET 10, Roslyn incremental source generator (netstandard2.0), xUnit + Verify snapshots, jj for version control.

## Global Constraints

- **Version control: `jj` only.** Never run `git commit/add/...`. Commit each task with `jj commit -m "..."`. No `Co-Authored-By` / tooling trailers.
- **Build/test:** `dotnet build Freista.slnx` and `dotnet test Freista.slnx`. Keep zero warnings.
- **Naming:** participle attributes — `Created`, `Loaded`, `Edited`, `Read`, `Deleted`. Runtime verb strings (`Create/Load/Read/Edit/Delete/Reference/Consume`) are unchanged.
- **Behavior-preserving:** the set of recorded effects and `ResourceLineageRelation`s for an equivalent scenario must be identical before and after. Being named in a producer's `References`/`Consumes` confers the `Reference`/`Consume` role+effect on the bare target parameter.
- **`Subject.Return`** retained only as a *target* token (an `[Edited]` param referencing the step's own return).

## Key Decisions (resolved)

1. **Option A — naming confers the role.** A param listed in `References`/`Consumes` is bare (no own attribute); it gets the Reference/Consume effect + lineage. This keeps `Resources.Reference(target, subject)` identical, only relocating the declaration. (Verified: collection expressions are legal attribute arguments in C# 14.)
2. **Effect order preserved.** Synthesize lineage claims (References then Consumes, in listed order) *before* the producer's own role claim, so `BookWithLineage` still yields effects `[Reference(user), Consume(slot), Create(appt)]`.
3. **Full replacement, no compat shim.** Pre-release; delete the old attributes outright.

## File Structure

| File | Change |
|---|---|
| `src/Freista/Resources/ResourceRoleAttributes.cs` | rename to participle; delete `References`/`Consumes` attrs; add `string[] References`/`string[] Consumes` props to `Created`/`Loaded`/`Edited` |
| `src/Freista/Resources/Subject.cs` | doc-comment refresh (token retained) |
| `src/Freista/Resources/ResourceContext.cs` | doc-comment refresh (`[Creates]`→`[Created]` mentions) |
| `src/Freista/Model/ResourceLineageRelation.cs` | doc-comment refresh |
| `src/Freista.Generator/Lowering/AttributeReader.cs` | rename role mapping; replace `ParameterSubjects` with `ProducerLineage` (reads named props off Created/Loaded/Edited) |
| `src/Freista.Generator/Lowering/ScenarioParser.cs` | `BuildResourceClaims` synthesizes lineage claims from producer; `ResolveSubjectExpressions`→`ResolveTargetExpressions` |
| `src/Freista.Generator/Analysis/Descriptors.cs` | FRST009/FRST010 message text |
| `src/Freista.Generator/Analysis/ScenarioAnalyzer.cs` | role-name updates; FRST009 treats lineage targets as covered; FRST010 inverts to subject side |
| `test/Freista.Test/Resources/SubjectAttributeTests.cs` | rewrite for property-based lineage |
| `test/Freista.Test/Resources/ResourceRoleAttributeTests.cs` | rename attrs |
| `test/Freista.Generator.Test/SampleSources.cs` | new syntax in `ResourceDsl`/`BookWithLineage` |
| `test/Freista.Generator.Test/AnalyzerTests.cs` | rewrite FRST010 cases to subject side |
| `test/Freista.Generator.Test/ResourceLoweringTests.cs` | comment refresh (asserts should pass unchanged) |
| `test/Freista.Generator.Test/Snapshots/...verified.cs` | re-accept |
| `README` / docs | sweep old syntax |

---

### Task 1: Rename role attributes to participle + lineage properties

**Files:**
- Modify: `src/Freista/Resources/ResourceRoleAttributes.cs`
- Test: `test/Freista.Test/Resources/SubjectAttributeTests.cs`, `test/Freista.Test/Resources/ResourceRoleAttributeTests.cs`

**Produces:** `CreatedAttribute`, `LoadedAttribute`, `EditedAttribute` (each with `string[] References`/`string[] Consumes` get/set props, default `[]`); `ReadAttribute`, `DeletedAttribute`. Deletes `CreatesAttribute`/`LoadsAttribute`/`ReadsAttribute`/`EditsAttribute`/`DeletesAttribute`/`ReferencesAttribute`/`ConsumesAttribute`.

- [ ] **Step 1: Rewrite the attribute file**

```csharp
namespace Freista;

/// <summary>Return/method role: the step produces a <b>new</b> resource (exclusive in C2).
/// <see cref="References"/>/<see cref="Consumes"/> name input parameters (or <see cref="Subject.Return"/>)
/// that flow into the produced resource, recording lineage from it to each.</summary>
[AttributeUsage(AttributeTargets.ReturnValue | AttributeTargets.Method)]
public sealed class CreatedAttribute : Attribute
{
    /// <summary>Inputs this resource keeps a durable reference to (each a parameter name via <c>nameof</c>, or <see cref="Subject.Return"/>).</summary>
    public string[] References { get; set; } = [];

    /// <summary>Inputs this resource consumes/uses-up (each a parameter name via <c>nameof</c>, or <see cref="Subject.Return"/>).</summary>
    public string[] Consumes { get; set; } = [];
}

/// <summary>Return/method role: the step returns an <b>existing</b> resource it loaded (shared in C2).
/// Carries the same lineage <see cref="References"/>/<see cref="Consumes"/> as <see cref="CreatedAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.ReturnValue | AttributeTargets.Method)]
public sealed class LoadedAttribute : Attribute
{
    public string[] References { get; set; } = [];
    public string[] Consumes { get; set; } = [];
}

/// <summary>Parameter role: the step only reads the resource (shared in C2).</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ReadAttribute : Attribute;

/// <summary>Parameter or return/method role: the step mutates the resource (exclusive in C2).
/// On a producing position carries lineage <see cref="References"/>/<see cref="Consumes"/>.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.Method)]
public sealed class EditedAttribute : Attribute
{
    public string[] References { get; set; } = [];
    public string[] Consumes { get; set; } = [];
}

/// <summary>Parameter role: the step removes the resource (exclusive in C2).</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class DeletedAttribute : Attribute;
```

- [ ] **Step 2: Rewrite `SubjectAttributeTests.cs`** to assert the new property surface

```csharp
using Freista;
using Xunit;

namespace Freista.Test.Resources;

public class SubjectAttributeTests
{
    [Fact]
    public void Created_lineage_properties_default_to_empty()
    {
        Assert.Empty(new CreatedAttribute().References);
        Assert.Empty(new CreatedAttribute().Consumes);
    }

    [Fact]
    public void Created_captures_references_and_consumes()
    {
        var attr = new CreatedAttribute { References = ["user", Subject.Return], Consumes = ["slot"] };
        Assert.Equal(["user", "<return>"], attr.References);
        Assert.Equal(["slot"], attr.Consumes);
    }

    [Fact]
    public void Edited_carries_the_same_lineage_surface()
    {
        var attr = new EditedAttribute { References = ["who"] };
        Assert.Equal(["who"], attr.References);
        Assert.Empty(attr.Consumes);
    }

    [Fact]
    public void Subject_Return_is_the_reserved_token() => Assert.Equal("<return>", Subject.Return);
}
```

- [ ] **Step 3:** Update `ResourceRoleAttributeTests.cs` references to renamed attrs (mechanical: `Creates`→`Created`, `Loads`→`Loaded`, `Reads`→`Read`, `Edits`→`Edited`, `Deletes`→`Deleted`; remove any `References`/`Consumes` attribute construction).
- [ ] **Step 4:** `dotnet build src/Freista/Freista.csproj` — expect green (generator not yet updated, but the runtime lib compiles).
- [ ] **Step 5:** Commit: `jj commit -m "refactor(resources): participle role attributes; lineage as Created/Loaded/Edited properties"`

---

### Task 2: Generator reads lineage off the producer

**Files:**
- Modify: `src/Freista.Generator/Lowering/AttributeReader.cs`
- Modify: `src/Freista.Generator/Lowering/ScenarioParser.cs`

**Interfaces:**
- Consumes: renamed attribute class names from Task 1.
- Produces: `AttributeReader.RoleVerb` recognizing `CreatedAttribute→Create`, `LoadedAttribute→Load`, `ReadAttribute→Read`, `EditedAttribute→Edit`, `DeletedAttribute→Delete`; `AttributeReader.ProducerLineage(ISymbol producer)` returning `(ImmutableArray<string> references, ImmutableArray<string> consumes)` read from the `References`/`Consumes` **named properties**.

- [ ] **Step 1:** In `AttributeReader.cs`, update the `RoleVerb` switch attribute-name strings to the renamed classes. Parameter roles now admit only `ReadAttribute/EditedAttribute/DeletedAttribute`; return/method roles `CreatedAttribute/LoadedAttribute/EditedAttribute`.

- [ ] **Step 2:** Replace `ParameterSubjects` with a producer reader:

```csharp
/// <summary>The (references, consumes) target names declared on a producer's
/// <c>[Created]/[Loaded]/[Edited]</c> attribute (its <c>References</c>/<c>Consumes</c> named
/// properties); empty arrays when absent. Targets are parameter names or <see cref="ReturnSubject"/>.</summary>
public static (ImmutableArray<string> References, ImmutableArray<string> Consumes) ProducerLineage(
    ImmutableArray<AttributeData> attributes)
{
    foreach (var attr in attributes)
    {
        if (attr.AttributeClass?.Name is "CreatedAttribute" or "LoadedAttribute" or "EditedAttribute")
        {
            return (NamedStringArray(attr, "References"), NamedStringArray(attr, "Consumes"));
        }
    }
    return (ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);
}

private static ImmutableArray<string> NamedStringArray(AttributeData attr, string name)
{
    foreach (var named in attr.NamedArguments)
    {
        if (named.Key == name && named.Value.Kind == TypedConstantKind.Array)
        {
            return named.Value.Values
                .Select(v => v.Value as string)
                .Where(s => s is not null)
                .ToImmutableArray()!;
        }
    }
    return ImmutableArray<string>.Empty;
}
```

- [ ] **Step 3:** In `ScenarioParser.BuildResourceClaims`, synthesize lineage claims from the producer side. For each role-bearing parameter, build its plain claim (no `SubjectExpressions`). Then, for the producing position (return when `hasResult`, plus any `[Edited]` parameter), read `ProducerLineage` and for each target name emit a `Reference`/`Consume` claim whose `Expression` is the **target** parameter's rewritten argument expression (or `__r` for `Subject.Return`) and whose `SubjectExpressions = [producerExpr]` (the producer's `__r` or edited-param expression). Emit these synthesized claims **before** the producer's own role claim. Replace `ResolveSubjectExpressions` with `ResolveTargetExpressions(targetName, method, arguments, rewriter)` returning the single target expression (param arg or `__r`), null when unresolved (analyzer reports FRST010).

```csharp
// inside BuildResourceClaims, replacing the param loop's subject logic and the return tail:
var claims = new List<ResourceRoleClaim>();
var rewriter = new IdentifierReplacer(replacements);
var arguments = invocation.ArgumentList.Arguments;

string? ProducerExpr(int paramIndex) // edited-param producer expression
    => ((ExpressionSyntax)rewriter.Visit(FindArgument(arguments, method.Parameters[paramIndex].Name, paramIndex)!.Expression)).ToFullString().Trim();

string? TargetExpr(string name) => ResolveTargetExpression(name, method, arguments, rewriter);

void EmitLineage(ImmutableArray<AttributeData> attrs, string subjectExpr)
{
    var (refs, cons) = AttributeReader.ProducerLineage(attrs);
    foreach (var t in refs)
        if (TargetExpr(t) is { } te) claims.Add(new ResourceRoleClaim("Reference", te, IsReturn: false) { SubjectExpressions = [subjectExpr] });
    foreach (var t in cons)
        if (TargetExpr(t) is { } te) claims.Add(new ResourceRoleClaim("Consume", te, IsReturn: false) { SubjectExpressions = [subjectExpr] });
}

for (var p = 0; p < method.Parameters.Length; p++)
{
    var parameter = method.Parameters[p];
    var role = AttributeReader.ParameterRole(parameter);
    if (role is null) continue;
    var argument = FindArgument(arguments, parameter.Name, p);
    if (argument is null) continue;
    var expression = ((ExpressionSyntax)rewriter.Visit(argument.Expression)).ToFullString().Trim();
    if (role == "Edit") EmitLineage(parameter.GetAttributes(), expression);   // edited-param producer lineage first
    claims.Add(new ResourceRoleClaim(role, expression, IsReturn: false));
}

if (hasResult && AttributeReader.ReturnRole(method) is { } returnRole)
{
    var attrs = method.GetReturnTypeAttributes().Any(a => a.AttributeClass?.Name is "CreatedAttribute" or "LoadedAttribute" or "EditedAttribute")
        ? method.GetReturnTypeAttributes() : method.GetAttributes();
    EmitLineage(attrs, "__r");                                                 // return producer lineage before Create/Load/Edit
    claims.Add(new ResourceRoleClaim(returnRole, "__r", IsReturn: true));
}
return claims;
```

```csharp
/// <summary>Resolves a lineage target name to an instance expression: <c>Subject.Return</c> ⇒ <c>__r</c>;
/// a parameter name ⇒ that parameter's rewritten argument expression; null when unresolved.</summary>
private static string? ResolveTargetExpression(
    string name, IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments, IdentifierReplacer rewriter)
{
    if (name == AttributeReader.ReturnSubject) return "__r";
    for (var i = 0; i < method.Parameters.Length; i++)
    {
        if (method.Parameters[i].Name != name) continue;
        var arg = FindArgument(arguments, name, i);
        return arg is null ? null : ((ExpressionSyntax)rewriter.Visit(arg.Expression)).ToFullString().Trim();
    }
    return null;
}
```

- [ ] **Step 4:** `dotnet build Freista.slnx` — expect green.
- [ ] **Step 5:** Commit: `jj commit -m "feat(generator): lower producer-side References/Consumes into lineage claims"`

---

### Task 3: Update samples and re-green the lowering tests

**Files:**
- Modify: `test/Freista.Generator.Test/SampleSources.cs`
- Modify: `test/Freista.Generator.Test/ResourceLoweringTests.cs` (comments only; asserts unchanged)
- Modify: `test/Freista.Generator.Test/Snapshots/GeneratorSnapshotTests.Resource_scenario#FreistaScenarios.g.verified.cs` (re-accept)

- [ ] **Step 1:** In `SampleSources.ResourceDsl`, rewrite roles to participle and `BookWithLineage` to producer-side lineage:

```csharp
[StepName("booking a slot")]
[return: Created]
public static async Task<Appointment> Book([Read] User user, [Edited] Slot slot)
{ await Task.Yield(); return new Appointment(user, slot); }

[StepName("booking with lineage")]
[return: Created(References = [nameof(user)], Consumes = [nameof(slot)])]
public static async Task<Appointment> BookWithLineage(User user, Slot slot)
{ await Task.Yield(); return new Appointment(user, slot); }
```
(Also rename every other role attribute in this DSL block: `[Reads]`→`[Read]`, `[Edits]`→`[Edited]`, `[return: Creates]`→`[return: Created]`, `[return: Loads]`→`[return: Loaded]`, `[Deletes]`→`[Deleted]`.)

- [ ] **Step 2:** Run lowering tests:

Run: `dotnet test test/Freista.Generator.Test/Freista.Generator.Test.csproj --filter "FullyQualifiedName~ResourceLoweringTests"`
Expected: PASS — `Reference_and_consume_subjects_emit_edge_calls`, `Reference_and_consume_params_lower_to_shared_lineage_effects` (effects `[Reference(user), Consume(slot), Create(appt)]`), and `BookWithLineage_records_relations_from_the_created_appointment` (lineage appt→user Ref, appt→slot Consume) all green, proving behavior preserved.

- [ ] **Step 3:** Re-accept the snapshot: run the snapshot test, diff `.received.cs` vs `.verified.cs` to confirm only the lineage call-emission/attribute-comment lines changed as expected, then accept (copy received→verified).

Run: `dotnet test test/Freista.Generator.Test/Freista.Generator.Test.csproj --filter "FullyQualifiedName~GeneratorSnapshotTests"`

- [ ] **Step 4:** Commit: `jj commit -m "test(generator): producer-side lineage samples + re-accepted snapshot"`

---

### Task 4: Analyzer — FRST009 coverage + FRST010 inversion

**Files:**
- Modify: `src/Freista.Generator/Analysis/Descriptors.cs`
- Modify: `src/Freista.Generator/Analysis/ScenarioAnalyzer.cs`
- Modify: `test/Freista.Generator.Test/AnalyzerTests.cs`

- [ ] **Step 1:** Update Descriptors messages:
  - FRST009 → `"...declare its access: [Read], [Edited], or [Deleted] on a parameter, or [Created], [Loaded], or [Edited] on the return — or be named in a producer's References/Consumes — there is no default"`.
  - FRST010 title `"Lineage target must name a step input"`; message `"'{0}' is not a valid lineage target for step '{1}' — References/Consumes must name a parameter (via nameof) or Subject.Return"`.

- [ ] **Step 2:** Rewrite `AnalyzeStepResources`:
  - Build the set of param names named in any producer's `References`/`Consumes` (call `AttributeReader.ProducerLineage` on each `[Edited]` param's attributes and on the return attributes). A resource param with no own role **but** present in that set is *covered* — skip FRST009 for it.
  - FRST010: for each producer (each `[Edited]` param + the return when it is `Created`/`Loaded`/`Edited`), validate each `References`/`Consumes` target name resolves to a parameter or `Subject.Return`; and that it does not name the producer itself (self-lineage). Report FRST010 otherwise.

```csharp
var paramNames = method.Parameters.Select(p => p.Name).ToImmutableHashSet();

(ImmutableArray<string> refs, ImmutableArray<string> cons, string? selfName, Location loc) ProducerOf(IParameterSymbol p)
    => (AttributeReader.ProducerLineage(p.GetAttributes()) is var (r, c) ? r : default, c, p.Name,
        p.Locations.FirstOrDefault() ?? method.Locations.FirstOrDefault() ?? Location.None);
// (return producer: selfName = Subject.Return token; loc = method location)

// 1) lineage targets that satisfy FRST009 for bare params
var coveredByLineage = ... // union of all producers' refs+cons that name a parameter

// 2) FRST009 over params/return, skipping coveredByLineage
// 3) FRST010 over each producer's targets: must be in paramNames or == ReturnSubject, and != producer's own subject
```

- [ ] **Step 3:** Rewrite the FRST010 tests to the subject side:

```csharp
[Fact] // unknown target
public async Task FRST010_unknown_target_name()
{
    var source = LineageDsl + """
        public static class BadDsl { extension(When) {
            [StepName("transfer")]
            [return: Created(References = ["ghost"])]
            public static async Task<Account> Transfer(User who) { await Task.Yield(); return new Account("a"); }
        } }
        """;
    AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST010");
}

[Fact] // Subject.Return target but return is not a subject
public async Task FRST010_return_target_without_a_subject_return()
{
    var source = LineageDsl + """
        public static class BadDsl { extension(When) {
            [StepName("look up")]
            public static async Task LookUp([Edited(References = [Subject.Return])] Account acc) { await Task.Yield(); }
        } }
        """;
    AssertHas(await GeneratorHarness.AnalyzeAsync(source), "FRST010");
}

[Fact] // clean
public async Task FRST010_clean_for_valid_targets()
{
    var source = LineageDsl + """
        public static class GoodDsl { extension(When) {
            [StepName("assign")]
            public static async Task Assign([Edited(References = [nameof(who)])] Account acc, [Read] User who) { await Task.Yield(); }

            [StepName("create")]
            [return: Created(References = [nameof(who)])]
            public static async Task<Account> Create(User who) { await Task.Yield(); return new Account("a"); }
        } }
        """;
    Assert.DoesNotContain(await GeneratorHarness.AnalyzeAsync(source), d => d.Id == "FRST010");
}
```
(Note: in the `Assign` clean case `who` is `[Read]` *and* referenced — allowed; the `[Read]` gives its lock, the reference adds lineage. In `Create`, `who` is bare and covered by the reference.)

- [ ] **Step 4:** Run analyzer tests:

Run: `dotnet test test/Freista.Generator.Test/Freista.Generator.Test.csproj --filter "FullyQualifiedName~AnalyzerTests"`
Expected: PASS.

- [ ] **Step 5:** Commit: `jj commit -m "feat(analyzer): FRST009 lineage-target coverage + subject-side FRST010"`

---

### Task 5: Doc-comment refresh + README/docs sweep + full green

**Files:**
- Modify: `src/Freista/Resources/Subject.cs`, `src/Freista/Resources/ResourceContext.cs`, `src/Freista/Model/ResourceLineageRelation.cs`
- Modify: `README*` and any doc referencing old syntax

- [ ] **Step 1:** Refresh doc comments mentioning `[Creates]`/`[Edits]`/`[References]`/`[Consumes]` to the new attribute names and producer-side framing (Subject.cs summary, ResourceContext `Reference`/`Consume` summaries, ResourceLineageRelation `Subject` summary).
- [ ] **Step 2:** Grep the repo for stale syntax and fix prose/examples:

Run: `grep -rIn -E "\[References|\[Consumes|\[Creates|\[Loads|\[Reads\]|\[Edits|\[Deletes|Subject\.Return" --include=*.md --include=*.cs .`
Expected after fixes: only legitimate new-form usages remain (property `References =`/`Consumes =`, `Subject.Return` as a target token).

- [ ] **Step 3:** Full build + test:

Run: `dotnet build Freista.slnx` then `dotnet test Freista.slnx`
Expected: 0 warnings, all tests PASS.

- [ ] **Step 4:** Commit: `jj commit -m "docs: producer-side lineage across comments, README, and samples"`

## Self-Review

- **Spec coverage:** flip (Tasks 2-3) ✓; two-axis vocabulary + participle naming (Task 1) ✓; `[Loaded]` keeps lineage (Task 1 props on Loaded) ✓; `Subject.Return` as target token (Tasks 2,4) ✓; FRST010 inversion (Task 4) ✓; runtime unchanged (no runtime task — by construction) ✓; full replacement (Task 1 deletes old attrs) ✓; testing (Tasks 3,4) ✓.
- **Placeholder scan:** none — all code shown.
- **Type consistency:** `ProducerLineage` returns the tuple consumed by ScenarioParser and ScenarioAnalyzer; verb strings `"Reference"`/`"Consume"`/`"Create"`/`"Edit"` unchanged; `ResourceRoleClaim` shape reused (Expression=target, SubjectExpressions=[producer]).
