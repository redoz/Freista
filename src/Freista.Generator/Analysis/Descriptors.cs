using Microsoft.CodeAnalysis;

namespace Freista.Generator.Analysis;

/// <summary>Diagnostics for the supported scenario subset (see the design's "Analyzer Rules").</summary>
internal static class Descriptors
{
    private const string Category = "Freista.Usage";

    public static readonly DiagnosticDescriptor UnhandledException = new(
        "FRST000",
        "Unhandled exception in Freista generator",
        "Freista failed to process a scenario: {0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MustBeAsyncTask = new(
        "FRST001",
        "Scenario method must be async Task or async ValueTask",
        "Scenario method '{0}' must be declared 'async Task' or 'async ValueTask'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedStatement = new(
        "FRST002",
        "Unsupported scenario statement",
        "Scenario statements must be an awaited phase-marker call (Given/When/Then, or any type implementing Freista.IPhase), an awaited tuple, or an awaited array of such calls",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedControlFlow = new(
        "FRST003",
        "Unsupported control flow in scenario",
        "Control flow is not supported in scenario bodies in this version",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotADslCall = new(
        "FRST004",
        "Scenario step must be a phase-marker call",
        "Scenario steps must call a static extension member on a phase marker (Given/When/Then, or any type implementing Freista.IPhase)",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidReturnType = new(
        "FRST005",
        "DSL method has an unsupported return type",
        "DSL method '{0}' must return Task, Task<T>, ValueTask, or ValueTask<T>",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidGroupElement = new(
        "FRST006",
        "Parallel group element must be a phase-marker call",
        "Every element of a tuple/array parallel group must be a phase-marker call (Given/When/Then, or any type implementing Freista.IPhase)",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidArgument = new(
        "FRST007",
        "Scenario step argument is not lowerable",
        "Argument '{0}' must be a prior step output, a scenario parameter, or a constant",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnboundPlaceholder = new(
        "FRST008",
        "Display-name placeholder does not bind to a parameter",
        "Display-name placeholder '{0}' does not match any parameter of '{1}'",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingResourceRole = new(
        "FRST009",
        "Resource access must be declared",
        "Resource-typed {0} '{1}' must declare its access: [Reads], [Edits], or [Deletes] on a parameter, or [Creates], [Loads], or [Edits] on the return — there is no default",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidLineageSubject = new(
        "FRST010",
        "Lineage subject must name a step subject",
        "'{0}' is not a valid lineage subject for step '{1}' — Subject must name an [Edits] parameter or the [Creates]/[Edits] return (use Subject.Return)",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
