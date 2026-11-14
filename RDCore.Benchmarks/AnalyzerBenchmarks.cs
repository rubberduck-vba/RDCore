using System.IO.Abstractions;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.App.Bench;
using RDCore.CLI.Host.Handlers;
using RDCore.Parsing;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Workspace;

namespace RDCore.Benchmarks;

// the diagnostics extension's analyzers over the generated big module, with the static facts the environment host gives them, as a document request runs them.
internal static class AnalysisFixture
{
    // the analyzers the extension registers that take no constructor argument, in name order.
    public static readonly IReadOnlyList<IModuleAnalyzer> Analyzers = [.. typeof(RDCore.Diagnostics.CoreDiagnosticsApp).Assembly.GetTypes()
        .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IModuleAnalyzer).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
        .OrderBy(type => type.Name, StringComparer.Ordinal)
        .Select(type => (IModuleAnalyzer)Activator.CreateInstance(type)!)];

    // a workspace of the big module and a Program module, loaded into an environment session as rdc bench loads one, and the module's static facts read from it.
    public static async Task<ModuleAnalysisContext> LoadAsync(int functions)
    {
        var folder = Directory.CreateTempSubdirectory("rdcore-bench-").FullName;
        try
        {
            var source = Modules.Big(functions);
            await File.WriteAllTextAsync(Path.Combine(folder, "Big.bas"), source);
            await File.WriteAllTextAsync(Path.Combine(folder, "Program.bas"),
                "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n\r\nPublic Sub Main()\r\n    Debug.Print F1(1)\r\nEnd Sub\r\n");

            var fileSystem = new FileSystem();
            var workspace = await BenchWorkspace.LoadAsync(fileSystem, new ProjectFileLoader(fileSystem), folder);
            var runner = new BenchRunner(RuntimeEnvironmentProfile.Default, fileSystem, new ErrorDetailsRecorder(), NullLoggerFactory.Instance,
                new DirectoryLibrarySource(fileSystem, string.Empty));
            var (provider, _, problems) = await runner.LoadProgramAsync(workspace);
            using (provider)
            {
                if (problems.Count > 0)
                {
                    throw new InvalidOperationException($"The workspace did not load: {string.Join("; ", problems)}");
                }

                var result = await new HostSemanticsHandler(provider).Handle(new HostSemanticsParams { ModuleName = Modules.BigName }, CancellationToken.None);
                var semantics = PlatformJson.Deserialize<SemanticsPayload>(result.Json).Modules
                    .Single(module => module.Module.Fragment.TrimStart('#') == Modules.BigName);
                var uri = workspace.DocumentOf(workspace.Modules.Single(module => module.Name == Modules.BigName));
                return new ModuleAnalysisContext(uri, new ModuleParser().Parse(uri, source), semantics);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

// each analyzer alone over the big module.
[MemoryDiagnoser]
public class AnalyzerBenchmarks
{
    private ModuleAnalysisContext _context = null!;
    private IModuleAnalyzer _analyzer = null!;

    public static IEnumerable<string> Names => AnalysisFixture.Analyzers.Select(analyzer => analyzer.GetType().Name);

    [ParamsSource(nameof(Names))]
    public string Analyzer { get; set; } = "";

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _analyzer = AnalysisFixture.Analyzers.Single(analyzer => analyzer.GetType().Name == Analyzer);
        _context = await AnalysisFixture.LoadAsync(300);
    }

    [Benchmark]
    public int Analyze() => _analyzer.Analyze(_context).Count();
}

// every analyzer over the module, one after the other, as one document request runs them; at two sizes, to show what grows faster than the module.
[MemoryDiagnoser]
public class AllAnalyzersBenchmarks
{
    private ModuleAnalysisContext _context = null!;

    [Params(75, 300)]
    public int Functions { get; set; } = 300;

    [GlobalSetup]
    public async Task SetupAsync() => _context = await AnalysisFixture.LoadAsync(Functions);

    [Benchmark]
    public int AnalyzeAll()
    {
        var findings = 0;
        foreach (var analyzer in AnalysisFixture.Analyzers)
        {
            findings += analyzer.Analyze(_context).Count();
        }

        return findings;
    }
}
