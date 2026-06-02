using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PUnit.Generator.Internal;

namespace PUnit.Generator.Lowering;

/// <summary>
/// Lowers a <c>[Scenario]</c> method body into a <see cref="ParsedScenario"/>: it walks the
/// statements in source order, recognizes awaited Given/When/Then calls (singly, in tuples, in
/// arrays, or via a constant LINQ <c>.ToArray()</c>), and records each step's dataflow + source-order
/// dependencies, output binding, display name, and rewritten invocation.
/// Returns <c>null</c> when the body falls outside the supported subset (the analyzer reports why).
/// </summary>
internal sealed class ScenarioParser
{
    readonly SemanticModel _model;
    readonly IMethodSymbol _method;
    readonly MethodDeclarationSyntax _syntax;

    // Variable name -> the step(s) that produced it.
    readonly Dictionary<string, VarSource> _vars = new();

    // Namespaces that contain the invoked DSL extension members; imported into the generated file
    // so `Given.PatientExists(...)` resolves there too.
    readonly HashSet<string> _dslNamespaces = new();

    // Indices introduced by the previous top-level statement (source-order barrier / join target).
    List<int> _prevFrontier = new();

    int _nextIndex;
    string _scenarioId = "";

    readonly List<ParsedStep> _steps = new();

    ScenarioParser(SemanticModel model, IMethodSymbol method, MethodDeclarationSyntax syntax)
    {
        _model = model;
        _method = method;
        _syntax = syntax;
    }

    readonly struct VarSource
    {
        VarSource(bool isArray, int index, int[] indices, string elementType)
        {
            IsArray = isArray;
            Index = index;
            Indices = indices;
            ElementType = elementType;
        }

        public bool IsArray { get; }
        public int Index { get; }
        public int[] Indices { get; }
        public string ElementType { get; }

        public static VarSource Scalar(int index) => new(false, index, [], "");
        public static VarSource Array(int[] indices, string elementType) => new(true, -1, indices, elementType);
    }

    public static ParsedScenario? TryParse(SemanticModel model, IMethodSymbol method, MethodDeclarationSyntax syntax)
        => new ScenarioParser(model, method, syntax).Parse();

    ParsedScenario? Parse()
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

        var scenario = new ParsedScenario
        {
            MethodFullName = methodFullName,
            SafeName = SafeName(methodFullName),
            ScenarioId = _scenarioId,
            DisplayName = ReadScenarioDisplayName() ?? _method.Name,
            TimeoutMs = ReadScenarioTimeout(),
            SourceFile = Location(_syntax.Identifier, out var line),
            SourceLine = line,
        };

        scenario.Steps.AddRange(_steps);
        foreach (var u in CollectUsings())
        {
            scenario.Usings.Add(u);
        }

        foreach (var ns in _dslNamespaces)
        {
            scenario.Usings.Add($"using {ns};");
        }

