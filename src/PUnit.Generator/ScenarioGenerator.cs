using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
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

        // Entry point: emit a Main calling PUnit.Mtp's bootstrap, gated on the MSBuild property
        // PUnitGenerateProgram (default true). The default-true read keeps "just add the package"
        // working without any property set; setting it to false lets a consumer own Program.cs.
        var generateProgram = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => ShouldGenerateProgram(provider));

        context.RegisterSourceOutput(generateProgram, static (spc, generate) =>
        {
            if (generate)
            {
                spc.AddSource(EntryPointEmitter.HintName, SourceText.From(EntryPointEmitter.Emit(), Encoding.UTF8));
            }
        });
    }

    static bool ShouldGenerateProgram(AnalyzerConfigOptionsProvider provider)
    {
        // Default true: emit unless the consumer explicitly opts out with PUnitGenerateProgram=false.
        if (provider.GlobalOptions.TryGetValue("build_property.PUnitGenerateProgram", out var value)
            && bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return true;
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
