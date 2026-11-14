namespace Pumpkin.DotNet.Tests;

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Pumpkin.DotNet.Api.Plugins;
using Pumpkin.DotNet.Runtime;
using Pumpkin.DotNet.SourceGenerator;

public sealed record GeneratorRunResult(
    string? GeneratedEntryPoint,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    ImmutableArray<Diagnostic> CompilationDiagnostics
);

public static class GeneratorTestHelper
{
    public static GeneratorRunResult Run(
        string sourceCode,
        Dictionary<string, string>? buildProperties = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        EnsureReference<PumpkinPlugin>(references);
        EnsureReference<PumpkinRuntime>(references);

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestPluginAssembly",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var optionsProvider = new InMemoryAnalyzerConfigOptionsProvider(buildProperties ?? []);
        var generator = new PumpkinPluginGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            optionsProvider: optionsProvider
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics
        );

        var runResult = driver.GetRunResult();
        var generatedFile = runResult.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("PumpkinPluginEntryPoint.g.cs", StringComparison.Ordinal));

        var compilationErrors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        return new GeneratorRunResult(
            GeneratedEntryPoint: generatedFile?.GetText().ToString(),
            GeneratorDiagnostics: generatorDiagnostics,
            CompilationDiagnostics: compilationErrors
        );
    }

    private static void EnsureReference<T>(List<MetadataReference> references)
    {
        var location = typeof(T).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(location) &&
            references.All(r => !string.Equals(r.Display, location, StringComparison.OrdinalIgnoreCase)))
        {
            references.Add(MetadataReference.CreateFromFile(location));
        }
    }

    private sealed class InMemoryAnalyzerConfigOptionsProvider(Dictionary<string, string> globalOptions)
        : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new MapOptions(globalOptions);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => MapOptions.Empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => MapOptions.Empty;

        private sealed class MapOptions(Dictionary<string, string> map) : AnalyzerConfigOptions
        {
            public static readonly MapOptions Empty = new([]);
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
                => map.TryGetValue(key, out value);
        }
    }
}