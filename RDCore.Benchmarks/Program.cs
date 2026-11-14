using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

// dotnet run -c Release --project RDCore.Benchmarks -- --filter "*"
// dotnet run -c Release --project RDCore.Benchmarks -- --filter "*ParseBenchmarks*"
// each benchmark runs in a process of its own (BenchmarkDotNet's default), so ANTLR's DFA cache, which a process shares, does not carry from one benchmark to
// the next. Within the process the warm-up fills it: these are warm figures. The cold parse is rdc bench's Parse row.
// the analyzers' names run past the summary's default parameter width (20), which cuts them to "Impli(...)lyzer".
BenchmarkSwitcher.FromAssembly(typeof(RDCore.Benchmarks.Modules).Assembly)
    .Run(args, DefaultConfig.Instance.WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(64)));
