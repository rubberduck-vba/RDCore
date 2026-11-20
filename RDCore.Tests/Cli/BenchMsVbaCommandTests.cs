using NSubstitute;
using RDCore.CLI.App.Bench;
using RDCore.CLI.App.Commands;
using RDCore.CLI.ConsoleIO;
using RDCore.CLI.ConsoleIO.Model;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdc bench-msvba</c>: the reference run of the benchmark, MS-VBA in Excel against RD-VBA. Excel is not run here: MS-VBA's side and RD-VBA's processes are
/// stand-ins that answer as Excel's harness and <c>rdc</c> answer, so what is tested is the harness the verb writes, how it reads both sides, the order of the
/// rounds, and the reference data it writes.
/// </summary>
[TestClass]
public sealed class BenchMsVbaCommandTests
{
    private static readonly string[] Cases = ["Arrays.BenchFill", "Calls.BenchRecursion"];

    [TestMethod]
    [TestCategory("CLI")]
    public void Harness_CallsEachCaseByNameInOrder()
    {
        var text = MsVbaHarness.ModuleText(Cases);

        Assert.StartsWith("Attribute VB_Name = \"XlBench\"\r\n", text);
        Assert.Contains("Private Const CaseCount As Long = 2\r\n", text);
        Assert.Contains("        Case 1: CaseName = \"Arrays.BenchFill\"\r\n", text);
        Assert.Contains("        Case 2: CaseName = \"Calls.BenchRecursion\"\r\n", text);
        Assert.Contains("        Case 1: Arrays.BenchFill\r\n", text);
        Assert.Contains("        Case 2: Calls.BenchRecursion\r\n", text);
        foreach (var procedure in new[] { "TimeCorpus(ByVal Runs As Long)", "HostProcess()", "Bitness()", "StartTarget()", "TimeExcel(ByVal Runs As Long)" })
        {
            Assert.Contains($"Public Function {procedure}", text);
        }

        Assert.Contains("Public Sub StopTarget()", text);
        Assert.DoesNotContain("\r\r", text);
        Assert.DoesNotMatchRegex(new System.Text.RegularExpressions.Regex("[^\r]\n"), text);
    }

    [TestMethod]
    [TestCategory("CLI")]
    public void Harness_NeedsNoLineContinuationWhateverTheCases()
    {
        // a VBA statement continues over 24 lines at most, and a line holds 1,023 characters: the harness names each case on a line of its own.
        var many = Enumerable.Range(1, 500).Select(index => $"Module{index}.BenchCase{index}").ToArray();

        var lines = MsVbaHarness.ModuleText(many).Split("\r\n");

        Assert.IsFalse(lines.Any(line => line.EndsWith(" _", StringComparison.Ordinal)));
        Assert.IsTrue(lines.All(line => line.Length <= 1023));
    }

    [TestMethod]
    [TestCategory("CLI")]
    public void Importable_NamesLocalLocalCountAndEndsLinesWithCrLf()
    {
        var source = "    Dim Local As Long\n    Local = Local + 1\n    Debug.Print Locale, MyLocal\n";

        Assert.AreEqual("    Dim LocalCount As Long\r\n    LocalCount = LocalCount + 1\r\n    Debug.Print Locale, MyLocal\r\n", MsVbaHarness.Importable(source));
    }

    [TestMethod]
    [TestCategory("CLI")]
    public void ReadHarness_KeepsTheFastestRunOfAllTheRoundsInMilliseconds()
    {
        var fastest = new Dictionary<string, double>();
        var problems = new List<string>();

        BenchFigures.ReadHarness("Arrays.BenchFill|900;582;610;\nCalls.BenchRecursion|9000;8930;\n", fastest, problems);
        BenchFigures.ReadHarness("Arrays.BenchFill|700;650;\r\nCalls.BenchRecursion|8000;9100;\r\n", fastest, problems);

        Assert.IsEmpty(problems);
        Assert.AreEqual(0.582, fastest["Arrays.BenchFill"], 1e-9);
        Assert.AreEqual(8.0, fastest["Calls.BenchRecursion"], 1e-9);
    }