        return scenario;
    }

    bool ParseStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case LocalDeclarationStatementSyntax local:
                return ParseLocalDeclaration(local);
            case ExpressionStatementSyntax expr:
                return ParseExpressionStatement(expr);
            default:
                return false;
        }
    }

    bool ParseLocalDeclaration(LocalDeclarationStatementSyntax local)
    {
        var variables = local.Declaration.Variables;
        if (variables.Count != 1 || variables[0].Initializer?.Value is not AwaitExpressionSyntax await)
        {
            return false;
        }

        return ParseAwaited(await.Expression, binding: SingleBinding(variables[0].Identifier.Text));
    }

    bool ParseExpressionStatement(ExpressionStatementSyntax statement)
    {
        switch (statement.Expression)
        {
            case AwaitExpressionSyntax bareAwait:
                return ParseAwaited(bareAwait.Expression, binding: null);
            case AssignmentExpressionSyntax { Right: AwaitExpressionSyntax await } assignment:
                return ParseAwaited(await.Expression, binding: DeconstructBinding(assignment.Left));
            default:
                return false;
        }
    }

    Binding SingleBinding(string name) => new(BindingKind.Single, [name]);

    Binding? DeconstructBinding(ExpressionSyntax left)
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

    enum BindingKind { Single, Tuple }

    sealed class Binding
    {
        public Binding(BindingKind kind, List<string> names)
        {
            Kind = kind;
            Names = names;
        }

        public BindingKind Kind { get; }
        public List<string> Names { get; }
    }

    bool ParseAwaited(ExpressionSyntax awaited, Binding? binding)
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

    bool ParseSingleCall(InvocationExpressionSyntax invocation, Binding? binding)
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

    bool ParseTuple(TupleExpressionSyntax tuple, Binding? binding)
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

    bool ParseArray(InitializerExpressionSyntax? initializer, Binding? binding)
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

    bool ParseLinqArray(InvocationExpressionSyntax toArray, Binding? binding)
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
                new Dictionary<string, string> { [loopVar] = value.ToString() }).Visit(bodyCall);

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

    static bool IsToArray(InvocationExpressionSyntax invocation)
        => invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ToArray" };

    /// <summary>
    /// Builds a step from a DSL invocation. <paramref name="semanticNode"/> is the invocation to use
    /// for semantic lookups when the emitted call has been syntactically rewritten (LINQ unroll).
    /// </summary>
    ParsedStep? BuildStep(
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

        var (template, formatExpr) = BuildDisplayName(method, invocation.ArgumentList.Arguments, replacements);

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
            TimeoutMs = ReadStepTimeout(method),
            SourceFile = Location(invocation, out var line),
            SourceLine = line,
        };

        foreach (var d in deps)
        {
            step.DependsOn.Add(d);
        }

        _steps.Add(step);
        return step;
    }

    List<int> CollectDataflowDeps(InvocationExpressionSyntax invocation)
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

    Dictionary<string, string> BuildReplacements()
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

    string BuildCallText(
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

    (string Template, string? FormatExpression) BuildDisplayName(
        IMethodSymbol method,
        SeparatedSyntaxList<ArgumentSyntax> args,
        Dictionary<string, string> replacements)
    {
        var template = ReadStepTemplate(method) ?? method.Name;
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
            var inModel = argExpr is not null && argExpr.SyntaxTree == _model.SyntaxTree;
            var constValue = inModel ? _model.GetConstantValue(argExpr!) : default;

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
        return (constant.ToString(), anyRuntime ? interpolation.ToString() : null);
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

    string? ReadScenarioDisplayName()
    {
        var attr = _method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "ScenarioAttribute");
        if (attr is { ConstructorArguments.Length: > 0 } && attr.ConstructorArguments[0].Value is string name)
        {
            return name;
        }

        return null;
    }

    int ReadScenarioTimeout()
    {
        var attr = _method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "ScenarioAttribute");
        return ReadTimeoutMs(attr, "Timeout"); // FactAttribute.Timeout (ms)
    }

    static int ReadStepTimeout(IMethodSymbol method)
        => ReadTimeoutMs(
            method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "StepNameAttribute"),
            "TimeoutMs");

    static int ReadTimeoutMs(AttributeData? attr, string namedArgument)
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

    static string? ReadStepTemplate(IMethodSymbol method)
    {
        var attr = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "StepNameAttribute");
        if (attr is { ConstructorArguments.Length: > 0 } && attr.ConstructorArguments[0].Value is string template)
        {
            return template;
        }

        return null;
    }

    IEnumerable<string> CollectUsings()
    {
        var root = _syntax.SyntaxTree.GetCompilationUnitRoot();
        var seen = new HashSet<string>();
        foreach (var u in root.Usings)
        {
            var text = u.ToString().Trim();
            if (seen.Add(text))
            {
                yield return text;
            }
        }
    }

    string? Location(SyntaxNode node, out int line)
    {
        var span = node.GetLocation().GetLineSpan();
        line = span.StartLinePosition.Line + 1;
        return span.Path;
    }

    string? Location(SyntaxToken token, out int line)
    {
        var span = token.GetLocation().GetLineSpan();
        line = span.StartLinePosition.Line + 1;
        return span.Path;
    }

    static string SafeName(string methodFullName)
    {
        var sb = new StringBuilder();
        foreach (var c in methodFullName)
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return sb.ToString();
    }

    /// <summary>Replaces bare identifiers (locals/loop variables) with provided expressions.</summary>
    sealed class IdentifierReplacer : CSharpSyntaxRewriter
    {
        readonly Dictionary<string, string> _map;

        public IdentifierReplacer(Dictionary<string, string> map) => _map = map;

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            // Don't rewrite the member-name side of a member access (e.g. the ".Method" part).
            if (node.Parent is MemberAccessExpressionSyntax member && member.Name == node)
            {
                return base.VisitIdentifierName(node);
            }

            if (_map.TryGetValue(node.Identifier.Text, out var replacement))
            {
                return SyntaxFactory.ParseExpression(replacement).WithTriviaFrom(node);
            }

            return base.VisitIdentifierName(node);
        }
    }
}
