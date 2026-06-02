using System.Collections.Generic;

namespace PUnit.Generator.Lowering;

/// <summary>A lowered scenario ready for emission.</summary>
internal sealed class ParsedScenario
{
    public string MethodFullName { get; set; } = "";
    public string SafeName { get; set; } = "";        // identifier-safe form for generated members
    public string ScenarioId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? SourceFile { get; set; }
    public int SourceLine { get; set; }
    public int TimeoutMs { get; set; }
    public List<string> Usings { get; } = new();
    public List<ParsedStep> Steps { get; } = new();
}

/// <summary>One lowered step (graph node).</summary>
internal sealed class ParsedStep
{
    public int Index { get; set; }
    public string StepId { get; set; } = "";
    public string Phase { get; set; } = "";           // Given/When/Then
    public string OperationName { get; set; } = "";
    public string? SourceFile { get; set; }
    public int SourceLine { get; set; }
    public int TimeoutMs { get; set; }
    public List<int> DependsOn { get; } = new();
    public string? GroupId { get; set; }

    /// <summary>True when the DSL method returns a value (Task&lt;T&gt;/ValueTask&lt;T&gt;).</summary>
    public bool HasResult { get; set; }

    /// <summary>Fully-qualified output type (the T), or "object" when there is no result.</summary>
    public string ResultTypeFqn { get; set; } = "object";

    /// <summary>The rewritten DSL invocation, e.g. <c>Given.PatientExists("Jane")</c>.</summary>
    public string InvokeCallText { get; set; } = "";

    /// <summary>Display name with constant placeholders already substituted.</summary>
    public string DisplayNameTemplate { get; set; } = "";

    /// <summary>
    /// When non-null, an interpolated-string expression (in terms of <c>__inputs</c>) for the
    /// runtime display-name formatter; null when the display name is fully constant.
    /// </summary>
    public string? FormatExpression { get; set; }
}
