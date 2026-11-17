using CommandLine;
using RDCore.CLI.App.Bench;
using RDCore.CLI.ConsoleIO;
using RDCore.CLI.ConsoleIO.Model;
using System.Globalization;
using System.IO.Abstractions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace RDCore.CLI.App.Commands;

internal sealed class BenchMsVbaCommandOptions
{
    [Option('o', "out", Default = "msvba.json", HelpText = nameof(Resources.BenchMsVba_OutHelp), ResourceType = typeof(Resources))]
    public string Out { get; set; } = "msvba.json";

    [Option("rounds", Default = 5, HelpText = nameof(Resources.BenchMsVba_RoundsHelp), ResourceType = typeof(Resources))]
    public int Rounds { get; set; } = 5;

    [Option("excel-rounds", Default = 3, HelpText = nameof(Resources.BenchMsVba_ExcelRoundsHelp), ResourceType = typeof(Resources))]
    public int ExcelRounds { get; set; } = 3;

    [Option('r', "runs", Default = 7, HelpText = nameof(Resources.BenchMsVba_RunsHelp), ResourceType = typeof(Resources))]
    public int Runs { get; set; } = 7;

    [Option("excel-runs", Default = 5, HelpText = nameof(Resources.BenchMsVba_ExcelRunsHelp), ResourceType = typeof(Resources))]
    public int ExcelRuns { get; set; } = 5;
}

