using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PUnit.Generator.Lowering;

internal enum BindingKind { Single, Tuple }

/// <summary>
/// The variables a scenario statement binds: a single local (<c>var x = ...</c>) or a tuple
/// deconstruction (<c>var (a, b) = ...</c> / <c>(var a, var b) = ...</c>).
/// </summary>
internal sealed record Binding(BindingKind Kind, IReadOnlyList<string> Names)
{
    public static Binding Single(string name) => new(BindingKind.Single, [name]);

    /// <summary>The binding for the left-hand side of an awaited assignment, or null if unsupported.</summary>
    public static Binding? FromAssignment(ExpressionSyntax left)
    {
        // var (a, b) = ...
        if (left is DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax paren })
        {
            var names = new List<string>();
            foreach (var d in paren.Variables)
            {
                if (d is SingleVariableDesignationSyntax single)
                {
                    names.Add(single.Identifier.Text);
                }
                else
                {
                    return null;
                }
            }

            return new Binding(BindingKind.Tuple, names);
        }

        // (var a, var b) = ...
        if (left is TupleExpressionSyntax tuple)
        {
            var names = new List<string>();
            foreach (var arg in tuple.Arguments)
            {
                if (arg.Expression is DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax single })
                {
                    names.Add(single.Identifier.Text);
                }
                else
                {
                    return null;
                }
            }

            return new Binding(BindingKind.Tuple, names);
        }

        return null;
    }
}
