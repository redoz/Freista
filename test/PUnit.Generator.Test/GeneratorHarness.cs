using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using PUnit.Generator;
using PUnit.Generator.Analysis;
using PUnit.Model;
using PUnit.Scheduling;

namespace PUnit.Generator.Test;

/// <summary>
/// Drives the generator over input source, then compiles + loads the result so tests can assert on
/// the actual generated <see cref="ScenarioDefinition"/> and even run it through the real scheduler.
/// This makes generator tests behavioral, not just snapshot-based.
/// </summary>
public static class GeneratorHarness
{
    static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    static ImmutableArray<MetadataReference> BuildReferences()
    {
        var tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var refs = tpa.Split(Path.PathSeparator)
            .Where(p => p.Length > 0)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        refs.Add(MetadataReference.CreateFromFile(typeof(Given).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(PUnit.ScenarioAttribute).Assembly.Location));
        return refs.ToImmutableArray();
    }

    public static GeneratorResult Run(string source, string assemblyName = "ScenarioTests")
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            assemblyName + "_" + Guid.NewGuid().ToString("N"),
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(
            [new ScenarioGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var genDiagnostics);

        var generatedTrees = output.SyntaxTrees.Where(t => t != tree).ToList();
        var generatedSource = string.Join("\n\n", generatedTrees.Select(t => t.ToString()));

        var emitDiagnostics = ImmutableArray<Diagnostic>.Empty;
        Assembly? assembly = null;
        using var ms = new MemoryStream();
        var emit = output.Emit(ms);
        emitDiagnostics = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        if (emit.Success)
        {
            assembly = Assembly.Load(ms.ToArray());
        }

        return new GeneratorResult(genDiagnostics, emitDiagnostics, generatedSource, assembly);
    }

    /// <summary>Runs the generator over source and returns the driver, for Verify snapshots.</summary>
    public static GeneratorDriver RunDriver(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "Snapshot",
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return CSharpGeneratorDriver
            .Create([new ScenarioGenerator().AsSourceGenerator()], parseOptions: parseOptions)
            .RunGenerators(compilation);
    }

    /// <summary>Runs the analyzer over source and returns just the PUnit diagnostics.</summary>
    public static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "Analyze_" + Guid.NewGuid().ToString("N"),
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new ScenarioAnalyzer()));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.Where(d => d.Id.StartsWith("PUNIT")).ToImmutableArray();
    }

    public static IReadOnlyList<ScenarioDefinition> Definitions(this GeneratorResult result)
    {
        Assert.NotNull(result.Assembly);
        var type = result.Assembly!.GetType("PUnit.Generated.PUnitGenerated");
        Assert.NotNull(type);
        var method = type!.GetMethod("CreateAll", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return (IReadOnlyList<ScenarioDefinition>)method!.Invoke(null, null)!;
    }

    public static Task<IReadOnlyList<StepResult>> RunAsync(this ScenarioDefinition definition, int maxParallelism = 0)
        => new ScenarioScheduler(maxParallelism).RunAsync(definition);
}

public sealed record GeneratorResult(
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    ImmutableArray<Diagnostic> EmitDiagnostics,
    string GeneratedSource,
    Assembly? Assembly)
{
    public void AssertCompiles()
    {
        Assert.True(
            GeneratorDiagnostics.IsEmpty,
            "generator diagnostics: " + string.Join("; ", GeneratorDiagnostics.Select(d => d.ToString())));
        Assert.True(
            EmitDiagnostics.IsEmpty,
            "generated code did not compile: " + string.Join("; ", EmitDiagnostics.Select(d => d.ToString())));
        Assert.NotNull(Assembly);
    }
}
