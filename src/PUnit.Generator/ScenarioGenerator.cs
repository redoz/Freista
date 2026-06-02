using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using PUnit.Generator.Emit;
using PUnit.Generator.Lowering;

namespace PUnit.Generator;

/// <summary>
/// Incremental generator that lowers every <c>[PUnit.Scenario]</c> method into a manifest +
/// executor graph, emitted as a single <c>PUnitScenarios.g.cs</c> registered with
/// <c>PUnit.ScenarioRegistry</c> via a module initializer.
/// </summary>
[Generator]
public sealed class ScenarioGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var scenarios = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "PUnit.ScenarioAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => Transform(ctx))
            .Where(static scenario => scenario is not null)
            .Collect();

        context.RegisterSourceOutput(scenarios, static (spc, items) =>
        {
            var list = items.OfType<ParsedScenario>().ToList();
            if (list.Count == 0)
            {
                return;
            }

            var source = ScenarioEmitter.Emit(list);
            spc.AddSource("PUnitScenarios.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    static ParsedScenario? Transform(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method || ctx.TargetNode is not MethodDeclarationSyntax syntax)
        {
            return null;
        }

        return ScenarioParser.TryParse(ctx.SemanticModel, method, syntax);
    }
}
