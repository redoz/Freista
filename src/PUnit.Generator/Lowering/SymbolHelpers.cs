using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PUnit.Generator.Lowering;

/// <summary>Shared symbol/syntax recognition for the supported scenario subset.</summary>
internal static class SymbolHelpers
{
    public static readonly SymbolDisplayFormat NoGlobal =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(
            SymbolDisplayGlobalNamespaceStyle.Omitted);

    public const string ScenarioContextFullName = "PUnit.ScenarioContext";

    /// <summary>Returns "Given"/"When"/"Then" if the receiver is one of the PUnit phase markers.</summary>
    public static string? PhaseOf(ExpressionSyntax receiver, SemanticModel model)
    {
        if (model.GetSymbolInfo(receiver).Symbol is not INamedTypeSymbol type)
        {
            return null;
        }

        if (type.ContainingNamespace?.ToDisplayString(NoGlobal) != "PUnit")
        {
            return null;
        }

        return type.Name is "Given" or "When" or "Then" ? type.Name : null;
    }

    /// <summary>True when the invocation is a phase-marker DSL call (e.g. <c>Given.PatientExists(...)</c>).</summary>
    public static bool IsDslCall(InvocationExpressionSyntax invocation, SemanticModel model, out string phase)
    {
        phase = "";
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return false;
        }

        var detected = PhaseOf(member.Expression, model);
        if (detected is null)
        {
            return false;
        }

        phase = detected;
        return true;
    }

    /// <summary>Unwraps Task/ValueTask return types; out result type is null when there is none.</summary>
    public static bool TryUnwrapReturn(ITypeSymbol returnType, out ITypeSymbol? resultType)
    {
        resultType = null;
        if (returnType is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.ContainingNamespace?.ToDisplayString(NoGlobal) != "System.Threading.Tasks")
        {
            return false;
        }

        if (named.Name is not ("Task" or "ValueTask"))
        {
            return false;
        }

        if (named.Arity == 1)
        {
            resultType = named.TypeArguments[0];
        }

        return true;
    }

    /// <summary>Whether the method has a trailing <c>ScenarioContext</c> parameter not supplied by source args.</summary>
    public static bool WantsContext(IMethodSymbol method, int suppliedArgCount)
    {
        if (method.Parameters.Length == 0)
        {
            return false;
        }

        var last = method.Parameters[method.Parameters.Length - 1];
        return last.Type.ToDisplayString(NoGlobal) == ScenarioContextFullName
            && suppliedArgCount == method.Parameters.Length - 1;
    }
}
