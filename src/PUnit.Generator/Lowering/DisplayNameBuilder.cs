using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PUnit.Generator.Lowering;

/// <summary>A lowered display name: the constant template plus an optional interpolated format
/// expression (in terms of <c>__inputs</c>) for runtime placeholders; null when fully constant.</summary>
internal readonly record struct LoweredDisplayName(string Template, string? FormatExpression);

/// <summary>
/// Builds a step's display name from its <c>[StepName]</c> template: constant placeholders are
/// folded into a literal, and runtime ones become an interpolated <c>$"..."</c> expression (in
/// terms of <c>__inputs</c>). The format expression is null when the name is fully constant.
/// </summary>
internal static class DisplayNameBuilder
{
    public static LoweredDisplayName Build(
        SemanticModel model,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> args,
        Dictionary<string, string> replacements)
    {
        var template = AttributeReader.StepTemplate(method) ?? method.Name;
        var tokens = TemplateTokenizer.Tokenize(template);
        var rewriter = new IdentifierReplacer(replacements);

        var constant = new StringBuilder();
        var interpolation = new StringBuilder("$\"");
        var anyRuntime = false;

        foreach (var token in tokens)
        {
            if (!token.IsPlaceholder)
            {
                constant.Append(token.Text);
                interpolation.Append(EscapeForInterpolation(token.Text));
                continue;
            }

            var argExpr = ArgumentForParameter(method, args, token.Text);
            // LINQ unrolling substitutes the loop variable, producing detached nodes the model
            // can't evaluate; treat those as runtime-formatted.
            var inModel = argExpr is not null && argExpr.SyntaxTree == model.SyntaxTree;
            var constValue = inModel ? model.GetConstantValue(argExpr!) : default;

            if (argExpr is not null && constValue.HasValue)
            {
                var text = constValue.Value?.ToString() ?? "";
                constant.Append(text);
                interpolation.Append(EscapeForInterpolation(text));
            }
            else if (argExpr is not null)
            {
                anyRuntime = true;
                constant.Append('{').Append(token.Text).Append('}');
                var rewritten = ((ExpressionSyntax)rewriter.Visit(argExpr!)).ToFullString().Trim();
                // Parenthesize so a ':' inside the expression (e.g. global::) isn't read as a
                // format separator, and to be safe against '?' / nested interpolation.
                interpolation.Append("{(").Append(rewritten).Append(")}");
            }
            else
            {
                constant.Append('{').Append(token.Text).Append('}');
                interpolation.Append("{{").Append(token.Text).Append("}}");
            }
        }

        interpolation.Append('"');
        return new LoweredDisplayName(constant.ToString(), anyRuntime ? interpolation.ToString() : null);
    }

    static ExpressionSyntax? ArgumentForParameter(
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> args,
        string parameterName)
    {
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (method.Parameters[i].Name == parameterName && i < args.Count)
            {
                return args[i].Expression;
            }
        }

        return null;
    }

    static string EscapeForInterpolation(string text)
        => text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("{", "{{").Replace("}", "}}");
}