    [TestMethod]
    [TestCategory("CLI")]
    public void ReadHarness_NamesThePhaseOfAnExcelAutomationCase()
    {
        var fastest = new Dictionary<string, double>();
        var problems = new List<string>();

        BenchFigures.ReadHarness("Excel.Block1000x10|read|5097;6000;\nExcel.Block1000x10|total|69471;\nExcel.CellByCell1000x10|total|30689146;\n", fastest, problems);

        Assert.IsEmpty(problems);
        CollectionAssert.AreEqual(new[] { "Excel.Block1000x10.Read", "Excel.Block1000x10.Total", "Excel.CellByCell1000x10.Total" }, fastest.Keys.ToArray());
        Assert.AreEqual(30689.146, fastest["Excel.CellByCell1000x10.Total"], 1e-9);
    }

    [TestMethod]
    [TestCategory("CLI")]
    public void ReadHarness_SaysACaseThatRaised()
    {
        var fastest = new Dictionary<string, double>();
        var problems = new List<string>();

        BenchFigures.ReadHarness("Arrays.BenchFill|582;\nCalls.BenchRecursion|error 28 Out of stack space\n", fastest, problems);

        CollectionAssert.AreEqual(new[] { "Calls.BenchRecursion|error 28 Out of stack space" }, problems);
        CollectionAssert.AreEqual(new[] { "Arrays.BenchFill" }, fastest.Keys.ToArray());
    }

    [TestMethod]
    [TestCategory("CLI")]
    public void ReadRdcBench_ReadsTheFastestRunWithOrWithoutWhatItAllocated()
    {
        var fastest = new Dictionary<string, double>();
        var problems = new List<string>();
        const string report = """
            rdc bench · Release · .NET 11.0.0 · Microsoft Windows 10.0.26300 X64 · 12 cores
              running Arrays.BenchFill

            parse          760.4 ms
            case                                     runs           min        median     allocated    gc 0/1/2
            Arrays.BenchFill                            7        4.5 ms        4.7 ms        120 KB       0/0/0
            Calls.BenchRecursion                        7       17.3 ms       18.0 ms
            Calls.BenchFails                            7  Run-time error 5: Invalid procedure call or argument
            """;

        BenchFigures.ReadRdcBench(report, fastest, problems);

        Assert.AreEqual(4.5, fastest["Arrays.BenchFill"], 1e-9);
        Assert.AreEqual(17.3, fastest["Calls.BenchRecursion"], 1e-9);
        Assert.HasCount(1, problems);
        Assert.StartsWith("Calls.BenchFails", problems[0]);
    }

    [TestMethod]
    [TestCategory("CLI")]
    public void CaseList_NamesTheCasesRdcBenchLists()
    {
        const string listing = """
            rdc bench · Release · .NET 11.0.0 · Microsoft Windows 10.0.26300 X64 · 12 cores
            engine     Compiled · Debug
            workspace  (built-in corpus) (12 modules)

            parse          760.4 ms
            symbols        160.4 ms

            Arrays.BenchFill
            Calls.BenchRecursion
            """;

        CollectionAssert.AreEqual(Cases, BenchFigures.CaseList(listing).ToArray());
    }

