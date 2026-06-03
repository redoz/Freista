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

    static int TimeoutMs(AttributeData? attr, string namedArgument)
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
