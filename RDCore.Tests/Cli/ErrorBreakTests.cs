using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program under a debugger can wait where a run-time error is raised, before anything is unwound or any handler has caught it: all the errors, or only those that
/// nothing would catch.
/// </summary>
[TestClass]
public sealed class ErrorBreakTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // Main calls a procedure that faults at the program's seventh line, the sixth counted from 0.
    private static string Faulting(string statement) => Program(
        "Public Sub Main()",
        "    Helper",
        "    Debug.Print \"after\"",
        "End Sub",
        "Private Sub Helper()",
        "    Dim z As Long",
        "    " + statement,
        "    Debug.Print \"never\"",
        "End Sub");

    private static readonly string Unhandled = Faulting("Debug.Print 5 \\ z");

    private static readonly string Handled = Program(
        "Public Sub Main()",
        "    On Error GoTo Oops",
        "    Dim z As Long",
        "    z = 1 \\ 0",
        "    Debug.Print \"no\"",
        "    Exit Sub",
        "Oops:",
        "    Debug.Print \"caught\"",
        "End Sub");

    // runs the program once as it is, and then again with the mode said.
    private static Task RunAsync(string source, ErrorBreakMode mode, bool debug, Func<EnvironmentSessionProvider, ExecuteSessionResult, Task> test)
        => ModuleWorkspace.InspectAsync([], source, async (provider, first, again) =>
        {
            Assert.AreNotEqual(ExecutionOutcome.NotFound, first.Outcome, first.ErrorMessage);
            _ = await new HostDebugErrorBreakHandler(provider).Handle(new HostDebugErrorBreakParams { Mode = mode }, CancellationToken.None);
            await test(provider, await again());
        }, debug: debug);

    private static Task<ExecuteSessionResult> ResumeAsync(EnvironmentSessionProvider provider)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

    [TestMethod]
    public async Task AnUnhandledError_IsWaitedAt_WhereItWasRaised_WithTheCallsStillOnTheStack()
        => await RunAsync(Unhandled, ErrorBreakMode.Unhandled, debug: true, async (provider, stopped) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            Assert.AreEqual(11, stopped.ErrorNumber);
            StringAssert.Contains(stopped.ErrorMessage, "Division by zero");
            Assert.AreEqual(6, stopped.ErrorLine, "the statement that raised it");
            var stack = provider.Execution.Stack().Frames;
            CollectionAssert.AreEqual(new[] { "Helper", "Main" }, stack.Select(frame => frame.Procedure).ToArray());
            Assert.AreEqual(6, stack[0].Line);
            await Task.CompletedTask;
        });

    [TestMethod]
    public async Task AProgramThatWaitedAtAnError_GoesOnToDealWithIt_AndIsNotWaitedAtAgainAsItUnwinds()
        => await RunAsync(Unhandled, ErrorBreakMode.Unhandled, debug: true, async (provider, stopped) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome);

            var resumed = await ResumeAsync(provider);

            Assert.AreEqual(ExecutionOutcome.RuntimeError, resumed.Outcome, resumed.ErrorMessage);
            Assert.AreEqual(11, resumed.ErrorNumber);
        });

    [TestMethod]
    [DataRow("Debug.Print CInt(\"x\")", DisplayName = "a conversion")]
    [DataRow("Debug.Print Hex(\"x\")", DisplayName = "a conversion with a $ twin")]
    [DataRow("Debug.Print Chr(-1)", DisplayName = "a member the dispatcher answers for")]
    public async Task AnErrorALibraryMemberRaises_IsWaitedAt_TheStatementThatCalledIt(string statement)
        => await RunAsync(Faulting(statement), ErrorBreakMode.Unhandled, debug: true, (provider, stopped) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            Assert.AreEqual(6, stopped.ErrorLine, "the statement that called the member");
            var stack = provider.Execution.Stack().Frames;
            Assert.AreEqual("Helper", stack[0].Procedure);
            Assert.AreEqual(6, stack[0].Line);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task AProgramThatWaitedAtALibraryError_IsNotWaitedAtAgainAsItUnwinds()
        => await RunAsync(Faulting("Debug.Print CInt(\"x\")"), ErrorBreakMode.Unhandled, debug: true, async (provider, stopped) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);

            // the caller the error unwinds into is given the error that was waited at, which already says where it is.
            var resumed = await ResumeAsync(provider);

            Assert.AreEqual(ExecutionOutcome.RuntimeError, resumed.Outcome, resumed.ErrorMessage);
            Assert.AreEqual(6, resumed.ErrorLine);
        });

    [TestMethod]
    public async Task AnErrorAtANext_IsWaitedAtOnce_AndNotAgainAtTheCallAsItUnwinds()
        => await RunAsync(Program(
            "Public Sub Main()",
            "    Helper",
            "    Debug.Print \"after\"",
            "End Sub",
            "Private Sub Helper()",
            "    Dim i As Long",
            "    GoTo Skip",
            "    For i = 1 To 2",
            "Skip:",
            "    Next",
            "End Sub"), ErrorBreakMode.Unhandled, debug: true, async (provider, stopped) =>
        {
            // a Next is no statement of its own, and its error is at the For it closes.
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            Assert.AreEqual(92, stopped.ErrorNumber);
            Assert.AreEqual(7, stopped.ErrorLine, "the For");

            var resumed = await ResumeAsync(provider);

            Assert.AreEqual(ExecutionOutcome.RuntimeError, resumed.Outcome, resumed.ErrorMessage);
            Assert.AreEqual(7, resumed.ErrorLine);
        });

    [TestMethod]
    public async Task AnErrorALibraryMemberRaises_NotUnderADebugger_EndsTheRunAtTheStatementThatCalledIt()
        => await RunAsync(Faulting("Debug.Print CInt(\"x\")"), ErrorBreakMode.None, debug: false, (provider, done) =>
        {
            Assert.AreEqual(ExecutionOutcome.RuntimeError, done.Outcome, done.ErrorMessage);
            Assert.AreEqual(6, done.ErrorLine, "the statement in Helper, not the call in Main");
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task AnErrorThatIsCaught_IsNotWaitedAt_WhenOnlyTheUnhandledOnesAre()
        => await RunAsync(Handled, ErrorBreakMode.Unhandled, debug: true, (provider, done) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, done.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "caught" }, done.Output.Select(line => line.Trim()).ToArray());
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task AnErrorThatIsCaught_IsWaitedAt_WhenAllOfThemAre_AndIsCaughtWhenTheProgramGoesOn()
        => await RunAsync(Handled, ErrorBreakMode.All, debug: true, async (provider, stopped) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            Assert.AreEqual(11, stopped.ErrorNumber);

            var resumed = await ResumeAsync(provider);

            Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "caught" }, resumed.Output.Select(line => line.Trim()).ToArray());
        });

    [TestMethod]
    public async Task NoErrorIsWaitedAt_WhenTheModeSaysNone()
        => await RunAsync(Unhandled, ErrorBreakMode.None, debug: true, (provider, done) =>
        {
            Assert.AreEqual(ExecutionOutcome.RuntimeError, done.Outcome);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task AProgramNotUnderADebugger_IsNeverWaitedAt()
        => await RunAsync(Unhandled, ErrorBreakMode.All, debug: false, (provider, done) =>
        {
            Assert.AreEqual(ExecutionOutcome.RuntimeError, done.Outcome);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task AStopThatIsNotAnError_SaysNoErrorWhenTheProgramWaitsAtIt()
        => await RunAsync(Program("Public Sub Main()", "    Stop", "End Sub"), ErrorBreakMode.All, debug: true, (provider, stopped) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome);
            Assert.AreEqual(0, stopped.ErrorNumber);
            return Task.CompletedTask;
        });
}
