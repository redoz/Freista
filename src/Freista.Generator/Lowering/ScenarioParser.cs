using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Freista.Generator.Lowering;

/// <summary>
/// Lowers a <c>[Scenario]</c> method body into a <see cref="ParsedScenario"/>: it walks the
/// statements in source order, recognizes awaited Given/When/Then calls (singly, in tuples, in
/// arrays, or via a constant LINQ <c>.ToArray()</c>), and records each step's dataflow + source-order
/// dependencies, output binding, display name, and rewritten invocation.
/// Returns <c>null</c> when the body falls outside the supported subset (the analyzer reports why).
/// </summary>
internal sealed class ScenarioParser
{
    private readonly SemanticModel _model;
    private readonly IMethodSymbol _method;
    private readonly MethodDeclarationSyntax _syntax;

    // Variable name -> the step(s) that produced it.
    private readonly Dictionary<string, VarSource> _vars = [];

    // Namespaces that contain the invoked DSL extension members; imported into the generated file
    // so `Given.PatientExists(...)` resolves there too.
    private readonly HashSet<string> _dslNamespaces = [];

    // Indices introduced by the previous top-level statement (source-order barrier / join target).
    private List<int> _prevFrontier = [];

    private int _nextIndex;
    private string _scenarioId = "";

    private readonly List<ParsedStep> _steps = [];

    private ScenarioParser(SemanticModel model, IMethodSymbol method, MethodDeclarationSyntax syntax)
    {
        _model = model;
        _method = method;
        _syntax = syntax;
    }

    private readonly record struct VarSource(bool IsArray, int Index, int[] Indices, string ElementType)
    {
        public static VarSource Scalar(int index) => new(false, index, [], "");
        public static VarSource Array(int[] indices, string elementType) => new(true, -1, indices, elementType);
    }

    public static ParsedScenario? TryParse(SemanticModel model, IMethodSymbol method, MethodDeclarationSyntax syntax)
        => new ScenarioParser(model, method, syntax).Parse();

    private ParsedScenario? Parse()
    {
        if (_syntax.Body is null)
        {
            return null;
        }

        var methodFullName = _method.ContainingType.ToDisplayString(SymbolHelpers.NoGlobal) + "." + _method.Name;
        _scenarioId = GenStableId.ForScenario(methodFullName);

        foreach (var statement in _syntax.Body.Statements)
        {
            if (!ParseStatement(statement))
            {
                return null; // unsupported construct
            }
        }

        var usings = CollectUsings().ToList();
        foreach (var ns in _dslNamespaces)
        {
            usings.Add($"using {ns};");
        }

        return new ParsedScenario
        {
            MethodFullName = methodFullName,
            SafeName = SafeName(methodFullName),
            ScenarioId = _scenarioId,
            DisplayName = AttributeReader.ScenarioDisplayName(_method) ?? _method.Name,
            ClassDisplayName = AttributeReader.ClassDisplayName(_method.ContainingType),
            TimeoutMs = AttributeReader.ScenarioTimeout(_method),
            SourceFile = Location(_syntax.Identifier, out var line),
            SourceLine = line,
            Steps = [.. _steps],
            Usings = usings,
        };
    }

    private bool ParseStatement(StatementSyntax statement)
    {
        return statement switch
        {
            LocalDeclarationStatementSyntax local => ParseLocalDeclaration(local),
            ExpressionStatementSyntax expr => ParseExpressionStatement(expr),
            _ => false,
        };

    }

    private bool ParseLocalDeclaration(LocalDeclarationStatementSyntax local)
    {
        var variables = local.Declaration.Variables;
        if (variables.Count != 1 || variables[0].Initializer?.Value is not AwaitExpressionSyntax await)
        {
            return false;
        }

        return ParseAwaited(await.Expression, binding: Binding.Single(variables[0].Identifier.Text));
    }

    private bool ParseExpressionStatement(ExpressionStatementSyntax statement)
    {
        return statement.Expression switch
        {
            AwaitExpressionSyntax bareAwait => ParseAwaited(bareAwait.Expression, binding: null),
            AssignmentExpressionSyntax { Right: AwaitExpressionSyntax await } assignment => ParseAwaited(await.Expression, binding: Binding.FromAssignment(assignment.Left)),
            _ => false,
        };

    }

    private bool ParseAwaited(ExpressionSyntax awaited, Binding? binding)
    {
        switch (awaited)
        {
            case InvocationExpressionSyntax invocation when IsToArray(invocation):
                return ParseLinqArray(invocation, binding);
            case InvocationExpressionSyntax invocation:
                return ParseSingleCall(invocation, binding);
            case TupleExpressionSyntax tuple:
                return ParseTuple(tuple, binding);
            case ArrayCreationExpressionSyntax array:
                return ParseArray(array.Initializer, binding);
            case ImplicitArrayCreationExpressionSyntax array:
                return ParseArray(array.Initializer, binding);
            default:
                return false;
        }
    }

