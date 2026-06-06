using Microsoft.CodeAnalysis;

namespace PUnit.Generator.Analysis;

/// <summary>Diagnostics for the supported scenario subset (see the design's "Analyzer Rules").</summary>
internal static class Descriptors
{
    private const string Category = "PUnit.Usage";

    public static readonly DiagnosticDescriptor UnhandledException = new(
        "PUNIT000",
        "Unhandled exception in PUnit generator",
        "PUnit failed to process a scenario: {0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MustBeAsyncTask = new(
        "PUNIT001",
        "Scenario method must be async Task or async ValueTask",
        "Scenario method '{0}' must be declared 'async Task' or 'async ValueTask'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedStatement = new(
        "PUNIT002",
        "Unsupported scenario statement",
        "Scenario statements must be an awaited phase-marker call (Given/When/Then, or any type implementing PUnit.IPhase), an awaited tuple, or an awaited array of such calls",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedControlFlow = new(
        "PUNIT003",
        "Unsupported control flow in scenario",
        "Control flow is not supported in scenario bodies in this version",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotADslCall = new(
        "PUNIT004",
        "Scenario step must be a phase-marker call",
        "Scenario steps must call a static extension member on a phase marker (Given/When/Then, or any type implementing PUnit.IPhase)",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidReturnType = new(
        "PUNIT005",
        "DSL method has an unsupported return type",
        "DSL method '{0}' must return Task, Task<T>, ValueTask, or ValueTask<T>",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidGroupElement = new(
        "PUNIT006",
        "Parallel group element must be a phase-marker call",
        "Every element of a tuple/array parallel group must be a phase-marker call (Given/When/Then, or any type implementing PUnit.IPhase)",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidArgument = new(
        "PUNIT007",
        "Scenario step argument is not lowerable",
        "Argument '{0}' must be a prior step output, a scenario parameter, or a constant",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnboundPlaceholder = new(
        "PUNIT008",
        "Display-name placeholder does not bind to a parameter",
        "Display-name placeholder '{0}' does not match any parameter of '{1}'",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingResourceRole = new(
        "PUNIT009",
        "Resource access must be declared",
        "Resource-typed {0} '{1}' must declare its access: [Reads], [Edits], or [Deletes] on a parameter, or [Creates], [Loads], or [Edits] on the return — there is no default",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
