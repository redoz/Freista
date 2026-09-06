using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Raun.Generator.Lowering;

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
            // LINQ unrolling substitutes the loop variable with a literal, producing detached nodes the
            // model cannot evaluate. Fold those syntactically when every part is a literal (a plain
            // literal, or an interpolated string whose holes are literals) — `$"user-{1}"` is
            // "user-1" — so each unrolled step lists under its real name at discovery time instead of
            // three identical "{name}" entries. Anything else stays runtime-formatted.
            var inModel = argExpr is not null && argExpr.SyntaxTree == model.SyntaxTree;
            var constValue = inModel ? model.GetConstantValue(argExpr!) : default;
            string? folded = null;
            var foldedOk = argExpr is not null && !inModel && TryFoldDetached(argExpr, out folded);

            if (argExpr is not null && (constValue.HasValue || foldedOk))
            {
                var text = constValue.HasValue ? constValue.Value?.ToString() ?? "" : folded!;
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

    private static ExpressionSyntax? ArgumentForParameter(
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

    /// <summary>Folds a detached expression made only of literals: a literal itself, a parenthesized
    /// one, or an interpolated string whose every hole is such an expression (no alignment or format
    /// clause). False for anything that needs evaluation.</summary>
    private static bool TryFoldDetached(ExpressionSyntax expression, out string? text)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax literal when literal.Token.Value is not null:
                text = literal.Token.Value is string s ? s : System.Convert.ToString(literal.Token.Value, System.Globalization.CultureInfo.InvariantCulture);
                return text is not null;

            case ParenthesizedExpressionSyntax parenthesized:
                return TryFoldDetached(parenthesized.Expression, out text);

            case InterpolatedStringExpressionSyntax interpolated:
                var builder = new StringBuilder();
                foreach (var content in interpolated.Contents)
                {
                    switch (content)
                    {
                        case InterpolatedStringTextSyntax part:
                            builder.Append(part.TextToken.ValueText);
                            break;
                        case InterpolationSyntax { AlignmentClause: null, FormatClause: null } hole
                            when TryFoldDetached(hole.Expression, out var holeText):
                            builder.Append(holeText);
                            break;
                        default:
                            text = null;
                            return false;
                    }
                }

                text = builder.ToString();
                return true;

            default:
                text = null;
                return false;
        }
    }

    private static string EscapeForInterpolation(string text)
        => text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("{", "{{").Replace("}", "}}");
}
