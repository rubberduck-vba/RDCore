using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Bench;
using RDCore.CLI.App.Commands;
using RDCore.CLI.ConsoleIO;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Runtime;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Workspace;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdc bench</c>: the interpreter timed on a workspace, in-process - what is loaded, which procedures are its cases, and what is reported of them. Nothing here
/// measures anything: the numbers are the command's to print, and a test only sees that they are there.
/// </summary>
[TestClass]
public sealed class BenchCommandTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-bench-ws");

    private static string Module(string name, params string[] body) => $"Attribute VB_Name = \"{name}\"\r\nOption Explicit\r\n{string.Join("\r\n", body)}\r\n";

    private static readonly string Cases = Module("Cases",
        "Public Sub BenchCount()",
        "    Dim I As Long",
        "    For I = 1 To 10",
        "    Next",
        "End Sub",
        "Public Sub BenchCallsAnObject()",
        "    Dim T As Tally",
        "    Set T = New Tally",
        "    T.Add",
        "End Sub",
        "Public Sub BenchFails()",
        "    Err.Raise 5",
        "End Sub",
        "Private Sub BenchPrivateIsNoCase()",
        "End Sub",
        "Public Sub BenchWithAnArgumentIsNoCase(ByVal X As Long)",
        "End Sub",
        "Public Function BenchFunctionIsNoCase() As Long",
        "End Function",
        "Public Sub NotNamedForACase()",
        "End Sub",
        "Public Sub CallsLateBoundInALoop()",
        "    Dim I As Long, T As Long, O As Object",
        "    Set O = New Tally",
        "    For I = 1 To 3",
        "        O.Add",
        "        Debug.Print T",
        "        T = T + I",
        "    Next",
        "End Sub",
        "Public Sub CountsInACell()",
        "    Dim I As Long",
        "    For I = 1 To 2",
        "        Touched I",
        "    Next",
        "End Sub",
        "Private Sub Touched(ByRef N As Long)",
        "End Sub");

    private static readonly string Tally = ClassModule("Tally",
        "Option Explicit",
        "Private mCount As Long",
        "Public Sub Add()",
        "    mCount = mCount + 1",
        "End Sub",
        "Public Sub BenchInAClassIsNoCase()",
        "End Sub");

    private static MockFileSystem Files(params (string Path, string Source)[] files)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.Directory.CreateDirectory(Root);
        foreach (var (path, source) in files)
        {
            fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Root, path))!);
            fileSystem.File.WriteAllText(Path.Combine(Root, path), source);
        }

        return fileSystem;
    }

    private static Task<BenchWorkspace> Load(MockFileSystem fileSystem, string? path = null)
        => BenchWorkspace.LoadAsync(fileSystem, new ProjectFileLoader(fileSystem), path ?? Root);

    private static BenchRunner Runner(MockFileSystem fileSystem, RDCore.SDK.Runtime.Libraries.ILibrarySource? libraries = null)
        => new(RuntimeEnvironmentProfile.Default, fileSystem, Substitute.For<IErrorDetailsRecorder>(), NullLoggerFactory.Instance,
            libraries ?? new RDCore.SDK.Runtime.Libraries.DirectoryLibrarySource(fileSystem, string.Empty));

    // ---- the workspace ----

    [TestMethod]
    public async Task AFolder_IsEveryModuleUnderIt_NamedAndKindedAsTheModuleSays()
    {
        var workspace = await Load(Files(("Cases.bas", Cases), ("Model/Tally.cls", Tally), ("notes.txt", "not a module")));

        CollectionAssert.AreEqual(new[] { "Cases", "Tally" }, workspace.Modules.Select(module => module.Name).ToArray());
        CollectionAssert.AreEqual(new[] { ModuleType.StdModule, ModuleType.ClassModule }, workspace.Modules.Select(module => module.Type).ToArray());
        CollectionAssert.AreEqual(new[] { "Cases.bas", "Model/Tally.cls" }, workspace.Project.Modules.Select(module => module.RelativeUri).ToArray());
        Assert.IsEmpty(workspace.Project.References, "the standard library is every project's own, and no reference of it");
    }

    [TestMethod]
    [DataRow("vba", new[] { "Cases.bas", "ThisWorkbook.doccls", "Model/Tally.cls" }, DisplayName = "RD-VBA: a document module is one, a UserControl is not")]
    [DataRow("vb6", new[] { "Cases.bas", "Gauge.ctl", "Model/Tally.cls" }, DisplayName = "VB6: a UserControl is one, a document module is not")]
    public async Task AFolderWithNoProject_IsTheProjectTheLanguageServerMakesOfIt_OfTheFilesOfItsLanguage(string language, string[] modules)
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Model/Tally.cls", Tally), ("ThisWorkbook.doccls", ClassModule("ThisWorkbook")),
            ("Gauge.ctl", ClassModule("Gauge")), ("notes.txt", "not a module"));
        var supported = SupportedLanguages.Get(language);

        var workspace = await BenchWorkspace.LoadAsync(fileSystem, new ProjectFileLoader(fileSystem), Root, CancellationToken.None, supported);
        var synthesized = (await new ProjectFileLoader(fileSystem).LoadOrSynthesizeAsync(Root, supported)).ProjectInfo;

        CollectionAssert.AreEquivalent(modules, workspace.Project.Modules.Select(module => module.RelativeUri).ToArray());
        CollectionAssert.AreEqual(synthesized.Modules.Select(module => module.RelativeUri).ToArray(),
            workspace.Project.Modules.Select(module => module.RelativeUri).ToArray(), "in the language server's order");
    }

    // a folder's .doccls is what legacy Rubberduck exported of a document's code: the text of its code pane, which no project gives a host (Super), so it is
    // the standard module its text makes it, as the language server reads it (DocumentModuleTests); nothing of it is hosted.
    [TestMethod]
    public async Task AFoldersDocumentModule_WithNothingInIt_LoadsAsAStandardModule_AndTheCasesRun()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally), ("ThisWorkbook.doccls", "Option Explicit\r\n"));

        var workspace = await Load(fileSystem);
        var report = await Runner(fileSystem).RunAsync(workspace, new BenchOptions(Runs: 1, Filter: "count"));

        Assert.AreEqual(ModuleType.StdModule, workspace.Modules.Single(module => module.Name == "ThisWorkbook").Type);
        Assert.IsEmpty(report.Problems, string.Join("; ", report.Problems));
        Assert.IsNull(report.Cases.Single().Error, report.Cases.Single().Error);
    }

    [TestMethod]
    public async Task AFoldersDocumentModule_WhoseCodeNamesItsDocument_IsAProblem_AsInTheLanguageServer_AndTheCasesAreRefused()
    {
        var sheet = "Option Explicit\r\nPublic Sub Reset()\r\n    Me.Range(\"A1\").Value = 1\r\nEnd Sub\r\n";
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally), ("GameSheet.doccls", sheet));

        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 1, Filter: "count"));

        Assert.IsTrue(report.Problems.Any(problem => problem.StartsWith("GameSheet:", StringComparison.Ordinal) && problem.Contains("Invalid use of Me keyword")),
            string.Join("; ", report.Problems));
        Assert.IsEmpty(report.Cases.Single().Runs);
    }

    [TestMethod]
    public async Task AFile_IsAWorkspaceOfThatOneModule()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var workspace = await Load(fileSystem, Path.Combine(Root, "Cases.bas"));

        Assert.AreEqual(Root, workspace.Root);
        CollectionAssert.AreEqual(new[] { "Cases" }, workspace.Modules.Select(module => module.Name).ToArray());
    }

    [TestMethod]
    public async Task TheDocumentOfEachModule_AndTheRoot_AreFileUris_ReadBackAsTheyAreWritten()
    {
        var workspace = await Load(Files(("Cases.bas", Cases), ("Model/Tally.cls", Tally)));

        // a parse and a built program's image travel as JSON, which writes a Uri as its text and reads it back as relative or absolute: the text of a path
        // that begins with / is read back relative, on Linux.
        foreach (var uri in workspace.Modules.Select(workspace.DocumentOf).Append(workspace.RootUri))
        {
            Assert.AreEqual(Uri.UriSchemeFile, uri.Scheme, uri.OriginalString);
            var read = new Uri(uri.OriginalString, UriKind.RelativeOrAbsolute);
            Assert.IsTrue(read.IsAbsoluteUri, uri.OriginalString);
            Assert.AreEqual(uri, read);
        }

        Assert.AreEqual(Path.Combine(Root, "Model", "Tally.cls"), workspace.DocumentOf(workspace.Modules[1]).LocalPath);
    }

    [TestMethod]
    public async Task AFolderWithAProject_IsTheProjectItDescribes()
    {
        var project = new ProjectFile(Root, new RDCoreProject { Name = "Ducks", Modules = [new RDCoreModule { RelativeUri = "Tally.cls" }] });
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally), (ProjectFile.FileName, JsonSerializer.Serialize(project)));

        var workspace = await Load(fileSystem);

        Assert.AreEqual("Ducks", workspace.Project.Name);
        CollectionAssert.AreEqual(new[] { "Tally" }, workspace.Modules.Select(module => module.Name).ToArray(), "the modules the project lists, and no others");
    }

    [TestMethod]
    public async Task APathThatIsNothing_IsNotFound()
        => await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => Load(Files(), Path.Combine(Root, "nothing")));

    // ---- the runner ----

    [TestMethod]
    public async Task AWorkspaceThatReferencesALibrary_IsBenchedWithTheLibrarysTypes()
    {
        // the platform's libraries, as the environment host has them: a project that references one names its types.
        var library = new RDCore.SDK.Runtime.Libraries.LibraryDescription
        {
            Name = "Rig",
            Classes = [new RDCore.SDK.Runtime.Libraries.ClassDescription { Name = "Valve", Members = [new RDCore.SDK.Runtime.Libraries.MemberDescription { Name = "Gate" }] }],
        };
        var fileSystem = Files(("Uses.bas", StandardModule("Uses", "Private Valve As Rig.Valve", "Public Sub Gate()", "    Valve.Gate", "End Sub")));
        fileSystem.File.WriteAllText(Path.Combine(Root, ProjectFile.FileName), JsonSerializer.Serialize(new ProjectFile(Root, new RDCoreProject
        {
            Name = "Rig",
            Modules = [new RDCoreModule { RelativeUri = "Uses.bas" }],
            References = [new RDCoreReference { Name = "Rig" }],
        })));

        var report = await Runner(fileSystem, new RDCore.Tests.Runtime.Libraries.InMemoryLibrarySource(library)).RunAsync(await Load(fileSystem), new BenchOptions(ListOnly: true));

        Assert.IsEmpty(report.Problems, string.Join("\n", report.Problems));
    }

    [TestMethod]
    public async Task TheCases_AreThePublicParameterlessBenchSubsOfStandardModules_InTheOrderTheyAreDeclared()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(ListOnly: true));

        CollectionAssert.AreEqual(new[] { "Cases.BenchCount", "Cases.BenchCallsAnObject", "Cases.BenchFails" }, report.Cases.Select(benchCase => benchCase.Name).ToArray());
        Assert.IsTrue(report.Cases.All(benchCase => benchCase.Runs.Count == 0), "a listed case is not run");
        Assert.IsEmpty(report.Problems);
    }

    [TestMethod]
    public async Task EachCase_IsRunAsManyTimesAsAsked_AndTimed()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var running = new List<string>();
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 2, Filter: "count"), running.Add);

        var counted = report.Cases.Single();
        Assert.AreEqual("Cases.BenchCount", counted.Name);
        Assert.HasCount(2, counted.Runs);
        Assert.IsNull(counted.Error);
        Assert.IsTrue(counted.Min > TimeSpan.Zero && counted.Min <= counted.Median);
        CollectionAssert.AreEqual(new[] { "Cases.BenchCount" }, running, "told of each case as it starts");
    }

    [TestMethod]
    public async Task EachRun_HasWhatItAllocated_AndTheCollectionsMadeWhileItRan()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 2, Filter: "callsanobject"), _ => { });

        var benchCase = report.Cases.Single();
        Assert.IsNull(benchCase.Error);
        Assert.IsNotNull(benchCase.Costs);
        Assert.HasCount(2, benchCase.Costs, "a cost for each timed run");
        Assert.IsTrue(benchCase.FastestCost!.Value.Bytes > 0, "a run that makes an object allocates");
    }

    [TestMethod]
    public async Task ACaseThatCallsAnObjectOfAClass_Runs()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 1, Filter: "object"));
        Assert.IsNull(report.Cases.Single().Error, report.Cases.Single().Error);
    }

    [TestMethod]
    public async Task ACaseThatRaisesAnError_SaysWhich_AndIsNotRunAgain()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 3, Filter: "fails"));

        var failed = report.Cases.Single();
        Assert.IsEmpty(failed.Runs);
        StringAssert.Contains(failed.Error, "5");
    }

    // ---- the warm-up ----

    private static readonly string Counted = Module("Counted",
        "Private Calls As Long",
        "Public Sub BenchFailsOnItsThirdCall()",
        "    Calls = Calls + 1",
        "    If Calls = 3 Then Err.Raise 6",
        "End Sub");

    [TestMethod]
    public async Task TheWarmUp_RunsEachCaseUntimed_BeforeItsTimedRuns_AndTheirCostsAreNotTheCases()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var warmUp = new BenchWarmUp(2, TimeSpan.Zero, TimeSpan.FromDays(1));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 3, Filter: "callsanobject", WarmUp: warmUp));

        var benchCase = report.Cases.Single();
        Assert.IsNull(benchCase.Error, benchCase.Error);
        Assert.AreEqual(2, benchCase.WarmUpRuns);
        Assert.HasCount(3, benchCase.Runs, "the timed runs are as many as asked");
        Assert.HasCount(3, benchCase.Costs!, "a cost for each timed run, none for a warm-up run");
    }

    [TestMethod]
    public async Task TheWarmUp_IsRunsOfTheProgram_SoTheTimedRunsComeAfterThem()
    {
        var fileSystem = Files(("Counted.bas", Counted));
        var warmUp = new BenchWarmUp(2, TimeSpan.Zero, TimeSpan.FromDays(1));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 3, WarmUp: warmUp));

        // the third call is the first timed run: it raises, and no run is kept.
        var benchCase = report.Cases.Single();
        Assert.AreEqual(2, benchCase.WarmUpRuns);
        Assert.IsEmpty(benchCase.Runs);
        StringAssert.Contains(benchCase.Error, "6");
    }

    [TestMethod]
    public async Task ACaseThatRaisesAnErrorInItsWarmUp_SaysWhich_AndIsNotTimed()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var warmUp = new BenchWarmUp(2, TimeSpan.Zero, TimeSpan.FromDays(1));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 3, Filter: "fails", WarmUp: warmUp));

        var failed = report.Cases.Single();
        Assert.AreEqual(1, failed.WarmUpRuns);
        Assert.IsEmpty(failed.Runs);
        StringAssert.Contains(failed.Error, "5");
    }

    [TestMethod]
    public async Task TheWarmUp_EndsAtItsLimit_AfterItsFirstRun_HoweverFewRunsItMade()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var warmUp = new BenchWarmUp(1000, TimeSpan.Zero, TimeSpan.Zero);
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 1, Filter: "count", WarmUp: warmUp));

        var benchCase = report.Cases.Single();
        Assert.AreEqual(1, benchCase.WarmUpRuns);
        Assert.HasCount(1, benchCase.Runs);
    }

    [TestMethod]
    public async Task TheWarmUp_EndsAtItsLimit_WhateverTheJitDoes()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        // never quiet enough: only the limit ends it.
        var warmUp = new BenchWarmUp(1, TimeSpan.FromDays(1), TimeSpan.FromMilliseconds(50));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 1, Filter: "count", WarmUp: warmUp));

        var benchCase = report.Cases.Single();
        Assert.IsNull(benchCase.Error, benchCase.Error);
        Assert.IsGreaterThanOrEqualTo(1, benchCase.WarmUpRuns);
        Assert.HasCount(1, benchCase.Runs);
    }

    [TestMethod]
    public async Task WithoutAWarmUp_TheFirstRunIsTimed()
    {
        var fileSystem = Files(("Counted.bas", Counted));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 3));

        var benchCase = report.Cases.Single();
        Assert.AreEqual(0, benchCase.WarmUpRuns);
        Assert.HasCount(2, benchCase.Runs, "the first two calls are timed; the third raises");
    }

    // the indentation of the shell's program is a bench of its own, which a filter selects by its name as it selects a case: a run of one case does not
    // wait for it.
    [TestMethod]
    [DataRow("count", false, 1, DisplayName = "a filter naming one case")]
    [DataRow(BenchRunner.IndentName, true, 0, DisplayName = "its name")]
    [DataRow("indent", true, 0, DisplayName = "part of its name")]
    [DataRow(null, true, 3, DisplayName = "no filter")]
    public async Task TheIndent_IsRun_WhenTheFilterSelectsIt(string? filter, bool indented, int cases)
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 1, Filter: filter));

        Assert.AreEqual(indented, report.Indent is not null);
        Assert.HasCount(cases, report.Cases);
    }

    // nothing of the project runs while a module of it does not load: a case of another module is refused with the problem, and not run.
    [TestMethod]
    public async Task AModuleThatDoesNotParse_IsAProblem_AndTheCasesOfTheOthersAreRefused()
    {
        var fileSystem = Files(("Broken.bas", Module("Broken", "Public Sub BenchBroken(", "End Sub")), ("Cases.bas", Cases), ("Tally.cls", Tally));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 1, Filter: "count"));

        StringAssert.StartsWith(report.Problems.Single(), "Broken:");
        var refused = report.Cases.Single();
        Assert.AreEqual("Cases.BenchCount", refused.Name);
        Assert.IsEmpty(refused.Runs);
        StringAssert.Contains(refused.Error, "Broken:");
    }

    // a syntax error in a branch of a #If that is excluded is in no code of the module, which loads: the cases run.
    [TestMethod]
    public async Task ASyntaxErrorInAnExcludedBranch_IsNoProblem_AndTheCasesRun()
    {
        var dead = Module("Dead", "Public Sub Helper()", "#If False Then", "Debug.Print 1 +", "#End If", "End Sub");
        var fileSystem = Files(("Dead.bas", dead), ("Cases.bas", Cases), ("Tally.cls", Tally));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 1, Filter: "count"));

        Assert.IsEmpty(report.Problems, string.Join("; ", report.Problems));
        Assert.IsNull(report.Cases.Single().Error, report.Cases.Single().Error);
    }

    [TestMethod]
    public async Task TheSweep_IsWaitedFor_AndIsTimedFromTheFirstModuleLoaded()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(ListOnly: true));

        Assert.IsTrue(report.Parse > TimeSpan.Zero && report.Symbols > TimeSpan.Zero && report.Load > TimeSpan.Zero);
        Assert.IsTrue(report.Sweep >= report.Load, "the sweep starts as modules load, and is over after they all are");
    }

    // ---- the command ----

    private static (BenchCommand Command, StringWriter Output, IConsoleMessageWriter Writer) Command(MockFileSystem fileSystem)
    {
        var output = new StringWriter();
        var writer = Substitute.For<IConsoleMessageWriter>();
        return (new BenchCommand(writer, fileSystem, new ProjectFileLoader(fileSystem), Runner(fileSystem), output), output, writer);
    }

    [TestMethod]
    public async Task TheReport_IsThePhasesThenACaseALine()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var (command, output, _) = Command(fileSystem);

        var exit = await command.ExecuteAsync([Root, "--runs", "2", "--filter", "BenchCount"], CancellationToken.None);

        Assert.AreEqual(0, exit);
        var text = output.ToString();
        foreach (var phase in new[] { "parse", "symbols", "load", "sweep" })
        {
            StringAssert.Contains(text, phase);
        }

        var line = text.Split(Environment.NewLine).Single(candidate => candidate.StartsWith("Cases.BenchCount", StringComparison.Ordinal));
        // the shape the bench job reads (.github/scripts/bench-figures.ps1): runs, fastest, median, then what the fastest run allocated and its collections
        StringAssert.Matches(line, new System.Text.RegularExpressions.Regex(@"^Cases\.BenchCount\s+2\s+[\d.]+ ms\s+[\d.]+ ms\s+\d+ KB\s+\d+/\d+/\d+$"));

        // the cases are warmed up by default, and the report says how
        var warmUp = text.Split(Environment.NewLine).Single(candidate => candidate.StartsWith("warm-up", StringComparison.Ordinal));
        StringAssert.EndsWith(warmUp, $" runs: {BenchWarmUp.UntilTheJitIsQuiet.Describe()}");
    }

    [TestMethod]
    public async Task NoWarmUp_TimesEachCaseFromItsFirstRun_AndTheReportSaysSo()
    {
        var fileSystem = Files(("Counted.bas", Counted));
        var (command, output, _) = Command(fileSystem);

        // the case raises on its third call: with no warm-up, its first two runs are timed and the third fails.
        var exit = await command.ExecuteAsync([Root, "--runs", "3", "--no-warm-up"], CancellationToken.None);

        Assert.AreEqual(1, exit);
        var lines = output.ToString().Split(Environment.NewLine);
        StringAssert.Matches(lines.Single(candidate => candidate.StartsWith("warm-up", StringComparison.Ordinal)),
            new System.Text.RegularExpressions.Regex(@"^warm-up\s+none$"));
        StringAssert.Matches(lines.Single(candidate => candidate.StartsWith("Counted.BenchFailsOnItsThirdCall", StringComparison.Ordinal)),
            new System.Text.RegularExpressions.Regex(@"^Counted\.BenchFailsOnItsThirdCall\s+2\s+VBA00006 "));
    }

    [TestMethod]
    public async Task ACaseThatFails_IsReportedInItsLine_AndTheCommandSaysSoByItsExitCode()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var (command, output, _) = Command(fileSystem);

        Assert.AreEqual(1, await command.ExecuteAsync([Root, "--filter", "fails"], CancellationToken.None));
        StringAssert.Contains(output.ToString(), "Cases.BenchFails");
    }

    [TestMethod]
    public async Task APathThatIsNothing_IsAnError()
    {
        var (command, _, writer) = Command(Files());
        Assert.AreEqual(1, await command.ExecuteAsync([Path.Combine(Root, "nothing")], CancellationToken.None));
        writer.ReceivedWithAnyArgs().WriteMessage(default!);
    }

    // formatting is timed instead of the cases: a line for each module, with its lines and its edits, then the totals.
    [TestMethod]
    public async Task Formatting_IsAModuleALine_ThenTheTotals()
    {
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var (command, output, _) = Command(fileSystem);

        Assert.AreEqual(0, await command.ExecuteAsync([Root, "--format", "--runs", "1"], CancellationToken.None));

        var lines = output.ToString().Split(Environment.NewLine);
        Assert.AreEqual(1, lines.Count(line => line.StartsWith("Cases.bas", StringComparison.Ordinal)));
        Assert.AreEqual(1, lines.Count(line => line.StartsWith("Tally.cls", StringComparison.Ordinal)));
        foreach (var total in new[] { "total", "every alignment on", "cold, every alignment on" })
        {
            Assert.IsTrue(lines.Any(line => line.StartsWith(total + " ", StringComparison.Ordinal)), total);
        }

        Assert.IsFalse(lines.Any(line => line.Contains("Cases.BenchCount", StringComparison.Ordinal)), "no case is run");
    }

    [TestMethod]
    public async Task RunsThatAreNotPositive_AreRefused()
        => Assert.AreEqual(2, await Command(Files()).Command.ExecuteAsync([Root, "--runs", "0"], CancellationToken.None));

    // while the session runs, its current directory is the corpus folder, and the process's stays where it was: the test runs alone, so that a test that moves
    // the process's directory cannot make it fail.
    [TestMethod]
    [DoNotParallelize]
    public async Task TheBuiltInCorpus_LoadsWithoutAProblem_AndHasTheCasesThePlanNames()
    {
        // the corpus is extracted to the real file system, as rdc does it.
        var fileSystem = new System.IO.Abstractions.FileSystem();
        var output = new StringWriter();
        var command = new BenchCommand(Substitute.For<IConsoleMessageWriter>(), fileSystem, new ProjectFileLoader(fileSystem),
            new BenchRunner(RuntimeEnvironmentProfile.Default, fileSystem, Substitute.For<IErrorDetailsRecorder>(), NullLoggerFactory.Instance,
                new RDCore.SDK.Runtime.Libraries.DirectoryLibrarySource(fileSystem, string.Empty)), output);
        var saved = Directory.GetCurrentDirectory();
        var corpus = Path.Combine(Path.GetTempPath(), "RDCore", "bench", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            Assert.AreEqual(0, await command.ExecuteAsync(["--list"], CancellationToken.None), output.ToString());

            // the run's session leaves the process's directory where it is, so Windows lets the command delete the corpus.
            Assert.AreEqual(saved, Directory.GetCurrentDirectory());
            Assert.IsFalse(Directory.Exists(corpus), corpus);
        }
        finally
        {
            Directory.SetCurrentDirectory(saved);
        }

        var text = output.ToString();
        foreach (var expected in new[]
        {
            "Numeric.BenchLongCounterDoubleSum", "Numeric.BenchDoubleSum", "Numeric.BenchLongArithmetic", "Numeric.BenchVariantSum",
            "Strings.BenchStringAssignment", "Strings.BenchStringConcatenation", "Arrays.BenchArrayFillAndSum",
            "Calls.BenchProcedureCalls", "Calls.BenchClassMemberCalls", "Calls.BenchStandardLibraryCalls",
            "Calls.BenchFactoryCalls", "Calls.BenchDefaultInstanceCalls", "Calls.BenchSiblingCalls",
            "Calls.BenchRecursion", "Strings.BenchStringBuilding", "Strings.BenchStringArguments", "Statements.BenchOnErrorResumeNext",
            "Numeric.BenchVariantTypedMix", "StdLib.BenchCStr", "StdLib.BenchLen", "StdLib.BenchFormat", "StdLib.BenchDateAdd",
        })
        {
            StringAssert.Contains(text, expected);
        }
    }

    // ---- rdc compilation ----

    [TestMethod]
    public async Task TheCompilationReport_SaysOfEachProcedureWhetherItIsCompiled_AndWhatKeepsItInTheInterpreter()
    {
        CompiledExecutionTests.RequirePayloads();
        var fileSystem = Files(("Cases.bas", Cases), ("Tally.cls", Tally));
        var output = new StringWriter();
        var command = new CompilationCommand(Substitute.For<IConsoleMessageWriter>(), fileSystem, new ProjectFileLoader(fileSystem), Runner(fileSystem), output);

        Assert.AreEqual(0, await command.ExecuteAsync([Root], CancellationToken.None), output.ToString());

        var lines = output.ToString().Split(Environment.NewLine);
        StringAssert.StartsWith(lines.Single(line => line.Contains("Cases.BenchCount ", StringComparison.Ordinal)), "compiled");
        // a call statement compiled code does not make is a support call the interpreter runs, as the Raise is.
        StringAssert.StartsWith(lines.Single(line => line.Contains("Cases.BenchFails ", StringComparison.Ordinal)), "compiled");

        // a member of an object is called by compiled code, and so is the Set that gives the variable an object made with New: the interpreter runs nothing
        // of the procedure, and the report says nothing is run by it.
        var objectCall = lines.Single(line => line.Contains("Cases.BenchCallsAnObject ", StringComparison.Ordinal));
        StringAssert.StartsWith(objectCall, "compiled");
        Assert.IsFalse(objectCall.Contains("run by the interpreter", StringComparison.Ordinal), objectCall);

        // a member of a class is compiled as any procedure is, and the field of Me its one statement assigns is read and written where the object holds it.
        StringAssert.StartsWith(lines.Single(line => line.Contains("Tally.Add ", StringComparison.Ordinal)), "compiled");

        // a compiled procedure says what of it the interpreter runs, and where: the Debug.Print, which compiled code does not do (rd-vbal.3.5.6), counted
        // from 1 in the file. The call of a member of an Object, bound late, is a support call: it is not stepped, and the report says so after what is.
        var stepping = lines.Single(line => line.Contains("Cases.CallsLateBoundInALoop ", StringComparison.Ordinal));
        StringAssert.StartsWith(stepping, "compiled");
        var handed = stepping.Split(" | ");
        Assert.HasCount(2, handed, stepping);
        StringAssert.Contains(handed[0], "run by the interpreter: ");
        var printed = Array.IndexOf(Cases.Split("\r\n"), "        Debug.Print T") + 1;
        StringAssert.Contains(handed[0], $"(line {printed})",
            $"the Debug.Print at line {printed} is no longer stepped: the case needs a statement compiled code does not do; give it another.");
        var called = Array.IndexOf(Cases.Split("\r\n"), "        O.Add") + 1;
        Assert.IsFalse(handed[0].Contains($"(line {called})", StringComparison.Ordinal), stepping);
        StringAssert.StartsWith(handed[1], "run by the interpreter as a support call: ");
        StringAssert.Contains(handed[1], $"(line {called})");
        StringAssert.Matches(output.ToString(), new System.Text.RegularExpressions.Regex(@"\d+ of \d+ procedures compiled\."));
    }

    // a call of a member of an Object, bound late, is a statement the interpreter runs for compiled code: the report says so of each, and where, and steps
    // nothing. The Set of an object made with New is compiled code's, and the report says nothing of it.
    [TestMethod]
    public async Task TheCompilationReport_SaysWhichStatementsAreSupportCalls_AndWhere()
    {
        CompiledExecutionTests.RequirePayloads();
        string[] calling =
        [
            "Public Sub Calls()",
            "    Dim G As Gadget, O As Object, I As Long",
            "    For I = 1 To 3",
            "        Set G = New Gadget",
            "        Set O = G",
            "        O.Grow I",
            "        O.Grow",
            "    Next",
            "End Sub",
        ];
        var fileSystem = Files(("Calling.bas", Module("Calling", calling)), ("Gadget.cls", ClassModule("Gadget",
            "Option Explicit",
            "Private mSize As Long",
            "Public Sub Grow(Optional ByVal By As Long = 1)",
            "    mSize = mSize + By",
            "End Sub")));
        var output = new StringWriter();
        var command = new CompilationCommand(Substitute.For<IConsoleMessageWriter>(), fileSystem, new ProjectFileLoader(fileSystem), Runner(fileSystem), output);
        Assert.AreEqual(0, await command.ExecuteAsync([Root], CancellationToken.None), output.ToString());

        var calls = output.ToString().Split(Environment.NewLine).Single(line => line.Contains("Calling.Calls ", StringComparison.Ordinal));
        StringAssert.StartsWith(calls, "compiled");
        Assert.IsFalse(calls.Contains("run by the interpreter: ", StringComparison.Ordinal), calls);
        StringAssert.Contains(calls, "run by the interpreter as a support call: ");
        var source = Module("Calling", calling).Split("\r\n");
        foreach (var statement in new[] { "        O.Grow I", "        O.Grow" })
        {
            StringAssert.Contains(calls, $"(line {Array.IndexOf(source, statement) + 1})", statement);
        }

        Assert.IsFalse(calls.Contains($"(line {Array.IndexOf(source, "        Set G = New Gadget") + 1})", StringComparison.Ordinal), calls);
    }

    // a reason names the code it is about as the module writes it: a Debug.Print, a ReDim and an assignment of an element of an array of the module, each
    // by the line it is on, and none by what the parser or the instructions call it. No reason is said twice of a procedure.
    [TestMethod]
    public async Task TheCompilationReport_QuotesTheCodeEachReasonIsAbout()
    {
        CompiledExecutionTests.RequirePayloads();
        string[] numbers =
        [
            "Private Values() As Long",
            "Public Sub PrintAll(ByVal Items As Collection)",
            "    Dim Item As Variant",
            "    For Each Item In Items",
            "        Debug.Print Item",
            "    Next",
            "End Sub",
            "Public Function Squares(ByVal N As Long) As Long()",
            "    Dim Index As Long",
            "    ReDim Values(1 To N)",
            "    For Index = 1 To N",
            "        Values(Index) = Index * Index",
            "    Next",
            "    Squares = Values",
            "End Function",
        ];
        var fileSystem = Files(("Numbers.bas", Module("Numbers", numbers)));
        var output = new StringWriter();
        var command = new CompilationCommand(Substitute.For<IConsoleMessageWriter>(), fileSystem, new ProjectFileLoader(fileSystem), Runner(fileSystem),
            output);
        Assert.AreEqual(0, await command.ExecuteAsync([Root], CancellationToken.None), output.ToString());

        var report = output.ToString();
        foreach (var internalName in new[] { "ForEachOpener", "ForEachNext", "DebugPrint", "RedimDeclaration", "IndexExpressionNode", "Node" })
        {
            Assert.IsFalse(report.Contains(internalName, StringComparison.Ordinal), $"{internalName}: {report}");
        }

        var source = Module("Numbers", numbers).Split("\r\n");
        var quoted = System.Text.RegularExpressions.Regex.Matches(report, @": ([^:;|]+?) \(line (\d+)\)")
            .Select(match => (Code: match.Groups[1].Value, Line: int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
        Assert.IsNotEmpty(quoted, report);
        foreach (var (code, line) in quoted)
        {
            Assert.AreEqual(source[line - 1].Trim(), code, report);
        }

        foreach (var line in report.Split(Environment.NewLine))
        {
            var reasons = line.Split(" | ").SelectMany(handed => handed.Split("; ")).ToArray();
            Assert.HasCount(reasons.Length, reasons.Distinct(), line);
        }
    }

    // an Enum, a Type, a Const and a field are members of a module that are no procedure: the report says nothing of them - of an Enum a Property Get is
    // named as either, as an interface's PlayerType is. A Property Get and its Let are two.
    [TestMethod]
    public async Task TheCompilationReport_IsOfTheSubFunctionAndPropertyProcedures_Only()
    {
        CompiledExecutionTests.RequirePayloads();
        var fileSystem = Files(("Player.cls", ClassModule("Player",
            "Attribute VB_Exposed = True",
            "Option Explicit",
            "Public Enum PlayerType",
            "    HumanControlled",
            "    ComputerControlled",
            "End Enum",
            "Private Type State",
            "    Kind As PlayerType",
            "End Type",
            "Private Const Limit As Long = 3",
            "Private this As State",
            "Private mCount As Long",
            "Public Property Get PlayerType() As PlayerType",
            "End Property",
            "Public Property Get Count() As Long",
            "    Count = mCount",
            "End Property",
            "Public Property Let Count(ByVal Value As Long)",
            "    mCount = Value",
            "End Property",
            "Public Function Twice(ByVal N As Long) As Long",
            "    Twice = N * Limit",
            "End Function")));
        var output = new StringWriter();
        var command = new CompilationCommand(Substitute.For<IConsoleMessageWriter>(), fileSystem, new ProjectFileLoader(fileSystem), Runner(fileSystem), output);
        Assert.AreEqual(0, await command.ExecuteAsync([Root], CancellationToken.None), output.ToString());

        var listed = output.ToString().Split(Environment.NewLine).Where(line => line.Contains("Player.", StringComparison.Ordinal)).ToArray();
        Assert.HasCount(4, listed, output.ToString());
        Assert.HasCount(1, listed.Where(line => line.Contains("Player.PlayerType ", StringComparison.Ordinal)), output.ToString());
        Assert.HasCount(2, listed.Where(line => line.Contains("Player.Count ", StringComparison.Ordinal)), output.ToString());
        Assert.HasCount(1, listed.Where(line => line.Contains("Player.Twice ", StringComparison.Ordinal)), output.ToString());
        StringAssert.Contains(output.ToString(), " of 4 procedures compiled.");
    }

    [TestMethod]
    public void EveryReasonAProcedureIsNotCompiled_IsWordedInEnglishAndInFrench()
    {
        foreach (var kind in Enum.GetValues<RDCore.Runtime.Compiled.IneligibilityKind>())
        {
            foreach (var culture in new[] { "en", "fr" })
            {
                var wording = RDCore.CLI.Resources.ResourceManager.GetString($"Compilation_Reason_{kind}", System.Globalization.CultureInfo.GetCultureInfo(culture));
                Assert.IsFalse(string.IsNullOrWhiteSpace(wording), $"{kind} ({culture})");
            }
        }
    }
}
