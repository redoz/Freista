namespace PUnit;

/// <summary>
/// Marks a static method as a PUnit scenario. The method body is source for the generator,
/// which lowers each Given/When/Then step into a graph node; xUnit never executes the body
/// directly. The optional display name overrides the method name in reporting.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ScenarioAttribute : Attribute
{
    public ScenarioAttribute(string? displayName = null) => DisplayName = displayName;

    /// <summary>Human-readable scenario name. Falls back to the method name when null.</summary>
    public string? DisplayName { get; }

    /// <summary>Scenario-wide timeout in milliseconds. Zero means no timeout.</summary>
    public int TimeoutMs { get; init; }
}