    [TestMethod]
    [TestCategory("CLI")]
    public void Method_SaysHowTheRoundsRan()
    {
        Assert.AreEqual(
            "alternated rounds on one machine: 5 for the corpus, the first 3 with Excel automation (MS-VBA first in odd rounds, RD-VBA first in even rounds); "
            + "7 runs a case (5 for Excel automation), fastest run; MS-VBA runs the Excel automation cases from a second Excel instance, as RD-VBA runs them out "
            + "of process, so both cross a process boundary",
            ReferenceData.Method(5, 3, 7, 5));
        Assert.AreEqual("alternated rounds on one machine: 1 for the corpus (MS-VBA first); 3 runs a case, fastest run", ReferenceData.Method(1, 0, 3, 5));
        Assert.AreEqual(
            "alternated rounds on one machine: 1 for the corpus (MS-VBA first); 3 runs a case, fastest run; corpus cases warmed up first: RD-VBA by untimed runs "
            + "of each case first, at least 2, until the JIT compiles nothing for 500 ms (at most 10 s), MS-VBA by 2 untimed runs",
            ReferenceData.Method(1, 0, 3, 5, BenchWarmUp.UntilTheJitIsQuiet));
    }

    [TestMethod]
    [TestCategory("CLI")]
    public void Harness_RunsEachCaseUntimed_AsOftenAsRdcBenchAtTheFewest_BeforeItsTimedRuns()
    {
        var text = MsVbaHarness.ModuleText(Cases);

        Assert.Contains($"Private Const WarmUpRuns As Long = {BenchWarmUp.UntilTheJitIsQuiet.Runs}\r\n", text);
        var warmUp = text.IndexOf("        For R = 1 To WarmUpRuns\r\n            RunCase I\r\n        Next\r\n", StringComparison.Ordinal);
        var timed = text.IndexOf("        For R = 1 To Runs\r\n            QueryPerformanceCounter T0\r\n", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, warmUp);
        Assert.IsGreaterThan(warmUp, timed);
    }

    [TestMethod]
    [TestCategory("CLI")]
    public void MsVbaRunsFirstInOddRounds()
    {
        CollectionAssert.AreEqual(new[] { true, false, true, false, true }, Enumerable.Range(1, 5).Select(ReferenceData.MsVbaFirst).ToArray());
    }

    [TestMethod]
    [TestCategory("CLI")]
    public void ToJson_WritesTheShapeTheBenchJobReads()
    {
        var stamp = new ReferenceStamp("94aa5a36e", "2026-11-17", "16.0 build 20527 (64-bit)", "Windows 11 x64", 12, "the method");

        var json = ReferenceData.ToJson(stamp, [KeyValuePair.Create("Arrays.BenchFill", new ReferenceFigure(0.58249, 4.5))],
            [KeyValuePair.Create("Excel.Block1000x10.Read", new ReferenceFigure(5.097, 33.531))]);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CollectionAssert.AreEqual(new[] { "reference", "cases", "excel" }, root.EnumerateObject().Select(property => property.Name).ToArray());
        var reference = root.GetProperty("reference");
        Assert.AreEqual("94aa5a36e", reference.GetProperty("commit").GetString());
        Assert.AreEqual("2026-11-17", reference.GetProperty("date").GetString());
        Assert.AreEqual("16.0 build 20527 (64-bit)", reference.GetProperty("excel").GetString());
        Assert.AreEqual("Windows 11 x64", reference.GetProperty("os").GetString());
        Assert.AreEqual(12, reference.GetProperty("cores").GetInt32());
        Assert.AreEqual("the method", reference.GetProperty("method").GetString());
        Assert.AreEqual(0.582, root.GetProperty("cases").GetProperty("Arrays.BenchFill").GetProperty("msvba").GetDouble());
        Assert.AreEqual(4.5, root.GetProperty("cases").GetProperty("Arrays.BenchFill").GetProperty("rdvba").GetDouble());
        Assert.AreEqual(33.531, root.GetProperty("excel").GetProperty("Excel.Block1000x10.Read").GetProperty("rdvba").GetDouble());
        Assert.DoesNotContain("\r", json);
    }

