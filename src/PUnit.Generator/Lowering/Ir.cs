using System.Collections.Generic;

namespace PUnit.Generator.Lowering;

/// <summary>A lowered scenario ready for emission.</summary>
internal sealed record ParsedScenario
{
    public string MethodFullName { get; init; } = "";
    public string SafeName { get; init; } = "";        // identifier-safe form for generated members
    public string ScenarioId { get; init; } = "";
    public string DisplayName { get; init; } = "";
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
}
