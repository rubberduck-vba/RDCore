using BenchmarkDotNet.Attributes;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Benchmarks;

// a parse result written to and read from the wire's JSON, as it goes from the ParseServer to the language server, and on to the environment host.
[MemoryDiagnoser]
public class SyntaxTreeJsonBenchmarks
{
    private ModuleParseResult _parse = null!;
    private string _json = null!;

    [Params(30, 300)]
    public int Functions { get; set; } = 300;

    [GlobalSetup]
    public void Setup()
    {
        _parse = new ModuleParser().Parse(new Uri("file:///bench/Big.bas"), Modules.Big(Functions));
        _json = PlatformJson.Serialize(_parse);
    }

    [Benchmark]
    public string Serialize() => PlatformJson.Serialize(_parse);

    [Benchmark]
    public ModuleParseResult Deserialize() => PlatformJson.Deserialize<ModuleParseResult>(_json);
}