    [TestMethod]
    [TestCategory("CLI")]
    public void TheExcelAutomationCasesAreBuiltIn()
    {
        var fileSystem = new MockFileSystem();

        var folder = BenchCommand.ExtractResources(fileSystem, MsVbaHarness.ExcelResourcePrefix, "bench-excel-test");

        CollectionAssert.AreEquivalent(new[] { MsVbaHarness.ExcelCasesFile, MsVbaHarness.ExcelProgramFile },
            fileSystem.Directory.GetFiles(folder).Select(fileSystem.Path.GetFileName).ToArray());
        Assert.Contains("Public Function RunExcelCases(ByVal Xl As Object, ByVal Runs As Long) As String",
            fileSystem.File.ReadAllText(fileSystem.Path.Combine(folder, MsVbaHarness.ExcelCasesFile)));
        Assert.Contains("RunExcelCases(Xl, CLng(Command$))", fileSystem.File.ReadAllText(fileSystem.Path.Combine(folder, MsVbaHarness.ExcelProgramFile)));
    }

    [TestMethod]
    [TestCategory("CLI")]
    public async Task RunsNothingOffWindows()
    {
        var (command, _, writer, host, rdc) = Command(new MockFileSystem(), isWindows: false);

        var exitCode = await command.ExecuteAsync([], CancellationToken.None);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(CLI.Resources.BenchMsVba_NeedsWindows, Body(writer.Written.Single()));
        Assert.IsEmpty(host.Rounds);
        Assert.IsEmpty(rdc.Calls);
    }

    [TestMethod]
    [TestCategory("CLI")]
    [DataRow("--rounds", "0")]
    [DataRow("--runs", "0")]
    [DataRow("--excel-runs", "0")]
    [DataRow("--excel-rounds", "6")]
    public async Task RefusesCountsThatCannotRun(string option, string value)
    {
        var (command, _, writer, host, rdc) = Command(new MockFileSystem());

        var exitCode = await command.ExecuteAsync([option, value], CancellationToken.None);

        Assert.AreEqual(2, exitCode);
        Assert.AreEqual(CLI.Resources.BenchMsVba_BadCounts, Body(writer.Written.Single()));
        Assert.IsEmpty(host.Rounds);
        Assert.IsEmpty(rdc.Calls);
    }

    [TestMethod]
    [TestCategory("CLI")]
    public async Task AlternatesTheSidesAndWritesTheFastestRunOfEachCase()
    {
        var fileSystem = new MockFileSystem();
        var (command, _, writer, host, rdc) = Command(fileSystem);
        var output = Path.Combine(Path.GetTempPath(), "rdcore-bench-msvba", "msvba.json");

        var exitCode = await command.ExecuteAsync(["--rounds", "3", "--excel-rounds", "2", "--runs", "2", "--excel-runs", "1", "--out", output], CancellationToken.None);

        Assert.AreEqual(0, exitCode, string.Join(Environment.NewLine, writer.Written.Select(Body)));
        // MS-VBA first in rounds 1 and 3, RD-VBA first in round 2; the Excel automation cases in rounds 1 and 2 only.
        CollectionAssert.AreEqual(
            new[] { "list", "MS-VBA 1", "bench", "run", "bench", "run", "MS-VBA 2", "MS-VBA 3", "bench" },
            host.Log.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 1, 0 }, host.Rounds.Select(round => round.ExcelRuns).ToArray());
        CollectionAssert.AreEqual(new[] { "bench", "--runs", "2", "--engine", "Compiled" }, rdc.Calls[1].ToArray());
        Assert.AreEqual("--", rdc.Calls[2][2]);
        Assert.AreEqual("1", rdc.Calls[2][3]);

        // what MS-VBA imported: the corpus, the Excel automation cases, and the harness last, which names the listed cases.
        var modules = host.Rounds[0].Modules;
        Assert.Contains("Slots.bas", modules);
        Assert.AreEqual(MsVbaHarness.ExcelCasesFile, modules[^2]);
        Assert.AreEqual("XlBench.bas", modules[^1]);
        Assert.Contains("Case 2: Calls.BenchRecursion", host.Rounds[0].Harness);
        Assert.DoesNotMatchRegex(new System.Text.RegularExpressions.Regex(@"\bLocal\b"), host.Rounds[0].Slots);