    private bool ParseSingleCall(InvocationExpressionSyntax invocation, Binding? binding)
    {
        if (binding is { Kind: BindingKind.Tuple })
        {
            return false;
        }

        var step = BuildStep(invocation, groupId: null, _prevFrontier);
        if (step is null)
        {
            return false;
        }

        if (binding is { Kind: BindingKind.Single })
        {
            _vars[binding.Names[0]] = VarSource.Scalar(step.Index);
        }

        _prevFrontier = [step.Index];
        return true;
    }

    private bool ParseTuple(TupleExpressionSyntax tuple, Binding? binding)
    {
        var groupId = "g" + _nextIndex;
        var frontier = new List<int>();
        var names = binding?.Names;

        for (var i = 0; i < tuple.Arguments.Count; i++)
        {
            if (tuple.Arguments[i].Expression is not InvocationExpressionSyntax invocation)
            {
                return false;
            }

            var step = BuildStep(invocation, groupId, _prevFrontier);
            if (step is null)
            {
                return false;
            }

            if (names is not null && i < names.Count)
            {
                _vars[names[i]] = VarSource.Scalar(step.Index);
            }

            frontier.Add(step.Index);
        }

        _prevFrontier = frontier;
        return true;
    }

    private bool ParseArray(InitializerExpressionSyntax? initializer, Binding? binding)
    {
        if (initializer is null || binding is not { Kind: BindingKind.Single })
        {
            return false;
        }

        var groupId = "g" + _nextIndex;
        var frontier = new List<int>();
        var elementType = "object";

        foreach (var element in initializer.Expressions)
        {
            if (element is not InvocationExpressionSyntax invocation)
            {
                return false;
            }

            var step = BuildStep(invocation, groupId, _prevFrontier);
            if (step is null)
            {
                return false;
            }

            elementType = step.ResultTypeFqn;
            frontier.Add(step.Index);
        }

        _vars[binding.Names[0]] = VarSource.Array(frontier.ToArray(), elementType);
        _prevFrontier = frontier;
        return true;
    }

    private bool ParseLinqArray(InvocationExpressionSyntax toArray, Binding? binding)
    {
        if (binding is not { Kind: BindingKind.Single })
        {
            return false;
        }

        // Shape: Enumerable.Range(start, count).Select(i => <DSL call using i>).ToArray()
        if (toArray.Expression is not MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax selectInv } toArrayMember
            || toArrayMember.Name.Identifier.Text != "ToArray")
        {
            return false;
        }

