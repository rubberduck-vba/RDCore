using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdcore/host/execute</c> end to end inside the environment host: the language server's own
/// symbol projection, then the host defining those symbols, lowering the module and running one
/// procedure of it — everything the platform does for a <c>RUN</c> except the two JSON-RPC hops.
/// </summary>
[TestClass]
public sealed class HostExecuteHandlerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-execute-ws");
    private const string ModuleName = "Program";

    private static (HostExecuteHandler Handler, EnvironmentSessionProvider Session, Uri WorkspaceRoot, Uri ModuleUri) Compose(string source)
    {
        var project = new ProjectFile(Root, new RDCoreProject
        {
            Name = ModuleName,
            Modules = [new RDCoreModule { RelativeUri = $"{ModuleName}.bas" }],
        });
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Root, ProjectFile.FileName)] = new(JsonSerializer.Serialize(project)),
            [Path.Combine(Root, $"{ModuleName}.bas")] = new(source),
        });

        var sessionProvider = new EnvironmentSessionProvider(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), fs,
            NullLogger<EnvironmentSessionProvider>.Instance);
        var workspaceRoot = new Uri(Root);
        sessionProvider.Compose(project.ProjectInfo, workspaceRoot);

        var moduleUri = new UriBuilder(workspaceRoot) { Fragment = ModuleName }.Uri;
        var handler = new HostExecuteHandler(sessionProvider, Substitute.For<IVerboseMessageBuilder>(),
            NullLogger<HostExecuteHandler>.Instance);

        return (handler, sessionProvider, workspaceRoot, moduleUri);
    }

    /// <summary>
    /// Runs <paramref name="entryPoint"/> of <paramref name="source"/> the way the platform does: the
    /// language server parses and projects the symbols, the host defines them and runs.
    /// </summary>
    private static async Task<ExecuteSessionResult> ExecuteAsync(string source, string entryPoint = "Main", CancellationToken token = default)
    {
        var (handler, sessionProvider, workspaceRoot, moduleUri) = Compose(source);

        var parse = new ModuleParser().Parse(new Uri(Path.Combine(Root, $"{ModuleName}.bas")), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var symbols = new SyntaxTreeSymbolProvider(
            workspaceRoot, moduleUri, ModuleType.StdModule, parse, new IntrinsicSymbolResolver()).ProvideSymbols();

        var defined = await new DefineSymbolsHandler(sessionProvider, NullLogger<DefineSymbolsHandler>.Instance)
            .Handle(new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = ModuleName,
                Symbols = SymbolDescriptorProjector.Project(symbols, moduleUri),
            }, CancellationToken.None);
        Assert.IsGreaterThan(0, defined.Defined, "no symbols were defined in the session");

        return await handler.Handle(new HostExecuteParams
        {
            Json = PlatformJson.Serialize(new HostExecutePayload(moduleUri, parse)),
            ModuleName = ModuleName,
            EntryPoint = entryPoint,
        }, token);
    }

    private static string Module(params string[] body)
        => $"Attribute VB_Name = \"{ModuleName}\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task APrintStatement_RunsAndItsOutputComesBack()
    {
        var result = await ExecuteAsync(Module("10 Debug.Print \"hello\""));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "hello" }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task ALocalVariable_IsAssignableAndReadable()
    {
        // the whole point of carrying a procedure's Dim variables to the host: an activation allocates
        // frame storage from them, so without them this cannot resolve its own target.
        var result = await ExecuteAsync(Module(
            "10 Dim Counter As Long",
            "20 Counter = 21",
            "30 Debug.Print Counter * 2"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { " 42 " }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AGoToALineNumber_BranchesToIt()
    {
        // a line number typed at the shell's prompt is an MS-VBAL line-number label, which is exactly
        // why GoTo 30 works without the shell knowing anything about it.
        var result = await ExecuteAsync(Module(
            "10 GoTo 30",
            "20 Debug.Print \"skipped\"",
            "30 Debug.Print \"landed\""));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "landed" }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AnEntryPointThatIsNotThere_IsReportedAsNotFound()
    {
        var result = await ExecuteAsync(Module("10 Debug.Print 1"), entryPoint: "Nowhere");

        Assert.AreEqual(ExecutionOutcome.NotFound, result.Outcome);
    }

    [TestMethod]
    public async Task ACancelledRun_IsReportedAsInterrupted_NotAsAFailure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var result = await ExecuteAsync(Module("10 Debug.Print 1"), token: cancelled.Token);

        Assert.AreEqual(ExecutionOutcome.Interrupted, result.Outcome);
    }

    [TestMethod]
    public async Task AnEndlessLoop_StopsWhenTheRunIsCancelled()
    {
        // the interpreter checks the token between instructions, so a program whose own control flow
        // never terminates still does. Cancelled on a timer, from outside, while it spins.
        using var cancelled = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await ExecuteAsync(Module("10 GoTo 10"), token: cancelled.Token);

        Assert.AreEqual(ExecutionOutcome.Interrupted, result.Outcome);
    }
}
