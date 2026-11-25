using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host.Handlers;
using RDCore.External;
using RDCore.External.Interaction;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.Runtime.Support.Host.Handlers;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Platform;
using System.IO.Abstractions;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// One program, run in several sessions of one process at once: what each session holds is its own. Every session writes its state, then waits at a
/// gate (<c>DoEvents</c>) until all of them have; only then does any read its state back. A session reads what it wrote, and what it would have read
/// running alone, whatever the others wrote meanwhile: its module variables, <c>Err</c>, <c>Rnd</c>, its open files and its current directory, and its
/// objects and their <c>Class_Terminate</c>.
/// </summary>
/// <remarks>
/// The gate is the only synchronization: no test here waits for a time to pass. Its timeout only keeps a session that never reaches it from hanging the
/// others.
/// </remarks>
[TestClass]
[TestCategory(TestCategories.Wide)]
public sealed class ParallelSessionsTests
{
    private const int Sessions = 8;
    private const string ModuleName = "Program";

    private static readonly TimeSpan GateTimeout = TimeSpan.FromMinutes(2);

    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        // under the temporary folder's real path: on macOS /var is a link to /private/var, and the process's directory, which a session starts in when its
        // root is not a workspace's, is a real path.
        var temporary = RDCore.SDK.Platform.PlatformRootConfinement.RealPath(Path.GetTempPath());
        _root = WorkspacePaths.Normalized(Path.Combine(temporary, "rdcore-parallel-ws-" + Guid.NewGuid().ToString("N")));
        for (var session = 1; session <= Sessions; session++)
        {
            Directory.CreateDirectory(Path.Combine(_root, $"S{session}"));
        }
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a temporary folder.
        }
    }

    // each session moves to a folder of its own, and writes and opens a file there by a relative name; with one current directory for the process, a
    // session could write or open another session's file, and CurDir after the gate would give the folder of the last one to move.
    private static readonly string FilesProgram = string.Join("\r\n",
        $"Attribute VB_Name = \"{ModuleName}\"",
        "Private Mine As String",
        "Public Sub Main()",
        "Dim got As String, first As Single, n As Integer",
        "Mine = Command()",
        "ChDir \"S\" & Mine",
        "n = FreeFile",
        "Open \"out.txt\" For Output As #n",
        "Print #n, \"written by \" & Mine",
        "Close #n",
        "n = FreeFile",
        "Open \"out.txt\" For Input As #n",
        "first = Rnd(-1)",
        "Randomize CInt(Mine)",
        "first = Rnd",
        "On Error Resume Next",
        "Err.Raise 1000 + CInt(Mine)",
        "DoEvents",
        "Debug.Print Mine",
        "Debug.Print Err.Number",
        "Debug.Print CurDir",
        "Line Input #n, got",
        "Close #n",
        "Debug.Print got",
        "Debug.Print first & \" \" & Rnd",
        "End Sub",
        "");

    [TestMethod]
    public async Task SessionsOfOneProgram_RunAtOnce_EachReadsTheStateItWrote()
    {
        var alone = new string[Sessions][];
        for (var session = 1; session <= Sessions; session++)
        {
            alone[session - 1] = await RunFilesProgramAsync(session, new Barrier(1));
        }

        var gate = new Barrier(Sessions);
        var together = await Task.WhenAll(Enumerable.Range(1, Sessions).Select(session => Task.Run(() => RunFilesProgramAsync(session, gate))));

        for (var session = 1; session <= Sessions; session++)
        {
            string[] own = [$"{session}", $"{1000 + session}", Path.Combine(_root, $"S{session}"), $"written by {session}"];
            CollectionAssert.AreEqual(own, alone[session - 1][..4], $"alone: {string.Join("|", alone[session - 1])}");
            CollectionAssert.AreEqual(alone[session - 1], together[session - 1], $"session {session}: {string.Join("|", together[session - 1])}");
        }

        Assert.AreEqual(Sessions, alone.Select(lines => lines[4]).Distinct().Count(), "each session's seed gives numbers of its own");
    }

    [TestMethod]
    public async Task SessionsOfOneProgram_RunAtOnce_EachTerminatesItsOwnObjects()
    {
        string[] tracked =
        [
            "Public Id As String",
            "Private Sub Class_Terminate()",
            "Debug.Print \"terminated \" & Id",
            "End Sub",
        ];
        var program = StandardModule(ModuleName,
            "Private Held As Tracked",
            "Public Sub Main()",
            "Set Held = New Tracked",
            "Held.Id = Command()",
            "DoEvents",
            "Debug.Print \"held \" & Held.Id",
            "Set Held = Nothing",
            "Debug.Print \"done \" & Command()",
            "End Sub");

        var gate = new Barrier(Sessions);
        var together = await Task.WhenAll(Enumerable.Range(1, Sessions).Select(session => Task.Run(()
            => RunAsync([("Tracked", ClassModule("Tracked", tracked))], program,
                new WorkspaceLibraries([], new RDCore.Tests.Runtime.Libraries.InMemoryLibrarySource(), Outside: WorldOf(session, gate))))));

        for (var session = 1; session <= Sessions; session++)
        {
            CollectionAssert.AreEqual(new[] { $"held {session}", $"terminated {session}", $"done {session}" }, together[session - 1],
                $"session {session}: {string.Join("|", together[session - 1])}");
        }
    }

    // what a session reaches outside the platform: its command line, and a desktop whose DoEvents is the gate every session waits at.
    private static ExternalWorld WorldOf(int session, Barrier gate)
    {
        var desktop = Substitute.For<IDesktop>();
        desktop.When(d => d.DoEvents()).Do(_ => Assert.IsTrue(gate.SignalAndWait(GateTimeout), $"session {session}: not every session reached the gate"));
        return ExternalWorld.None with { Desktop = desktop, CommandLine = $"{session}" };
    }

    private Task<string[]> RunFilesProgramAsync(int session, Barrier gate)
    {
        var profile = new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false,
            ExecutionEngine: RDCore.SDK.Server.Configuration.SdkEnvironmentOptions.DefaultExecutionEngine);
        var provider = new EnvironmentSessionProvider(
            profile, new FileSystem(), NullLogger<EnvironmentSessionProvider>.Instance, outside: WorldOf(session, gate));
        provider.Compose(new ProjectFile(_root, new RDCoreProject { Name = "Project1", Modules = [new RDCoreModule { RelativeUri = $"{ModuleName}.bas" }] })
            .ProjectInfo, Wire.FileUri(_root));
        return RunInSessionAsync(provider, _root, FilesProgram);
    }

    // Program.Main of the source given, defined in the session and run, as the language server has the host do it; the session is let go of after.
    private static async Task<string[]> RunInSessionAsync(EnvironmentSessionProvider provider, string root, string source)
    {
        using var _ = provider;
        var workspaceRoot = Wire.FileUri(root);
        var moduleUri = new UriBuilder(workspaceRoot) { Fragment = ModuleName }.Uri;
        var parse = new ModuleParser().Parse(Wire.FileUri(Path.Combine(root, $"{ModuleName}.bas")), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var resolver = WorkspaceSymbolResolver.Compose(workspaceRoot, [(moduleUri, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
        var symbols = new SyntaxTreeSymbolProvider(workspaceRoot, moduleUri, ModuleType.StdModule, parse, resolver, withImplicitDeclarations: true)
            .ProvideSymbols();
        await new DefineSymbolsHandler(provider, Substitute.For<IProjectAnalysisSessions>(), Substitute.For<IErrorDetailsRecorder>(),
                NullLogger<DefineSymbolsHandler>.Instance)
            .Handle(Wire.RoundTrip(new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = ModuleName,
                Symbols = SymbolDescriptorProjector.Project(symbols, moduleUri),
                Directives = parse.SyntaxTree.GetModuleDirectives(),
                PrecompilerTriviaJson = DefineSymbolsParams.PrecompilerTriviaJsonOf(parse),
                Replace = true,
            }), CancellationToken.None);

        var result = await new HostExecuteHandler(provider, Substitute.For<IErrorDetailsRecorder>(), NullLogger<HostExecuteHandler>.Instance)
            .Handle(new HostExecuteParams
            {
                Json = PlatformJson.Serialize(new HostExecutePayload(moduleUri, parse)),
                ModuleName = ModuleName,
                EntryPoint = "Main",
            }, CancellationToken.None);

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        return [.. result.Output.Select(line => line.Trim())];
    }
}
