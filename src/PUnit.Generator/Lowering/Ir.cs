using System.Collections.Generic;

namespace PUnit.Generator.Lowering;

/// <summary>The original-source span of a step's DSL call, for span-form #line emission. 0-based
/// (Roslyn LinePosition); the emitter converts to the directive's 1-based form.</summary>
internal readonly record struct SourceSpan(
    string File, int StartLine, int StartChar, int EndLine, int EndChar);

/// <summary>
/// A single resource role lowered from a <c>[Creates]/[Loads]/[Reads]/[Edits]/[Deletes]</c> attribute
/// on a step's parameter or return value. <see cref="Verb"/> is the runtime <c>ResourceContext</c>
/// method name (Read/Load/Create/Edit/Delete); <see cref="Expression"/> is the rewritten argument
/// expression (in terms of <c>__inputs</c>) for a parameter role, or <c>__r</c> for a return role.
/// </summary>
internal readonly record struct ResourceRoleClaim(string Verb, string Expression, bool IsReturn);

/// <summary>A lowered scenario ready for emission.</summary>
internal sealed record ParsedScenario
{
    public string MethodFullName { get; init; } = "";
    public string SafeName { get; init; } = "";        // identifier-safe form for generated members
    public string ScenarioId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? ClassDisplayName { get; init; }
    public string? SourceFile { get; init; }
    public int SourceLine { get; init; }
    public int TimeoutMs { get; init; }
    public IReadOnlyList<string> Usings { get; init; } = [];
    public IReadOnlyList<ParsedStep> Steps { get; init; } = [];
}

/// <summary>One lowered step (graph node).</summary>
internal sealed record ParsedStep
{
    public int Index { get; init; }
    public string StepId { get; init; } = "";
    public string Phase { get; init; } = "";           // Given/When/Then
    public string OperationName { get; init; } = "";
    public string? SourceFile { get; init; }
    public int SourceLine { get; init; }

    /// <summary>Original span of the DSL invocation, for column-accurate #line mapping; null when
    /// the input was parsed without a path (e.g. pathless snapshot harness) ⇒ no directive.</summary>
    public SourceSpan? CallSpan { get; init; }

    public int TimeoutMs { get; init; }
    public IReadOnlyList<int> DependsOn { get; init; } = [];
    public string? GroupId { get; init; }

    /// <summary>True when the DSL method returns a value (Task&lt;T&gt;/ValueTask&lt;T&gt;).</summary>
    public bool HasResult { get; init; }

    /// <summary>Fully-qualified output type (the T), or "object" when there is no result.</summary>
    public string ResultTypeFqn { get; init; } = "object";

    /// <summary>The rewritten DSL invocation, e.g. <c>Given.PatientExists("Jane")</c>.</summary>
    public string InvokeCallText { get; init; } = "";

    /// <summary>Display name with constant placeholders already substituted.</summary>
    public string DisplayNameTemplate { get; init; } = "";

    /// <summary>
    /// When non-null, an interpolated-string expression (in terms of <c>__inputs</c>) for the
    /// runtime display-name formatter; null when the display name is fully constant.
    /// </summary>
    public string? FormatExpression { get; init; }

    /// <summary>
    /// Resource roles lowered from the step's role attributes, in declaration order (parameter roles
    /// first, then a return role). Empty when the step declares no roles ⇒ the emitter inserts nothing.
    /// </summary>
    public IReadOnlyList<ResourceRoleClaim> ResourceClaims { get; init; } = [];
}
