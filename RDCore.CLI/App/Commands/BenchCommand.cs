using CommandLine;
using RDCore.CLI.App.Bench;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Workspace;
using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RDCore.CLI.App.Commands;

internal sealed class BenchCommandOptions
{
    [Value(0, MetaName = "path", Required = false, HelpText = "A module file, or a workspace folder (with or without a .rdproj). The built-in corpus when omitted.")]
    public string? Path { get; set; }

    [Option('r', "runs", Default = 3, HelpText = "How many times each case is run; the fastest and the median run are reported.")]
    public int Runs { get; set; } = 3;

    [Option('f', "filter", HelpText = "Only the cases whose Module.Procedure name contains this text.")]
    public string? Filter { get; set; }

    [Option('l', "list", HelpText = "Load and sweep the workspace, and list its cases without running them.")]
    public bool List { get; set; }
}

/// <summary>
/// <c>rdc bench [path]</c>: times the interpreter on a workspace - parsing, loading and sweeping it, then each of its cases - and prints what it measured.
/// </summary>
/// <remarks>
/// A case is a <c>Public Sub Bench*()</c> of a standard module (<see cref="BenchRunner"/>). Nothing is asserted: the numbers are for comparing one build with
/// another, and a case that raises an error says so instead. With no path, the corpus built into <c>rdc</c> is run: arithmetic loops over <c>Long</c>,
/// <c>Double</c> and <c>Variant</c>, strings, arrays, and calls to procedures, objects and the standard library.
/// </remarks>
/// <param name="writer">Writes the platform's messages: what went wrong.</param>
/// <param name="fileSystem">The file system the workspace is on.</param>
/// <param name="projects">Reads a <c>.rdproj</c>.</param>
/// <param name="runner">Runs the benchmark.</param>
/// <param name="output">Where the report is written; the console unless said otherwise.</param>
internal sealed class BenchCommand(IConsoleMessageWriter writer, IFileSystem fileSystem, IProjectFileLoader projects, BenchRunner runner, TextWriter? output = null)
    : ICliCommand
{
    /// <summary>The prefix of the names of the corpus files embedded in the assembly.</summary>
    public const string CorpusResourcePrefix = "Bench/Corpus/";

    private TextWriter Output => output ?? System.Console.Out;

    public string Name => CommandNames.BenchCommand.Name;
    public IReadOnlyList<string> Aliases => [CommandNames.BenchCommand.Alias];
    public string Summary => Resources.Bench_Summary;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var parsed = Parser.Default.ParseArguments<BenchCommandOptions>(args);
        if (parsed.Errors.Any())
        {
            // CommandLine already wrote the usage/errors to stderr.
            return 2;
        }

        var options = parsed.Value;
        if (options.Runs < 1)
        {
            writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Error).WithMessageBody(Resources.Bench_RunsMustBePositive));
            return 2;
        }

        var corpus = options.Path is null ? ExtractCorpus() : null;
        try
        {
            BenchWorkspace workspace;
            try
            {
                workspace = await BenchWorkspace.LoadAsync(fileSystem, projects, corpus ?? options.Path!, token);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Error).WithMessageBody(exception.Message));
                return 1;
            }

            WriteHeader(workspace, corpus is not null);
            var report = await runner.RunAsync(
                workspace, new BenchOptions(options.Runs, options.Filter, options.List), name => Output.WriteLine($"  running {name}"), token);
            WriteReport(report, options.List);
            return report.Problems.Count > 0 || report.Cases.Any(benchCase => benchCase.Error is not null) ? 1 : 0;
        }
        finally
        {
            if (corpus is not null && fileSystem.Directory.Exists(corpus))
            {
                fileSystem.Directory.Delete(corpus, recursive: true);
            }
        }
    }

    // the corpus is compiled into rdc, and written to a folder of its own for the session to read, as any workspace is.
    private string ExtractCorpus()
    {
        var folder = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "RDCore", "bench", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        fileSystem.Directory.CreateDirectory(folder);

        var assembly = typeof(BenchCommand).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(CorpusResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            fileSystem.File.WriteAllText(fileSystem.Path.Combine(folder, resource[CorpusResourcePrefix.Length..]), reader.ReadToEnd());
        }

        return folder;
    }

    private void WriteHeader(BenchWorkspace workspace, bool builtIn)
    {
        var runtime = typeof(Runtime.Execution.RuntimeExecutionPipeline).Assembly;
        var configuration = runtime.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true ? "Debug" : "Release";
        Output.WriteLine($"rdc bench · {configuration} · {RuntimeInformation.FrameworkDescription} · {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture} · {Environment.ProcessorCount} cores");
        Output.WriteLine($"engine     {runner.Environment.ExecutionEngine} · {runner.Environment.Configuration}");
        Output.WriteLine($"workspace  {(builtIn ? Resources.Bench_BuiltInCorpus : workspace.Root)} ({workspace.Modules.Count} modules)");
    }

    private void WriteReport(BenchReport report, bool listOnly)
    {
        Output.WriteLine();
        Output.WriteLine($"{"parse",-10}{Milliseconds(report.Parse),12}");
        Output.WriteLine($"{"symbols",-10}{Milliseconds(report.Symbols),12}");
        Output.WriteLine($"{"load",-10}{Milliseconds(report.Load),12}");
        Output.WriteLine($"{"sweep",-10}{Milliseconds(report.Sweep),12}");
        Output.WriteLine($"{"compile",-10}{Milliseconds(report.Compile),12}");

        foreach (var problem in report.Problems)
        {
            writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Error).WithMessageBody(problem));
        }

        if (report.Cases.Count == 0)
        {
            Output.WriteLine();
            Output.WriteLine(Resources.Bench_NoCases);
            return;
        }

        var width = Math.Max(30, report.Cases.Max(benchCase => benchCase.Name.Length) + 2);
        Output.WriteLine();
        if (listOnly)
        {
            foreach (var benchCase in report.Cases)
            {
                Output.WriteLine(benchCase.Name);
            }

            return;
        }

        Output.WriteLine($"{"case".PadRight(width)}{"runs",6}{"min",14}{"median",14}");
        foreach (var benchCase in report.Cases)
        {
            Output.WriteLine(benchCase.Error is { } error
                ? $"{benchCase.Name.PadRight(width)}{benchCase.Runs.Count,6}  {error}"
                : $"{benchCase.Name.PadRight(width)}{benchCase.Runs.Count,6}{Milliseconds(benchCase.Min!.Value),14}{Milliseconds(benchCase.Median!.Value),14}");
        }
    }

    private static string Milliseconds(TimeSpan time) => time.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
}
