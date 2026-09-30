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
        var composed = Compose(source);
        return await ExecuteAsync(composed, source, entryPoint, token);
    }

    /// <summary>
    /// Runs one module against an already-composed session, so a test can run more than once against
    /// the same one — which is what a live client does, and the only way a redefinition is exercised.
    /// </summary>
    private static async Task<ExecuteSessionResult> ExecuteAsync(
        (HostExecuteHandler Handler, EnvironmentSessionProvider Session, Uri WorkspaceRoot, Uri ModuleUri) composed,
        string source, string entryPoint = "Main", CancellationToken token = default)
    {
        var (handler, sessionProvider, workspaceRoot, moduleUri) = composed;

        var parse = new ModuleParser().Parse(new Uri(Path.Combine(Root, $"{ModuleName}.bas")), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        // the same resolver the language server composes, so the test sees what the platform sees -
        // including the standard library and the environment's own globals.
        var workspaceResolver = WorkspaceSymbolResolver.Compose(
            workspaceRoot, [(moduleUri, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver());
        var symbols = new SyntaxTreeSymbolProvider(
            workspaceRoot, moduleUri, ModuleType.StdModule, parse, workspaceResolver).ProvideSymbols();

        var defined = await new DefineSymbolsHandler(sessionProvider, NullLogger<DefineSymbolsHandler>.Instance)
            .Handle(new DefineSymbolsParams
            {
                WorkspaceRoot = workspaceRoot,
                ModuleUri = moduleUri,
                ModuleName = ModuleName,
                Symbols = SymbolDescriptorProjector.Project(symbols, moduleUri),
                Replace = true,
            }, CancellationToken.None);
        Assert.IsGreaterThan(0, defined.Defined + defined.Replaced, "no symbols were defined in the session");

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
    public async Task AnUndeclaredLocal_IsImplicitlyDeclared_AndRuns()
    {
        // MS-VBAL 5.6.10, all the way through: the declaration pass declares Counter because something
        // referred to it, the descriptor carries it to the host, and the activation allocates frame
        // storage for it — so a program that never says Dim runs, which is how BASIC is written.
        var result = await ExecuteAsync(Module(
            "10 Counter = 21",
            "20 Debug.Print Counter * 2"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { " 42 " }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task ASecondRunAgainstTheSameSession_RedefinesTheModuleAndRuns()
    {
        // what a live client does every time: the module is defined again, then run again. The second
        // definition replaces the first, and both runs have to work.
        var composed = Compose(Module("10 Counter = 1", "20 Debug.Print Counter"));

        var first = await ExecuteAsync(composed, Module("10 Counter = 1", "20 Debug.Print Counter"));
        Assert.AreEqual(ExecutionOutcome.Completed, first.Outcome, first.ErrorMessage);

        // the shell's own shape: a second module with an extra procedure, run by that procedure's name.
        var second = await ExecuteAsync(
            composed,
            $"{Module("10 Counter = 1", "20 Debug.Print Counter")}\r\nPublic Sub Immediate()\r\nDebug.Print 1 + 1\r\nEnd Sub\r\n",
            entryPoint: "Immediate");

        Assert.AreEqual(ExecutionOutcome.Completed, second.Outcome, second.ErrorMessage);
        CollectionAssert.AreEqual(new[] { " 2 " }, second.Output.ToArray());
    }

    [TestMethod]
    public async Task AnImplicitLocalThatIsOnlyEverRead_IsEmpty()
    {
        // an implicit declaration is a declaration with no initializer, so its value is its type's
        // default — a Variant's Empty, which prints as nothing at all.
        var result = await ExecuteAsync(Module("10 Debug.Print Untouched"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "" }, result.Output.ToArray());
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
    public async Task ADeadIfBranch_DoesNotRun()
    {
        // MS-VBAL 3.4.2: an excluded #If branch is logically removed before the rest of the language
        // sees it. PrecompilerLiveBranchEvaluator has computed the dead ranges correctly since #315 but
        // had no production caller, so lowering kept both branches and ran them in source order.
        var result = await ExecuteAsync(Module(
            "#If False Then",
            "Debug.Print \"dead\"",
            "#Else",
            "Debug.Print \"live\"",
            "#End If"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "live" }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AnAssignmentInADeadIfBranch_DoesNotWin()
    {
        // the dead branch ran *after* the live one, so its assignment was the value that survived.
        var result = await ExecuteAsync(Module(
            "#If True Then",
            "x = 1",
            "#Else",
            "x = 99",
            "#End If",
            "Debug.Print \"x=\" & x"));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "x=1" }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AFaultingStatementInADeadIfBranch_DoesNotRaise()
    {
        // the sharpest form of the same defect: code that never compiled raised error 11 at run time.
        var result = await ExecuteAsync(Module(
            "#If False Then",
            "x = 1 / 0",
            "#End If",
            "Debug.Print \"survived\""));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "survived" }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AnOmittedOptionalArgument_GetsItsDeclaredDefault_NotTheTypeDefault()
    {
        // MS-VBAL 5.3.1.5: a `default-value` clause specifies the parameter's default value, and only a
        // parameter declaring none falls back to its declared type's. The value was dropped at the very
        // first stage — SymbolBuilder built the parameter symbol without it — so `Optional k As Long = 5`
        // arrived as 0 and `Optional t As String = "abc"` as "", all the way across the wire.
        var result = await ExecuteAsync(
            $"Attribute VB_Name = \"{ModuleName}\"\r\n"
            + "Public Sub Main()\r\n"
            + "Foo\r\n"
            + "Call Foo(7)\r\n"
            + "Bar\r\n"
            + "End Sub\r\n"
            + "Private Sub Foo(Optional ByVal k As Long = 5)\r\n"
            + "Debug.Print \"k=\" & k\r\n"
            + "End Sub\r\n"
            + "Private Sub Bar(Optional ByVal t As String = \"abc\")\r\n"
            + "Debug.Print \"t=\" & t\r\n"
            + "End Sub\r\n");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "k=5", "k=7", "t=abc" }, result.Output.ToArray());
    }

    [TestMethod]
    public async Task AnOmittedOptionalArgumentWithNoDeclaredDefault_GetsItsTypeDefault()
    {
        // the other half of the same MS-VBAL 5.3.1.5 rule, so carrying a declared default across cannot
        // quietly become "carry something either way".
        var result = await ExecuteAsync(
            $"Attribute VB_Name = \"{ModuleName}\"\r\n"
            + "Public Sub Main()\r\n"
            + "Foo\r\n"
            + "End Sub\r\n"
            + "Private Sub Foo(Optional ByVal k As Long)\r\n"
            + "Debug.Print \"k=\" & k\r\n"
            + "End Sub\r\n");

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "k=0" }, result.Output.ToArray());
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