/// <summary>
/// <c>rdc bench-msvba</c>: the reference run of the benchmark. The built-in corpus and the Excel automation cases are run in MS-VBA and in RD-VBA, in alternated
/// rounds on this machine, and the fastest run of each case on each side is written as the reference data the CI bench job reads (<see cref="ReferenceData"/>).
/// </summary>
/// <remarks>
/// <para>
/// MS-VBA's side of a round is a new hidden Excel (<see cref="IMsVbaHost"/>): the corpus, the Excel automation cases and a harness module
/// (<see cref="MsVbaHarness"/>) imported into a new workbook, each case called directly between two <c>QueryPerformanceCounter</c> reads. RD-VBA's side is this
/// <c>rdc</c> run again (<see cref="IRdcProcess"/>): <c>rdc bench</c> on the built-in corpus with the compiled engine, and <c>rdc run</c> of the Excel automation
/// cases' program. On both sides the Excel automation cases automate an Excel instance of their own, in another process.
/// </para>
/// <para>
/// The cases are those <c>rdc bench --list</c> names. Nothing is written unless every case has a figure on both sides. Excel automation runs only on Windows:
/// elsewhere the verb says so and runs nothing.
/// </para>
/// </remarks>
/// <param name="writer">Writes the platform's messages: what went wrong.</param>
/// <param name="fileSystem">The file system the modules and the reference data are written to.</param>
/// <param name="output">Where the progress is written; the console unless said otherwise.</param>
/// <param name="host">Runs MS-VBA's side; Excel when none is given.</param>
/// <param name="rdc">Runs RD-VBA's side; this <c>rdc</c> when none is given.</param>
/// <param name="isWindows">Whether this is Windows; the operating system says when none is given.</param>
/// <param name="clock">The date the reference data is stamped with.</param>
internal sealed class BenchMsVbaCommand(IConsoleMessageWriter writer, IFileSystem fileSystem, TextWriter? output = null, IMsVbaHost? host = null,
    IRdcProcess? rdc = null, Func<bool>? isWindows = null, TimeProvider? clock = null)
    : ICliCommand
{
    private const string MsVba = "MS-VBA";
    private const string RdVba = "RD-VBA";
    private const string Engine = "Compiled";

    private static readonly TimeSpan ListTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BenchTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan ExcelTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ExcelEndTimeout = TimeSpan.FromSeconds(30);

    private TextWriter Output => output ?? System.Console.Out;

    public string Name => CommandNames.BenchMsVbaCommand.Name;
    public IReadOnlyList<string> Aliases => [CommandNames.BenchMsVbaCommand.Alias];
    public string Summary => Resources.BenchMsVba_Summary;

    public async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        var parsed = Parser.Default.ParseArguments<BenchMsVbaCommandOptions>(args);
        if (parsed.Errors.Any())
        {
            // CommandLine already wrote the usage/errors to stderr.
            return 2;
        }

        var options = parsed.Value;
        if (options.Rounds < 1 || options.Runs < 1 || options.ExcelRuns < 1 || options.ExcelRounds < 0 || options.ExcelRounds > options.Rounds)
        {
            Error(Resources.BenchMsVba_BadCounts);
            return 2;
        }

        if (!(isWindows ?? OperatingSystem.IsWindows)())
        {
            Error(Resources.BenchMsVba_NeedsWindows);
            return 1;
        }

        var msvba = host ?? (OperatingSystem.IsWindows() ? new ExcelMsVbaHost() : throw new PlatformNotSupportedException(Resources.BenchMsVba_NeedsWindows));
        var rdvba = rdc ?? new RdcProcess();

        // the cases, as this rdc runs them.
        var (listed, listing) = await rdvba.RunAsync(["bench", "--list", "-e", Engine], ListTimeout, token);
        var cases = BenchFigures.CaseList(listing);
        if (listed != 0 || cases.Count == 0)
        {
            Error(string.Format(CultureInfo.CurrentCulture, Resources.BenchMsVba_NoCaseList, listing.Trim()));
            return 1;
        }

        var corpus = BenchCommand.ExtractCorpus(fileSystem);
        var program = BenchCommand.ExtractResources(fileSystem, MsVbaHarness.ExcelResourcePrefix, "bench-excel");
        try
        {
            var modules = WriteModules(corpus, program, cases);
            var figures = new Figures();
            for (var round = 1; round <= options.Rounds; round++)
            {
                var excel = round <= options.ExcelRounds;
                await Output.WriteLineAsync(string.Format(CultureInfo.CurrentCulture, excel ? Resources.BenchMsVba_RoundWithExcel : Resources.BenchMsVba_Round, round, options.Rounds));
                var problems = new List<string>();
                foreach (var side in ReferenceData.MsVbaFirst(round) ? new[] { MsVba, RdVba } : [RdVba, MsVba])
                {
                    await Output.WriteLineAsync($"  {side}");
                    if (side == MsVba)
                    {
                        var result = msvba.Run(corpus, modules, options.Runs, excel ? options.ExcelRuns : 0, token);
                        figures.Excel ??= result.Excel;
                        BenchFigures.ReadHarness(result.Corpus, figures.MsVbaCases, problems);
                        if (result.ExcelCases is { } excelCases)
                        {
                            BenchFigures.ReadHarness(excelCases, figures.MsVbaExcel, problems);
                        }
                    }
                    else
                    {
                        string[] bench = ["bench", "--runs", Invariant(options.Runs), "--engine", Engine];
                        var (benched, report) = await rdvba.RunAsync(bench, BenchTimeout, token);
                        BenchFigures.ReadRdcBench(report, figures.RdVbaCases, problems);
                        Ended(bench, benched, BenchTimeout, problems);
                        if (excel)
                        {
                            var before = msvba.ExcelProcesses();
                            string[] run = ["run", program, "--", Invariant(options.ExcelRuns)];
                            var (ran, printed) = await rdvba.RunAsync(run, ExcelTimeout, token);
                            BenchFigures.ReadHarness(printed, figures.RdVbaExcel, problems);
                            Ended(run, ran, ExcelTimeout, problems);
                            await WaitForExcelAsync(msvba, before, token);
                        }
                    }

                    if (problems.Count > 0)
                    {
                        foreach (var problem in problems)
                        {
                            Error($"{side}: {problem}");
                        }

                        return 1;
                    }
                }
            }

            return Write(options, cases, figures);
        }
        catch (MsVbaHostException exception)
        {
            Error($"{MsVba}: {exception.Message}");
            return 1;
        }
        finally
        {
            foreach (var folder in new[] { corpus, program }.Where(fileSystem.Directory.Exists))
            {
                fileSystem.Directory.Delete(folder, recursive: true);
            }
        }
    }

    // what MS-VBA imports, beside the corpus in its folder: the corpus as MS-VBA reads it, the Excel automation cases, and the harness; in Excel's code page.
    private List<string> WriteModules(string corpus, string program, IReadOnlyList<string> cases)
    {
        var modules = new List<string>();
        foreach (var file in fileSystem.Directory.GetFiles(corpus).Order(StringComparer.OrdinalIgnoreCase))
        {
            fileSystem.File.WriteAllText(file, MsVbaHarness.Importable(fileSystem.File.ReadAllText(file)), Encoding.Latin1);
            modules.Add(fileSystem.Path.GetFileName(file));
        }

        var excelCases = fileSystem.File.ReadAllText(fileSystem.Path.Combine(program, MsVbaHarness.ExcelCasesFile));
        fileSystem.File.WriteAllText(fileSystem.Path.Combine(corpus, MsVbaHarness.ExcelCasesFile), MsVbaHarness.Importable(excelCases), Encoding.Latin1);
        modules.Add(MsVbaHarness.ExcelCasesFile);

        var harness = MsVbaHarness.ModuleName + ".bas";
        fileSystem.File.WriteAllText(fileSystem.Path.Combine(corpus, harness), MsVbaHarness.ModuleText(cases), Encoding.Latin1);
        modules.Add(harness);
        return modules;
    }

    private static void Ended(string[] arguments, int? exitCode, TimeSpan timeout, List<string> problems)
    {
        if (exitCode is null)
        {
            problems.Add(string.Format(CultureInfo.CurrentCulture, Resources.BenchMsVba_ProcessTimedOut, arguments[0], timeout.TotalMinutes));
        }
        else if (exitCode != 0 && problems.Count == 0)
        {
            problems.Add(string.Format(CultureInfo.CurrentCulture, Resources.BenchMsVba_ProcessFailed, arguments[0], exitCode));
        }
    }

    // an Excel the RD-VBA program started ends when the program quits it; one that is still there is said, and left alone.
    private async Task WaitForExcelAsync(IMsVbaHost msvba, IReadOnlySet<int> before, CancellationToken token)
    {
        var waited = TimeSpan.Zero;
        var left = msvba.ExcelProcesses().Except(before).ToArray();
        while (left.Length > 0 && waited < ExcelEndTimeout)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            waited += TimeSpan.FromSeconds(1);
            left = msvba.ExcelProcesses().Except(before).ToArray();
        }

        if (left.Length > 0)
        {
            writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Warning).WithMessageBody(
                string.Format(CultureInfo.CurrentCulture, Resources.BenchMsVba_ExcelLeftRunning, string.Join(", ", left))));
        }
    }

    private int Write(BenchMsVbaCommandOptions options, IReadOnlyList<string> cases, Figures figures)
    {
        var missing = new List<(string Name, string Side)>();
        void Check(IEnumerable<string> names, OrderedDictionary<string, double> ms, OrderedDictionary<string, double> rd)
        {
            foreach (var name in names)
            {
                if (!ms.ContainsKey(name))
                {
                    missing.Add((name, MsVba));
                }

                if (!rd.ContainsKey(name))
                {
                    missing.Add((name, RdVba));
                }
            }
        }

        Check(cases, figures.MsVbaCases, figures.RdVbaCases);
        Check([.. figures.MsVbaExcel.Keys.Union(figures.RdVbaExcel.Keys)], figures.MsVbaExcel, figures.RdVbaExcel);
        if (missing.Count > 0)
        {
            foreach (var (name, side) in missing)
            {
                Error(string.Format(CultureInfo.CurrentCulture, Resources.BenchMsVba_Missing, name, side));
            }

            return 1;
        }

        var stamp = new ReferenceStamp(Commit(), (clock ?? TimeProvider.System).GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            figures.Excel ?? string.Empty, OperatingSystemName(), Environment.ProcessorCount,
            ReferenceData.Method(options.Rounds, options.ExcelRounds, options.Runs, options.ExcelRuns));
        var json = ReferenceData.ToJson(stamp,
            cases.Select(name => KeyValuePair.Create(name, new ReferenceFigure(figures.MsVbaCases[name], figures.RdVbaCases[name]))),
            figures.MsVbaExcel.Keys.Select(name => KeyValuePair.Create(name, new ReferenceFigure(figures.MsVbaExcel[name], figures.RdVbaExcel[name]))));

        var path = fileSystem.Path.GetFullPath(options.Out);
        if (fileSystem.Path.GetDirectoryName(path) is { Length: > 0 } folder)
        {
            fileSystem.Directory.CreateDirectory(folder);
        }

        fileSystem.File.WriteAllText(path, json);
        Output.WriteLine(string.Format(CultureInfo.CurrentCulture, Resources.BenchMsVba_Written, cases.Count, figures.MsVbaExcel.Count, path));
        return 0;
    }

    // the commit this rdc was built from: what its informational version says after the +.
    private static string Commit()
    {
        var version = typeof(BenchMsVbaCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        return version.IndexOf('+', StringComparison.Ordinal) is var plus and >= 0 ? version[(plus + 1)..] : version;
    }

    // as the reference stamp names it: Windows 11 from build 22000 on.
    private static string OperatingSystemName() =>
        $"Windows {(Environment.OSVersion.Version.Build >= 22000 ? "11" : "10")} {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private void Error(string message) => writer.WriteMessage(new ConsoleMessageBuilder().WithKind(MessageKind.Error).WithMessageBody(message));

    // the fastest run of each case on each side, in milliseconds, in the order the cases ran; and the Excel that ran MS-VBA.
    private sealed class Figures
    {
        public OrderedDictionary<string, double> MsVbaCases { get; } = [];
        public OrderedDictionary<string, double> RdVbaCases { get; } = [];
        public OrderedDictionary<string, double> MsVbaExcel { get; } = [];
        public OrderedDictionary<string, double> RdVbaExcel { get; } = [];
        public string? Excel { get; set; }
    }
}
