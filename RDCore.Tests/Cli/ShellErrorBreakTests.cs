using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Repl;
using RDCore.CLI.Host.Handlers;
using RDCore.Parsing;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The shell runs its program, and every line typed at the prompt, waiting at the errors nothing handles (<see cref="ErrorBreakMode.Unhandled"/>), as the host
/// runs them: the program waits at the statement that raised the error, in the shell's own program or in a module of the workspace, and goes on from there or is
/// ended. An error the program handles is not waited at.
/// </summary>
[TestClass]
public sealed class ShellErrorBreakTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static readonly (int Number, string Statement)[] Divides = [(10, "D = 0"), (20, "N = 1 / D"), (30, "PRINT N")];

    // a host whose session waits at the errors nothing handles, as the shell says it before it runs anything.
    private static async Task<ShellHost> ComposeAsync()
    {
        var shell = ShellHost.Compose();
        _ = await new HostDebugErrorBreakHandler(shell.Provider).Handle(new HostDebugErrorBreakParams { Mode = ErrorBreakMode.Unhandled }, CancellationToken.None);
        return shell;
    }

    private static ReplProgram Program(IEnumerable<(int Number, string Statement)> lines)
    {
        var program = new ReplProgram();
        foreach (var (number, statement) in lines)
        {
            program.Store(number, statement);
        }

        return program;
    }

    private static Task<ExecuteResult> ResumeAsync(EnvironmentSessionProvider provider)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance).Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

    // a statement typed while the program waits, as the shell sends it: run in the activation the program waits in.
    private static Task<DebugEvaluateResult> TypeAsync(EnvironmentSessionProvider provider, string statement)
        => new HostDebugExecuteHandler(provider, Substitute.For<IErrorDetailsRecorder>())
            .Handle(new HostDebugExecuteParams { FrameId = 0, Json = PlatformJson.Serialize(Statement(statement)) }, CancellationToken.None).WaitAsync(Patience);

    private static SyntaxNode Statement(string text)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Execute.bas"), $"Public Sub E()\r\n{text}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children.OfType<StatementNode>().Single();
    }

    [TestMethod]
    public async Task AnErrorTheProgramRaises_WaitsAtTheStatementThatRaisedIt()
    {
        var shell = await ComposeAsync();

        var stopped = await shell.RunAsync(Divides, debug: true);

        Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
        Assert.AreEqual(11, stopped.ErrorNumber);
        Assert.AreEqual(20, Program(Divides).LineNumberAt(stopped.ErrorLine));
        Assert.AreEqual(ReplProgram.ModuleName, stopped.ErrorModule);
        Assert.AreEqual(ReplProgram.EntryPointName, shell.Provider.Execution.Stack().Frames[0].Procedure);
    }

    [TestMethod]
    public async Task AnErrorTheProgramRaises_WithoutADebugger_EndsTheRunInTheShellsModule()
    {
        var shell = ShellHost.Compose();

        var ended = await shell.RunAsync(Divides, debug: false);

        Assert.AreEqual(ExecutionOutcome.RuntimeError, ended.Outcome, ended.ErrorMessage);
        Assert.AreEqual(ReplProgram.ModuleName, ended.ErrorModule);
        Assert.AreEqual((ReplProgram.EntryPointName, ReplProgram.ModuleName), (ended.StackTrace[0].Procedure, ended.StackTrace[0].Module));
    }

    [TestMethod]
    public async Task AProgramThatWaitsAtAnError_GoesOn_OnceWhatRaisedItIsFixed()
    {
        var shell = await ComposeAsync();
        var stopped = await shell.RunAsync(Divides, debug: true);
        Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);

        var fixedIt = await TypeAsync(shell.Provider, "D = 2");
        var resumed = await ResumeAsync(shell.Provider);

        Assert.IsTrue(fixedIt.Success, fixedIt.Error);
        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "0.5" }, resumed.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task AProgramThatWaitsAtAnError_GoesOnWithoutAFix_RunsTheStatementAgain_AndWaitsThereAgain()
    {
        var shell = await ComposeAsync();
        var stopped = await shell.RunAsync(Divides, debug: true);

        var resumed = await ResumeAsync(shell.Provider);

        Assert.AreEqual((ExecutionOutcome.Suspended, 11), (resumed.Outcome, resumed.ErrorNumber), resumed.ErrorMessage);
        Assert.AreEqual(stopped.ErrorLine, resumed.ErrorLine);
    }

    [TestMethod]
    public async Task AProgramThatWaitsAtAnError_EndedByAnEnd_IsOver()
    {
        var shell = await ComposeAsync();
        var stopped = await shell.RunAsync(Divides, debug: true);
        Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);

        var ended = await TypeAsync(shell.Provider, "End");

        Assert.IsTrue(ended.EndedTheProgram);
        Assert.AreEqual(0, shell.Provider.Execution.Stack().Frames.Count);
    }

    // the error an End cleared is not the next one's: Err.Raise with a number and nothing else describes it by the number (MS-VBAL 6.1.3.2.1.2).
    [TestMethod]
    public async Task AnErrorRaisedAfterAnEnd_IsDescribedByItsOwnNumber_NotByTheErrorTheEndCleared()
    {
        var shell = await ComposeAsync();
        _ = await shell.RunAsync(Divides, debug: true);
        _ = await TypeAsync(shell.Provider, "End");

        var raised = await shell.RunAsync([(10, "ERR.RAISE 13")], debug: true);

        Assert.AreEqual((ExecutionOutcome.Suspended, 13), (raised.Outcome, raised.ErrorNumber), raised.ErrorMessage);
        StringAssert.Contains(raised.ErrorMessage, "Type mismatch");
    }

    [TestMethod]
    public async Task AnErrorTheProgramHandles_IsNotWaitedAt()
    {
        var shell = await ComposeAsync();

        var done = await shell.RunAsync([(10, "ON ERROR RESUME NEXT"), (20, "N = 1 / 0"), (30, "PRINT \"after\"")], debug: true);

        Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, done.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "after" }, done.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public async Task AnErrorAModuleOfTheWorkspaceRaises_WaitsInThatModule_AtTheStatementThatRaisedIt()
    {
        string[] divide = ["Public Sub Divide()", "    Dim X As Double", "    X = 1 / 0", "End Sub"];
        var other = ModuleWorkspace.StandardModule("Other", divide);
        var shell = new ReplProgram { AttachedToWorkspace = true };
        DebugStackFrame? innermost = null;

        var stopped = await ModuleWorkspace.RunSessionModuleAsync(
            [("Other", other)], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n", shell.Module, shell.ToImmediateModuleSource("Divide"), shell.ImmediateEntryPoint,
            debug: true, errorBreak: ErrorBreakMode.Unhandled, afterRun: async (_, provider) =>
            {
                innermost = (await new HostDebugStackHandler(provider).Handle(new HostDebugStackParams(), CancellationToken.None)).Frames.FirstOrDefault();
            });

        Assert.AreEqual((ExecutionOutcome.Suspended, 11), (stopped.Outcome, stopped.ErrorNumber), stopped.ErrorMessage);
        Assert.AreEqual("Other", stopped.ErrorModule);
        Assert.AreEqual(("Divide", "Other"), (innermost!.Procedure, innermost.Module));
        Assert.AreEqual(Array.FindIndex(other.Split("\r\n"), line => line.Trim() == "X = 1 / 0"), innermost.Line);
    }

    [TestMethod]
    public async Task AProgramThatWaitsAtAnErrorInAModuleOfTheWorkspace_EndedByAnEnd_IsOver()
    {
        var other = ModuleWorkspace.StandardModule("Other", "Public Sub Divide()", "    Dim X As Double", "    X = 1 / 0", "End Sub");
        var shell = new ReplProgram { AttachedToWorkspace = true };
        DebugEvaluateResult? ended = null;

        var stopped = await ModuleWorkspace.RunSessionModuleAsync(
            [("Other", other)], "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n", shell.Module, shell.ToImmediateModuleSource("Divide"), shell.ImmediateEntryPoint,
            debug: true, errorBreak: ErrorBreakMode.Unhandled, afterRun: async (_, provider) => ended = await TypeAsync(provider, "End"));

        Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
        Assert.IsTrue(ended!.EndedTheProgram, ended.Error);
    }
}