        using var document = JsonDocument.Parse(fileSystem.File.ReadAllText(output));
        var root = document.RootElement;
        Assert.AreEqual("16.0 build 20527 (64-bit)", root.GetProperty("reference").GetProperty("excel").GetString());
        Assert.AreEqual("2026-11-17", root.GetProperty("reference").GetProperty("date").GetString());
        Assert.AreEqual(ReferenceData.Method(3, 2, 2, 1, BenchWarmUp.UntilTheJitIsQuiet), root.GetProperty("reference").GetProperty("method").GetString());
        CollectionAssert.AreEqual(Cases, root.GetProperty("cases").EnumerateObject().Select(property => property.Name).ToArray());
        // the fastest of the rounds on each side: MS-VBA's third round and RD-VBA's second.
        Assert.AreEqual(0.1, root.GetProperty("cases").GetProperty("Arrays.BenchFill").GetProperty("msvba").GetDouble(), 1e-9);
        Assert.AreEqual(2.0, root.GetProperty("cases").GetProperty("Arrays.BenchFill").GetProperty("rdvba").GetDouble(), 1e-9);
        Assert.AreEqual(5.0, root.GetProperty("excel").GetProperty("Excel.Block1000x10.Read").GetProperty("msvba").GetDouble(), 1e-9);
        Assert.AreEqual(30.0, root.GetProperty("excel").GetProperty("Excel.Block1000x10.Read").GetProperty("rdvba").GetDouble(), 1e-9);
    }

    [TestMethod]
    [TestCategory("CLI")]
    public async Task WritesNothingWhenACaseRaises()
    {
        var fileSystem = new MockFileSystem();
        var (command, _, writer, host, _) = Command(fileSystem);
        host.Raises = "Calls.BenchRecursion";
        var output = Path.Combine(Path.GetTempPath(), "rdcore-bench-msvba", "raises.json");

        var exitCode = await command.ExecuteAsync(["--rounds", "1", "--excel-rounds", "0", "--out", output], CancellationToken.None);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual("MS-VBA: Calls.BenchRecursion|error 28 Out of stack space", Body(writer.Written.Single()));
        Assert.IsFalse(fileSystem.File.Exists(output));
    }

    [TestMethod]
    [TestCategory("CLI")]
    public async Task WritesNothingWhenACaseHasNoFigureOnOneSide()
    {
        var fileSystem = new MockFileSystem();
        var (command, _, writer, _, rdc) = Command(fileSystem);
        rdc.Leaves = "Calls.BenchRecursion";
        var output = Path.Combine(Path.GetTempPath(), "rdcore-bench-msvba", "missing.json");

        var exitCode = await command.ExecuteAsync(["--rounds", "1", "--excel-rounds", "0", "--out", output], CancellationToken.None);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(string.Format(CLI.Resources.BenchMsVba_Missing, "Calls.BenchRecursion", "RD-VBA"), Body(writer.Written.Single()));
        Assert.IsFalse(fileSystem.File.Exists(output));
    }

    [TestMethod]
    [TestCategory("CLI")]
    public async Task SaysWhatStoppedExcel()
    {
        var (command, _, writer, host, _) = Command(new MockFileSystem());
        host.Stops = "Excel showed a dialog";

        var exitCode = await command.ExecuteAsync(["--rounds", "1", "--excel-rounds", "1"], CancellationToken.None);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual("MS-VBA: Excel showed a dialog", Body(writer.Written.Single()));
    }

    private static string Body(ConsoleMessageBuilder message) => message.Parts.OfType<ConsoleMessageBodyPart>().Single().Body;

    private static (BenchMsVbaCommand Command, StringWriter Output, CapturingWriter Writer, StandInExcel Host, StandInRdc Rdc) Command(
        MockFileSystem fileSystem, bool isWindows = true)
    {
        var output = new StringWriter();
        var writer = new CapturingWriter();
        var log = new List<string>();
        var host = new StandInExcel(fileSystem, log);
        var rdc = new StandInRdc(log);
        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
        clock.LocalTimeZone.Returns(TimeZoneInfo.Utc);
        return (new BenchMsVbaCommand(writer, fileSystem, output, host, rdc, () => isWindows, clock), output, writer, host, rdc);
    }

    private sealed class CapturingWriter : IConsoleMessageWriter
    {
        public List<ConsoleMessageBuilder> Written { get; } = [];

        public IConsoleMessageWriter WriteMessage(ConsoleMessageBuilder builder)
        {
            Written.Add(builder);
            return this;
        }

        public IConsoleMessageWriter Clear() => this;
        public IConsoleMessageWriter WriteException(Exception exception) => this;
        public IConsoleMessageWriter WriteAssemblyInfo() => this;
        public IConsoleMessageWriter WriteSlogan(int indent = 0) => this;
        public IConsoleMessageWriter WriteLegalNotice() => this;
    }

    // what Excel's harness returns: each case's runs, faster each round (round 3: 0.1 ms and 0.2 ms); each Excel phase at 5 ms.
    private sealed class StandInExcel(MockFileSystem fileSystem, List<string> log) : IMsVbaHost
    {
        public List<(IReadOnlyList<string> Modules, int ExcelRuns, string Harness, string Slots)> Rounds { get; } = [];
        public List<string> Log => log;
        public string? Raises { get; set; }
        public string? Stops { get; set; }

        public MsVbaRound Run(string folder, IReadOnlyList<string> modules, int runs, int excelRuns, CancellationToken token)
        {
            Rounds.Add((modules, excelRuns, fileSystem.File.ReadAllText(Path.Combine(folder, "XlBench.bas")), fileSystem.File.ReadAllText(Path.Combine(folder, "Slots.bas"))));
            log.Add($"MS-VBA {Rounds.Count}");
            if (Stops is { } stops)
            {
                throw new MsVbaHostException(stops);
            }

            var round = Rounds.Count;
            var corpus = string.Join("\n", Cases.Select((name, index) => name == Raises
                ? $"{name}|error 28 Out of stack space"
                : $"{name}|{(4 - round) * 100 + index * 100};{(4 - round) * 100 + 50 + index * 100};"));
            return new MsVbaRound("16.0 build 20527 (64-bit)", corpus + "\n", excelRuns > 0 ? "Excel.Block1000x10|read|5000;\n" : null);
        }

        public IReadOnlySet<int> ExcelProcesses() => new HashSet<int>();
    }

    // what rdc answers: the listed cases, then each bench's runs (fastest in the second bench, 2 ms), and each Excel phase at 30 ms.
    private sealed class StandInRdc(List<string> log) : IRdcProcess
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public string? Leaves { get; set; }

        public Task<(int? ExitCode, string Output)> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token)
        {
            Calls.Add(arguments);
            if (arguments.Contains("--list"))
            {
                log.Add("list");
                return Task.FromResult<(int?, string)>((0, "parse   1.0 ms\n\n" + string.Join("\n", Cases) + "\n"));
            }

            log.Add(arguments[0]);
            if (arguments[0] == "run")
            {
                return Task.FromResult<(int?, string)>((0, "Excel.Block1000x10|read|30000;\n"));
            }

            var benches = Calls.Count(call => call[0] == "bench" && !call.Contains("--list"));
            var milliseconds = benches == 2 ? 2.0 : 3.0;
            var report = string.Join("\n", Cases.Where(name => name != Leaves)
                .Select(name => $"{name,-40}    7 {milliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} ms    9.9 ms"));
            return Task.FromResult<(int?, string)>((0, report + "\n"));
        }
    }
}
