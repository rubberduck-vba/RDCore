using BenchmarkDotNet.Attributes;
using RDCore.Parsing;

namespace RDCore.Benchmarks;

// the parse of one module, as the ParseServer makes it: the syntax tree, its trivia and its syntax errors.
[MemoryDiagnoser]
public class ParseBenchmarks
{
    private readonly ModuleParser _parser = new();
    private Uri _uri = null!;
    private string _source = null!;

    [Params(Modules.BigName, "Statements.bas", "Strings.bas", "Counter.cls")]
    public string Module { get; set; } = Modules.BigName;

    [GlobalSetup]
    public void Setup()
    {
        _source = Module == Modules.BigName ? Modules.Big() : Modules.Corpus(Module);
        _uri = new Uri($"file:///bench/{(Module == Modules.BigName ? "Big.bas" : Module)}");
    }

    [Benchmark]
    public object Parse() => _parser.Parse(_uri, _source);
}
