using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Freista.Generator.Analysis;
using Freista.Generator.Emit;
using Freista.Generator.Lowering;

namespace Freista.Generator;

/// <summary>
/// Incremental generator that lowers every <c>[Freista.Scenario]</c> method into a manifest +
/// executor graph, emitted as a single <c>FreistaScenarios.g.cs</c> registered with
/// <c>Freista.ScenarioRegistry</c> via a module initializer.
/// </summary>
[Generator]
public sealed class ScenarioGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var scenarios = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Freista.ScenarioAttribute",
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => Transform(ctx))
            .Where(static result => result is not null)
            .Collect();

        context.RegisterSourceOutput(scenarios, static (spc, items) =>
        {
            var parsed = new List<ParsedScenario>();
            foreach (var result in items)
            {
                if (result is not { } r)
                {
                    continue;
                }

                if (r.Error is not null)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Descriptors.UnhandledException, MakeLocation(r.File, r.Line), r.Error));
                }
                else if (r.Scenario is not null)
                {
                    parsed.Add(r.Scenario);
                }
            }

            if (parsed.Count == 0)
            {
                return;
            }

            var (source, error) = GeneratorSafety.SafeEmit(() => ScenarioEmitter.Emit(parsed));
            if (error is not null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Descriptors.UnhandledException, Location.None, error));
                return;
            }

            spc.AddSource("FreistaScenarios.g.cs", SourceText.From(source!, Encoding.UTF8));
        });

        // Entry point: emit a Main calling Freista.Mtp's bootstrap, gated on the MSBuild property
        // FreistaGenerateProgram (default true). The default-true read keeps "just add the package"
        // working without any property set; setting it to false lets a consumer own Program.cs.
        var generateProgram = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => ShouldGenerateProgram(provider));

        context.RegisterSourceOutput(generateProgram, static (spc, generate) =>
        {
            if (!generate)
            {
                return;
            }

            var (source, error) = GeneratorSafety.SafeEmit(EntryPointEmitter.Emit);
            if (error is not null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Descriptors.UnhandledException, Location.None, error));
                return;
            }

            spc.AddSource(EntryPointEmitter.HintName, SourceText.From(source!, Encoding.UTF8));
        });
    }

    private static bool ShouldGenerateProgram(AnalyzerConfigOptionsProvider provider)
    {
        // Default true: emit unless the consumer explicitly opts out with FreistaGenerateProgram=false.
        if (provider.GlobalOptions.TryGetValue("build_property.FreistaGenerateProgram", out var value)
            && bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return true;
    }

    private static ScenarioResult? Transform(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method || ctx.TargetNode is not MethodDeclarationSyntax syntax)
        {
            return null;
        }

        var lineSpan = syntax.Identifier.GetLocation().GetLineSpan();
        return GeneratorSafety.SafeParse(
            () => ScenarioParser.TryParse(ctx.SemanticModel, method, syntax),
            lineSpan.Path,
            lineSpan.StartLinePosition.Line + 1);
    }

    /// <summary>A 1-based file/line location for a diagnostic, or <see cref="Location.None"/> when the
    /// input had no path.</summary>
    private static Location MakeLocation(string? file, int line)
    {
        if (string.IsNullOrEmpty(file) || line <= 0)
        {
            return Location.None;
        }

        var position = new LinePosition(line - 1, 0);
        return Location.Create(file!, new TextSpan(0, 0), new LinePositionSpan(position, position));
    }
}
