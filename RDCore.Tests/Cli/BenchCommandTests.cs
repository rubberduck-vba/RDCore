using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Bench;
using RDCore.CLI.App.Commands;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Runtime;
using RDCore.SDK.Services.VerboseMessages;
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

    private static BenchRunner Runner(MockFileSystem fileSystem)
        => new(RuntimeEnvironmentProfile.Default, fileSystem, Substitute.For<IVerboseMessageBuilder>(), NullLoggerFactory.Instance);

    // ---- the workspace ----

    [TestMethod]
    public async Task AFolder_IsEveryModuleUnderIt_NamedAndKindedAsTheModuleSays()
    {
        var workspace = await Load(Files(("Cases.bas", Cases), ("Model/Tally.cls", Tally), ("notes.txt", "not a module")));

        CollectionAssert.AreEqual(new[] { "Cases", "Tally" }, workspace.Modules.Select(module => module.Name).ToArray());
        CollectionAssert.AreEqual(new[] { ModuleType.StdModule, ModuleType.ClassModule }, workspace.Modules.Select(module => module.Type).ToArray());
        CollectionAssert.AreEqual(new[] { "Cases.bas", Path.Combine("Model", "Tally.cls") }, workspace.Project.Modules.Select(module => module.RelativeUri).ToArray());
        Assert.AreEqual(RDCoreReference.VBStandardLibrary.Name, workspace.Project.References[0].Name);
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

    [TestMethod]
    public async Task AModuleThatDoesNotParse_IsAProblem_AndTheOthersRunAllTheSame()
    {
        var fileSystem = Files(("Broken.bas", Module("Broken", "Public Sub BenchBroken(", "End Sub")), ("Cases.bas", Cases), ("Tally.cls", Tally));
        var report = await Runner(fileSystem).RunAsync(await Load(fileSystem), new BenchOptions(Runs: 1, Filter: "count"));

        StringAssert.StartsWith(report.Problems.Single(), "Broken:");
        Assert.AreEqual("Cases.BenchCount", report.Cases.Single().Name);
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
        StringAssert.Matches(line, new System.Text.RegularExpressions.Regex(@"^Cases\.BenchCount\s+2\s+[\d.]+ ms\s+[\d.]+ ms$"));
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

    [TestMethod]
    public async Task RunsThatAreNotPositive_AreRefused()
        => Assert.AreEqual(2, await Command(Files()).Command.ExecuteAsync([Root, "--runs", "0"], CancellationToken.None));

    [TestMethod]
    public async Task TheBuiltInCorpus_LoadsWithoutAProblem_AndHasTheCasesThePlanNames()
    {
        // the corpus is extracted to the real file system, as rdc does it.
        var fileSystem = new System.IO.Abstractions.FileSystem();
        var output = new StringWriter();
        var command = new BenchCommand(Substitute.For<IConsoleMessageWriter>(), fileSystem, new ProjectFileLoader(fileSystem),
            new BenchRunner(RuntimeEnvironmentProfile.Default, fileSystem, Substitute.For<IVerboseMessageBuilder>(), NullLoggerFactory.Instance), output);

        Assert.AreEqual(0, await command.ExecuteAsync(["--list"], CancellationToken.None), output.ToString());

        var text = output.ToString();
        foreach (var expected in new[]
        {
            "Numeric.BenchLongCounterDoubleSum", "Numeric.BenchDoubleSum", "Numeric.BenchLongArithmetic", "Numeric.BenchVariantSum",
            "Strings.BenchStringAssignment", "Strings.BenchStringConcatenation", "Arrays.BenchArrayFillAndSum",
            "Calls.BenchProcedureCalls", "Calls.BenchClassMemberCalls", "Calls.BenchStandardLibraryCalls",
            "Calls.BenchFactoryCalls", "Calls.BenchDefaultInstanceCalls", "Calls.BenchSiblingCalls",
        })
        {
            StringAssert.Contains(text, expected);
        }
    }
}
