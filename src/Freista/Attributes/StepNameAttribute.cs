namespace Freista;

/// <summary>
/// Sets the reported display name for a Given/When/Then DSL operation. The template may contain
/// <c>{parameterName}</c> placeholders that bind to the operation's parameters; the generator
/// substitutes compile-time constants and emits a runtime formatter for the rest.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StepNameAttribute : Attribute
{
    public StepNameAttribute(string template) => Template = template;

    /// <summary>Display-name template, e.g. <c>"patient {name} exists"</c>.</summary>
    public string Template { get; }

    /// <summary>Per-operation timeout in milliseconds. Zero means inherit the scenario timeout.</summary>
    public int TimeoutMs { get; init; }
}
