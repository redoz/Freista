using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace PUnit.Generator.Lowering;

/// <summary>Reads PUnit's <c>[Scenario]</c> / <c>[StepName]</c> attribute data off method symbols.</summary>
internal static class AttributeReader
{
    public static string? ScenarioDisplayName(IMethodSymbol method)
    {
        var attr = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "ScenarioAttribute");
        if (attr is { ConstructorArguments.Length: > 0 } && attr.ConstructorArguments[0].Value is string name)
        {
            return name;
        }

        return null;
    }

    public static int ScenarioTimeout(IMethodSymbol method)
    {
        var attr = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "ScenarioAttribute");
        return TimeoutMs(attr, "Timeout"); // FactAttribute.Timeout (ms)
    }

    public static int StepTimeout(IMethodSymbol method)
        => TimeoutMs(
            method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "StepNameAttribute"),
            "TimeoutMs");

    public static string? StepTemplate(IMethodSymbol method)
    {
        var attr = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "StepNameAttribute");
        if (attr is { ConstructorArguments.Length: > 0 } && attr.ConstructorArguments[0].Value is string template)
        {
            return template;
        }

        return null;
    }

    public static string? ClassDisplayName(INamedTypeSymbol type)
    {
        var attr = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "DisplayNameAttribute");
        if (attr is { ConstructorArguments.Length: > 0 } && attr.ConstructorArguments[0].Value is string name)
        {
            return name;
        }

        return null;
    }

    /// <summary>
    /// The resource role declared on <paramref name="parameter"/> (<c>[Reads]/[Edits]/[Deletes]</c>),
    /// as the runtime <c>ResourceContext</c> verb name, or null when none.
    /// </summary>
    public static string? ParameterRole(IParameterSymbol parameter)
        => RoleVerb(parameter.GetAttributes(), parameterRoles: true);

    /// <summary>
    /// The resource role declared on <paramref name="method"/>'s return value
    /// (<c>[return: Creates]/[return: Loads]/[return: Edits]</c>), falling back to the method-level
    /// shorthand, as the runtime verb name, or null when none. Return roles only apply when the step
    /// actually yields a value (the caller checks that).
    /// </summary>
    public static string? ReturnRole(IMethodSymbol method)
        => RoleVerb(method.GetReturnTypeAttributes(), parameterRoles: false)
            ?? RoleVerb(method.GetAttributes(), parameterRoles: false);

    /// <summary>
    /// Maps the first recognized role attribute to its runtime verb name. Parameter roles admit
    /// <c>Reads/Edits/Deletes</c>; return/method roles admit <c>Creates/Loads/Edits</c>; <c>Edits</c>
    /// is valid in both positions.
    /// </summary>
    private static string? RoleVerb(ImmutableArray<AttributeData> attributes, bool parameterRoles)
    {
        foreach (var attr in attributes)
        {
            var verb = attr.AttributeClass?.Name switch
            {
                "ReadsAttribute" when parameterRoles => "Read",
                "ReferencesAttribute" when parameterRoles => "Reference",
                "ConsumesAttribute" when parameterRoles => "Consume",
                "DeletesAttribute" when parameterRoles => "Delete",
                "EditsAttribute" => "Edit",
                "CreatesAttribute" when !parameterRoles => "Create",
                "LoadsAttribute" when !parameterRoles => "Load",
                _ => null,
            };

            if (verb is not null)
            {
                return verb;
            }
        }

        return null;
    }

    private static int TimeoutMs(AttributeData? attr, string namedArgument)
    {
        if (attr is null)
        {
            return 0;
        }

        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == namedArgument && named.Value.Value is int ms)
            {
                return ms;
            }
        }

        return 0;
    }
}