        if (selectInv.Expression is not MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax rangeInv } selectMember
            || selectMember.Name.Identifier.Text != "Select"
            || selectInv.ArgumentList.Arguments.Count != 1
            || selectInv.ArgumentList.Arguments[0].Expression is not SimpleLambdaExpressionSyntax lambda)
        {
            return false;
        }

        if (rangeInv.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Range" }
            || rangeInv.ArgumentList.Arguments.Count != 2)
        {
            return false;
        }

        var startConst = _model.GetConstantValue(rangeInv.ArgumentList.Arguments[0].Expression);
        var countConst = _model.GetConstantValue(rangeInv.ArgumentList.Arguments[1].Expression);
        if (!startConst.HasValue || !countConst.HasValue
            || startConst.Value is not int start || countConst.Value is not int count)
        {
            return false;
        }

        if (lambda.Body is not InvocationExpressionSyntax bodyCall)
        {
            return false;
        }

        var loopVar = lambda.Parameter.Identifier.Text;
        var groupId = "g" + _nextIndex;
        var frontier = new List<int>();
        var elementType = "object";

        for (var k = 0; k < count; k++)
        {
            var value = start + k;
            // Replace the loop variable with the constant value for this element.
            var substituted = (InvocationExpressionSyntax)new IdentifierReplacer(
                new Dictionary<string, string> { [loopVar] = value.ToString(CultureInfo.InvariantCulture) }).Visit(bodyCall);

            var step = BuildStep(substituted, groupId, _prevFrontier, semanticNode: bodyCall);
            if (step is null)
            {
                return false;
            }

            elementType = step.ResultTypeFqn;
            frontier.Add(step.Index);
        }

        _vars[binding.Names[0]] = VarSource.Array(frontier.ToArray(), elementType);
        _prevFrontier = frontier;
        return true;
    }

    private static bool IsToArray(InvocationExpressionSyntax invocation)
        => invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ToArray" };

    /// <summary>
    /// Builds a step from a DSL invocation. <paramref name="semanticNode"/> is the invocation to use
    /// for semantic lookups when the emitted call has been syntactically rewritten (LINQ unroll).
    /// </summary>
    private ParsedStep? BuildStep(
        InvocationExpressionSyntax invocation,
        string? groupId,
        List<int> sourceOrderDeps,
        InvocationExpressionSyntax? semanticNode = null)
    {
        var lookup = semanticNode ?? invocation;
        if (lookup.Expression is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        var phase = SymbolHelpers.PhaseOf(member.Expression, _model);
        if (phase is null)
        {
            return null;
        }

        if (_model.GetSymbolInfo(lookup).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        if (!SymbolHelpers.TryUnwrapReturn(method.ReturnType, out var resultType))
        {
            return null;
        }

        var dslNamespace = method.ContainingType?.ContainingNamespace;
        if (dslNamespace is { IsGlobalNamespace: false })
        {
            _dslNamespaces.Add(dslNamespace.ToDisplayString(SymbolHelpers.NoGlobal));
        }

        var index = _nextIndex++;
        var operation = member.Name.Identifier.Text;

        // Dataflow dependencies: variables referenced in the (original) arguments.
        var dataflow = CollectDataflowDeps(invocation);
        var deps = new SortedSet<int>(sourceOrderDeps);
        foreach (var d in dataflow)
        {
            deps.Add(d);
        }

        var replacements = BuildReplacements();
        var wantsCtx = SymbolHelpers.WantsContext(method, invocation.ArgumentList.Arguments.Count);
        var callText = BuildCallText(invocation, member, replacements, wantsCtx);
        var resourceClaims = BuildResourceClaims(invocation, method, resultType is not null, replacements);

        var (template, formatExpr) = DisplayNameBuilder.Build(_model, method, invocation.ArgumentList.Arguments, replacements);

        var step = new ParsedStep
        {
            Index = index,
            StepId = GenStableId.ForStep(_scenarioId, operation + ":" + index),
            Phase = phase,
            OperationName = operation,
            HasResult = resultType is not null,
            ResultTypeFqn = resultType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "object",
            InvokeCallText = callText,
            DisplayNameTemplate = template,
            FormatExpression = formatExpr,
            GroupId = groupId,
            TimeoutMs = AttributeReader.StepTimeout(method),
            SourceFile = Location(invocation, out var line),
            SourceLine = line,
            CallSpan = SpanOf(invocation),
            DependsOn = [.. deps],
            ResourceClaims = resourceClaims,
        };

        _steps.Add(step);
        return step;
    }

    private List<int> CollectDataflowDeps(InvocationExpressionSyntax invocation)
    {
        var deps = new List<int>();
        foreach (var identifier in invocation.ArgumentList.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (_vars.TryGetValue(identifier.Identifier.Text, out var source))
            {
                if (source.IsArray)
                {
                    deps.AddRange(source.Indices);
                }
                else
                {
                    deps.Add(source.Index);
                }
            }
        }

        return deps;
    }

    private Dictionary<string, string> BuildReplacements()
    {
        var map = new Dictionary<string, string>();
        foreach (var pair in _vars)
        {
            if (pair.Value.IsArray)
            {
                var elements = pair.Value.Indices.Select(i => $"__inputs.Get<{pair.Value.ElementType}>({i})");
                map[pair.Key] = $"new {pair.Value.ElementType}[] {{ {string.Join(", ", elements)} }}";
            }
            else
            {
                var producer = _steps.First(s => s.Index == pair.Value.Index);
                map[pair.Key] = $"__inputs.Get<{producer.ResultTypeFqn}>({pair.Value.Index})";
            }
        }

        return map;
    }

    private static string BuildCallText(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax member,
        Dictionary<string, string> replacements,
        bool appendCtx)
    {
        var receiver = member.Expression.ToString();
        var name = member.Name.Identifier.Text;
        var rewriter = new IdentifierReplacer(replacements);

        var args = invocation.ArgumentList.Arguments
            .Select(a => ((ArgumentSyntax)rewriter.Visit(a)).ToFullString().Trim())
            .ToList();

        if (appendCtx)
        {
            args.Add("__ctx");
        }

        return $"{receiver}.{name}({string.Join(", ", args)})";
    }

    /// <summary>
    /// Lowers the method's resource role attributes into <see cref="ResourceRoleClaim"/>s: one per
    /// role-bearing parameter (its rewritten argument expression), then one for a return role when the
    /// step yields a value (using <c>__r</c>). Argument expressions are rewritten with the SAME
    /// replacements as the call text, so step-output locals become <c>__inputs.Get&lt;…&gt;(i)</c>.
    /// Empty when the method declares no roles ⇒ the emitter inserts nothing.
    /// </summary>
    private static List<ResourceRoleClaim> BuildResourceClaims(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        bool hasResult,
        Dictionary<string, string> replacements)
    {
        var claims = new List<ResourceRoleClaim>();
        var rewriter = new IdentifierReplacer(replacements);
        var arguments = invocation.ArgumentList.Arguments;

        for (var p = 0; p < method.Parameters.Length; p++)
        {
            var parameter = method.Parameters[p];
            var role = AttributeReader.ParameterRole(parameter);
            if (role is null)
            {
                continue;
            }

            var argument = FindArgument(arguments, parameter.Name, p);
            if (argument is null)
            {
                // No supplied argument. The trailing-ScenarioContext case is correct (nothing to claim).
                // The omitted-optional case (a declared role on a defaulted param that the call left out)
                // is a deliberate, known gap: the role silently vanishes. Diagnosing it is intentionally
                // deferred to FRST009 (Task 9); a future reader should not treat this skip as a bug.
                continue;
            }

            var expression = ((ExpressionSyntax)rewriter.Visit(argument.Expression)).ToFullString().Trim();
            var subjectExpressions = role is "Reference" or "Consume"
                ? ResolveSubjectExpressions(parameter, method, arguments, rewriter)
                : [];
            claims.Add(new ResourceRoleClaim(role, expression, IsReturn: false) { SubjectExpressions = subjectExpressions });
        }

        if (hasResult && AttributeReader.ReturnRole(method) is { } returnRole)
        {
            claims.Add(new ResourceRoleClaim(returnRole, "__r", IsReturn: true));
        }

        return claims;
    }

    /// <summary>
    /// Maps a [References]/[Consumes] parameter's declared subject names to instance expressions:
    /// <c>Subject.Return</c> ⇒ <c>__r</c>; a parameter name ⇒ that parameter's rewritten argument
    /// expression. Unresolved names are skipped (the analyzer reports them as FRST010).
    /// </summary>
    private static List<string> ResolveSubjectExpressions(
        IParameterSymbol parameter,
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IdentifierReplacer rewriter)
    {
        var result = new List<string>();
        foreach (var subject in AttributeReader.ParameterSubjects(parameter))
        {
            if (subject == AttributeReader.ReturnSubject)
            {
                result.Add("__r");
                continue;
            }

            for (var i = 0; i < method.Parameters.Length; i++)
            {
                if (method.Parameters[i].Name != subject)
                {
                    continue;
                }

                var arg = FindArgument(arguments, subject, i);
                if (arg is not null)
                {
                    result.Add(((ExpressionSyntax)rewriter.Visit(arg.Expression)).ToFullString().Trim());
                }

                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Finds the argument bound to the parameter at <paramref name="position"/>: a named argument
    /// (<c>name: value</c>) matching <paramref name="parameterName"/> if present, else the positional
    /// argument at that index. Null when neither exists (e.g. an omitted optional parameter).
    /// </summary>
    private static ArgumentSyntax? FindArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        string parameterName,
        int position)
    {
        foreach (var argument in arguments)
        {
            if (argument.NameColon?.Name.Identifier.Text == parameterName)
            {
                return argument;
            }
        }

        return position < arguments.Count && arguments[position].NameColon is null
            ? arguments[position]
            : null;
    }

    // The emitter dedupes the merged using set, so no need to dedupe here.
    private IEnumerable<string> CollectUsings()
        => _syntax.SyntaxTree.GetCompilationUnitRoot().Usings.Select(u => u.ToString().Trim());

    private static string? Location(SyntaxNode node, out int line)
    {
        var span = node.GetLocation().GetLineSpan();
        line = span.StartLinePosition.Line + 1;
        return span.Path;
    }

    private static SourceSpan? SpanOf(SyntaxNode node)
    {
        var s = node.GetLocation().GetLineSpan();
        if (string.IsNullOrEmpty(s.Path))
        {
            return null;
        }

        return new SourceSpan(
            s.Path,
            s.StartLinePosition.Line, s.StartLinePosition.Character,
            s.EndLinePosition.Line, s.EndLinePosition.Character);
    }

    private static string? Location(SyntaxToken token, out int line)
    {
        var span = token.GetLocation().GetLineSpan();
        line = span.StartLinePosition.Line + 1;
        return span.Path;
    }

    private static string SafeName(string methodFullName)
    {
        var sb = new StringBuilder();
        foreach (var c in methodFullName)
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return sb.ToString();
    }
}
