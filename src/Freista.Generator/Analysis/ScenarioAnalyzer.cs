using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Freista.Generator;
using Freista.Generator.Lowering;

namespace Freista.Generator.Analysis;

/// <summary>
/// Validates that <c>[Scenario]</c> methods stay inside the lowerable subset, and that
/// <c>[StepName]</c> templates bind to parameters, reporting clear FRST diagnostics otherwise.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ScenarioAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        Descriptors.UnhandledException,
        Descriptors.MustBeAsyncTask,
        Descriptors.UnsupportedStatement,
        Descriptors.UnsupportedControlFlow,
        Descriptors.NotADslCall,
        Descriptors.InvalidReturnType,
        Descriptors.InvalidGroupElement,
        Descriptors.InvalidArgument,
        Descriptors.UnboundPlaceholder,
        Descriptors.MissingResourceRole,
        Descriptors.InvalidLineageSubject,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        try
        {
            AnalyzeMethodCore(context);
        }
        catch (Exception ex)
        {
            var location = ((MethodDeclarationSyntax)context.Node).Identifier.GetLocation();
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.UnhandledException, location, GeneratorSafety.Describe(ex)));
        }
    }

    private static void AnalyzeMethodCore(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(method) is not IMethodSymbol symbol)
        {
            return;
        }

        if (HasAttribute(symbol, "StepNameAttribute"))
        {
            AnalyzeStepName(context, symbol);
            AnalyzeStepResources(context, symbol);
        }

        if (!HasAttribute(symbol, "ScenarioAttribute"))
        {
            return;
        }

        if (!symbol.IsAsync || !SymbolHelpers.IsVoidTaskLike(symbol.ReturnType))
        {
            Report(context, Descriptors.MustBeAsyncTask, method.Identifier.GetLocation(), symbol.Name);
        }

        if (method.Body is null)
        {
            return;
        }

        var stepOutputs = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        foreach (var statement in method.Body.Statements)
        {
            AnalyzeStatement(context, statement, stepOutputs);
        }
    }

    private static void AnalyzeStatement(
        SyntaxNodeAnalysisContext context,
        StatementSyntax statement,
        HashSet<ILocalSymbol> stepOutputs)
    {
        switch (statement)
        {
            case EmptyStatementSyntax:
                return;

            case LocalDeclarationStatementSyntax local:
                AnalyzeLocalDeclaration(context, local, stepOutputs);
                return;

            case ExpressionStatementSyntax expr:
                AnalyzeExpressionStatement(context, expr, stepOutputs);
                return;

            case IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax
                or WhileStatementSyntax or DoStatementSyntax or SwitchStatementSyntax
                or TryStatementSyntax or UsingStatementSyntax or LockStatementSyntax
                or GotoStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax
                or ThrowStatementSyntax or YieldStatementSyntax or LabeledStatementSyntax
                or FixedStatementSyntax or CheckedStatementSyntax or UnsafeStatementSyntax
                or LocalFunctionStatementSyntax or ReturnStatementSyntax:
                Report(context, Descriptors.UnsupportedControlFlow, statement.GetLocation());
                return;

            default:
                Report(context, Descriptors.UnsupportedStatement, statement.GetLocation());
                return;
        }
    }

    private static void AnalyzeLocalDeclaration(
        SyntaxNodeAnalysisContext context,
        LocalDeclarationStatementSyntax local,
        HashSet<ILocalSymbol> stepOutputs)
    {
        var variables = local.Declaration.Variables;
        if (variables.Count != 1 || variables[0].Initializer?.Value is not AwaitExpressionSyntax await)
        {
            Report(context, Descriptors.UnsupportedStatement, local.GetLocation());
            return;
        }

        AnalyzeAwaited(context, await.Expression, stepOutputs);

        if (context.SemanticModel.GetDeclaredSymbol(variables[0]) is ILocalSymbol declared)
        {
            stepOutputs.Add(declared);
        }
    }

    private static void AnalyzeExpressionStatement(
        SyntaxNodeAnalysisContext context,
        ExpressionStatementSyntax statement,
        HashSet<ILocalSymbol> stepOutputs)
    {
        switch (statement.Expression)
        {
            case AwaitExpressionSyntax bareAwait:
                AnalyzeAwaited(context, bareAwait.Expression, stepOutputs);
                return;

            case AssignmentExpressionSyntax { Right: AwaitExpressionSyntax await } assignment:
                AnalyzeAwaited(context, await.Expression, stepOutputs);
                RecordDeconstructedLocals(context, assignment.Left, stepOutputs);
                return;

            default:
                Report(context, Descriptors.UnsupportedStatement, statement.GetLocation());
                return;
        }
    }

    private static void AnalyzeAwaited(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax awaited,
        HashSet<ILocalSymbol> stepOutputs)
    {
        switch (awaited)
        {
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ToArray" }:
                AnalyzeLinqArray(context, invocation, stepOutputs);
                return;

            case InvocationExpressionSyntax invocation:
                AnalyzeDslCall(context, invocation, stepOutputs, Descriptors.NotADslCall);
                return;

            case TupleExpressionSyntax tuple:
                foreach (var arg in tuple.Arguments)
                {
                    AnalyzeGroupElement(context, arg.Expression, stepOutputs);
                }

                return;

            case ArrayCreationExpressionSyntax array:
                AnalyzeArrayElements(context, array.Initializer, awaited, stepOutputs);
                return;

            case ImplicitArrayCreationExpressionSyntax array:
                AnalyzeArrayElements(context, array.Initializer, awaited, stepOutputs);
                return;

            default:
                Report(context, Descriptors.UnsupportedStatement, awaited.GetLocation());
                return;
        }
    }

    private static void AnalyzeArrayElements(
        SyntaxNodeAnalysisContext context,
        InitializerExpressionSyntax? initializer,
        ExpressionSyntax awaited,
        HashSet<ILocalSymbol> stepOutputs)
    {
        if (initializer is null)
        {
            Report(context, Descriptors.UnsupportedStatement, awaited.GetLocation());
            return;
        }

        foreach (var element in initializer.Expressions)
        {
            AnalyzeGroupElement(context, element, stepOutputs);
        }
    }

    private static void AnalyzeGroupElement(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax element,
        HashSet<ILocalSymbol> stepOutputs)
    {
        if (element is InvocationExpressionSyntax invocation)
        {
            AnalyzeDslCall(context, invocation, stepOutputs, Descriptors.InvalidGroupElement);
        }
        else
        {
            Report(context, Descriptors.InvalidGroupElement, element.GetLocation());
        }
    }

    private static void AnalyzeDslCall(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        HashSet<ILocalSymbol> stepOutputs,
        DiagnosticDescriptor notDslDescriptor)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member
            || SymbolHelpers.PhaseOf(member.Expression, context.SemanticModel) is null)
        {
            Report(context, notDslDescriptor, invocation.GetLocation());
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
            && !SymbolHelpers.TryUnwrapReturn(method.ReturnType, out _))
        {
            Report(context, Descriptors.InvalidReturnType, invocation.GetLocation(), method.Name);
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            foreach (var identifier in argument.Expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                // Skip member names (`x.Member`) and argument labels (`name:`); flag any other
                // identifier that binds to a local which isn't a prior step output.
                if ((identifier.Parent is MemberAccessExpressionSyntax access && access.Name == identifier)
                    || identifier.Parent is NameColonSyntax or NameEqualsSyntax)
                {
                    continue;
                }

                if (context.SemanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol local
                    && !stepOutputs.Contains(local))
                {
                    Report(context, Descriptors.InvalidArgument, identifier.GetLocation(), identifier.Identifier.Text);
                }
            }
        }
    }

    private static void AnalyzeLinqArray(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax toArray,
        HashSet<ILocalSymbol> stepOutputs)
    {
        // Enumerable.Range(constStart, constCount).Select(i => <DSL call>).ToArray()
        if (toArray.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax selectInv }
            && selectInv.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Select" }
            && selectInv.ArgumentList.Arguments.Count == 1
            && selectInv.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax { Body: InvocationExpressionSyntax body }
            && selectInv.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax rangeInv }
            && rangeInv.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Range" }
            && rangeInv.ArgumentList.Arguments.Count == 2
            && context.SemanticModel.GetConstantValue(rangeInv.ArgumentList.Arguments[0].Expression).Value is int
            && context.SemanticModel.GetConstantValue(rangeInv.ArgumentList.Arguments[1].Expression).Value is int)
        {
            // Validate the per-element call resolves to a DSL member with a valid return type.
            if (body.Expression is MemberAccessExpressionSyntax bodyMember
                && SymbolHelpers.PhaseOf(bodyMember.Expression, context.SemanticModel) is not null)
            {
                if (context.SemanticModel.GetSymbolInfo(body).Symbol is IMethodSymbol method
                    && !SymbolHelpers.TryUnwrapReturn(method.ReturnType, out _))
                {
                    Report(context, Descriptors.InvalidReturnType, body.GetLocation(), method.Name);
                }
            }
            else
            {
                Report(context, Descriptors.InvalidGroupElement, body.GetLocation());
            }

            return;
        }

        Report(context, Descriptors.UnsupportedStatement, toArray.GetLocation());
    }

    private static void RecordDeconstructedLocals(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax left,
        HashSet<ILocalSymbol> stepOutputs)
    {
        foreach (var designation in left.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>())
        {
            if (context.SemanticModel.GetDeclaredSymbol(designation) is ILocalSymbol local)
            {
                stepOutputs.Add(local);
            }
        }
    }

    private static void AnalyzeStepName(SyntaxNodeAnalysisContext context, IMethodSymbol method)
    {
        var attribute = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "StepNameAttribute");
        if (attribute is not { ConstructorArguments.Length: > 0 }
            || attribute.ConstructorArguments[0].Value is not string template)
        {
            return;
        }

        var parameters = method.Parameters.Select(p => p.Name).ToImmutableHashSet();
        foreach (var token in TemplateTokenizer.Tokenize(template))
        {
            if (token.IsPlaceholder && !parameters.Contains(token.Text))
            {
                var location = method.Locations.FirstOrDefault() ?? Location.None;
                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.UnboundPlaceholder, location, token.Text, method.Name));
            }
        }
    }

    private static void AnalyzeStepResources(SyntaxNodeAnalysisContext context, IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if (IsResourceType(parameter.Type) && AttributeReader.ParameterRole(parameter) is null)
            {
                var location = parameter.Locations.FirstOrDefault() ?? method.Locations.FirstOrDefault() ?? Location.None;
                context.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.MissingResourceRole, location, "parameter", parameter.Name));
            }
        }

        if (SymbolHelpers.TryUnwrapReturn(method.ReturnType, out var resultType)
            && resultType is not null
            && IsResourceType(resultType)
            && AttributeReader.ReturnRole(method) is null)
        {
            var location = method.Locations.FirstOrDefault() ?? Location.None;
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.MissingResourceRole, location, "return", method.Name));
        }

        var editParamNames = method.Parameters
            .Where(p => AttributeReader.ParameterRole(p) == "Edit")
            .Select(p => p.Name)
            .ToImmutableHashSet();
        var returnIsSubject = SymbolHelpers.TryUnwrapReturn(method.ReturnType, out var subjectReturn)
            && subjectReturn is not null
            && AttributeReader.ReturnRole(method) is "Create" or "Edit";

        foreach (var parameter in method.Parameters)
        {
            if (AttributeReader.ParameterRole(parameter) is not ("Reference" or "Consume"))
            {
                continue;
            }

            foreach (var subject in AttributeReader.ParameterSubjects(parameter))
            {
                var valid = subject == AttributeReader.ReturnSubject
                    ? returnIsSubject
                    : editParamNames.Contains(subject);
                if (!valid)
                {
                    var location = parameter.Locations.FirstOrDefault() ?? method.Locations.FirstOrDefault() ?? Location.None;
                    context.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.InvalidLineageSubject, location, subject, method.Name));
                }
            }
        }
    }

    /// <summary>
    /// True when <paramref name="type"/> participates in the resource model — i.e. it implements
    /// <c>Freista.IResource&lt;TSelf&gt;</c> (arity 1) or <c>Freista.IResourceIdentity</c>. A trailing
    /// <c>Freista.ScenarioContext</c> param is naturally excluded; so is <c>ISingletonResource&lt;&gt;</c>,
    /// which does not implement <c>IResource&lt;&gt;</c>.
    /// </summary>
    private static bool IsResourceType(ITypeSymbol type)
        => type.AllInterfaces.Any(i =>
            i.ContainingNamespace?.ToDisplayString(SymbolHelpers.NoGlobal) == "Freista"
            && ((i.Name == "IResource" && i.Arity == 1) || i.Name == "IResourceIdentity"));

    private static bool HasAttribute(IMethodSymbol method, string attributeName)
        => method.GetAttributes().Any(a =>
            a.AttributeClass?.Name == attributeName
            && a.AttributeClass.ContainingNamespace?.ToDisplayString(SymbolHelpers.NoGlobal) == "Freista");

    private static void Report(
        SyntaxNodeAnalysisContext context,
        DiagnosticDescriptor descriptor,
        Location location,
        params object[] args)
        => context.ReportDiagnostic(Diagnostic.Create(descriptor, location, args));
}
