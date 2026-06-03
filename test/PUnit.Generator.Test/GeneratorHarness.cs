using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
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

    /// <summary>Mirrors <see cref="Run"/> but parses the input with a real file path, so spans
    /// carry that path (the generator's span-directive branch only fires for path-bearing input).</summary>
    public static GeneratorResult RunWithPath(string source, string path, string assemblyName = "ScenarioTests")
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, path: path);
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

        Assembly? assembly = null;
        using var ms = new MemoryStream();
        var emit = output.Emit(ms);
        var emitDiagnostics = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        if (emit.Success)
        {
            assembly = Assembly.Load(ms.ToArray());
        }

        return new GeneratorResult(genDiagnostics, emitDiagnostics, generatedSource, assembly);
    }

    /// <summary>Runs the generator over path-bearing source and emits a portable PDB; returns emit
    /// errors and the raw PDB bytes for sequence-point inspection.</summary>
    public static (ImmutableArray<Diagnostic> Errors, ImmutableArray<byte> Pdb) EmitWithPdb(string source, string path)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, path: path,
            encoding: System.Text.Encoding.UTF8);
        var compilation = CSharpCompilation.Create(
            "PdbSnapshot_" + Guid.NewGuid().ToString("N"),
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(
            [new ScenarioGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        using var dll = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var emit = output.Emit(dll, pdbStream: pdbStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        var errors = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        return (errors, [.. pdbStream.ToArray()]);
    }

    /// <summary>One sequence point read from a portable PDB. Line/column are 1-based;
    /// <see cref="IsHidden"/> points have line 0xFEEFEE and no meaningful coordinates.</summary>
    public sealed record SeqPoint(string Document, bool IsHidden, int StartLine, int StartColumn, int EndLine, int EndColumn);

    public static IReadOnlyList<SeqPoint> ReadSequencePoints(ImmutableArray<byte> pdb)
    {
        using var stream = new MemoryStream([.. pdb]);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var reader = provider.GetMetadataReader();

        var result = new List<SeqPoint>();
        foreach (var handle in reader.MethodDebugInformation)
        {
            var info = reader.GetMethodDebugInformation(handle);
            if (info.SequencePointsBlob.IsNil)
            {
                continue;
            }

            foreach (var sp in info.GetSequencePoints())
            {
                var doc = sp.Document.IsNil
                    ? ""
                    : reader.GetString(reader.GetDocument(sp.Document).Name);
                result.Add(new SeqPoint(doc, sp.IsHidden, sp.StartLine, sp.StartColumn, sp.EndLine, sp.EndColumn));
            }
        }

        return result;
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
