using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Runtime.Compiled;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// Error handling and <c>GoSub</c> in compiled code (IR lowering S2.4): the <c>On Error</c> statements, <c>GoSub</c> and <c>Return</c> compiled; an error
/// raised under a handler, a <c>Resume</c>, an <c>Error</c> statement, an <c>On...GoTo</c> run by the interpreter alone, and the activation gone on
/// compiled where it was left. A program runs the same either way - what it prints, the error it raises and where, what <c>Err</c> holds after it.
/// </summary>
[TestClass]
public sealed class CompiledErrorHandlingTests
{
    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> RunAsync(string program, VBExecutionEngine engine,
        IReadOnlyList<(string Name, string Source)>? classes = null)
    {
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync(classes ?? [], program, (provider, run, _) =>
        {
            made = (Observed(run), CompiledExecutionTests.ByName(provider));
            return Task.CompletedTask;
        }, engine: engine);
        return made;
    }

    // the program run both ways: the compiled run is the interpreted one, and the procedure named was compiled, and ran to its end in compiled code - or was
    // handed to the interpreter as often as walkBacks says - having the interpreter run recovered instructions alone. With expected, what both runs
    // observe holds it: the result is the one MS-VBA gives, and not only one both engines agree on.
    internal static async Task<CompilationResult> AssertAlikeAsync(string program, string compiled, long recoveries, long walkBacks = 0,
        IReadOnlyList<(string Name, string Source)>? classes = null, string? expected = null)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, classes);
        var run = await RunAsync(program, VBExecutionEngine.Compiled, classes);
        Assert.AreEqual(interpreted.Observed, run.Observed);
        if (expected is not null)
        {
            StringAssert.Contains(run.Observed, expected);
        }

        Assert.IsTrue(run.Compiled.TryGetValue(compiled, out var result), $"{compiled} was not called");
        Assert.IsNotNull(result.Body, $"{compiled}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");

        // under the random walk-back every activation may be handed to the interpreter, which is what that run is for.
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(walkBacks, result.Body.WalkBacks, $"{compiled}'s walk-backs");
            Assert.AreEqual(recoveries, result.Body.Recoveries, $"{compiled}'s recoveries");
        }

        return result;
    }

    // the program run both ways, whatever compiled code does of it: what was compiled.
    internal static async Task<IReadOnlyDictionary<string, CompilationResult>> AssertAlikeAsync(string program,
        IReadOnlyList<(string Name, string Source)>? classes = null)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, classes);
        var run = await RunAsync(program, VBExecutionEngine.Compiled, classes);
        Assert.AreEqual(interpreted.Observed, run.Observed);
        return run.Compiled;
    }

    [TestMethod]
    public async Task AnErrorResumeNextGoesPast_IsGonePastInCompiledCode_AndTheLoopRunsOn()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Total()",
            "End Sub",
            "Private Function Total() As Double",
            "    On Error Resume Next",
            "    Dim I As Long",
            "    Dim D As Double",
            "    For I = 1 To 30",
            "        D = 10 / (I Mod 10)",
            "        Total = Total + D",
            "    Next",
            "End Function"), "Total", recoveries: 3);

    [TestMethod]
    public async Task AnErrorOnErrorGoToCatches_RunsTheHandlerCompiled_AndResumeNextGoesOnAfterIt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Ratio(10, 2)",
            "    Debug.Print Ratio(1, 0)",
            "End Sub",
            "Private Function Ratio(ByVal A As Long, ByVal B As Long) As Long",
            "    On Error GoTo Failed",
            "    Ratio = A \\ B",
            "    Ratio = Ratio + 100",
            "    Exit Function",
            "Failed:",
            "    Ratio = -1",
            "    Resume Next",
            "End Function"), "Ratio", recoveries: 2);

    [TestMethod]
    public async Task Resume_RunsTheStatementThatFailedAgain_InCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Retried()",
            "End Sub",
            "Private Function Retried() As Long",
            "    Dim D As Long",
            "    On Error GoTo Fixing",
            "    Retried = 12 \\ D",
            "    Exit Function",
            "Fixing:",
            "    D = 4",
            "    Resume",
            "End Function"), "Retried", recoveries: 2);

    [TestMethod]
    public async Task ResumeALabel_GoesOnAtTheLabel()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Labelled(0)",
            "End Sub",
            "Private Function Labelled(ByVal D As Long) As Long",
            "    On Error GoTo Fixing",
            "    Labelled = 1 \\ D",
            "    Labelled = 5",
            "Done:",
            "    Labelled = Labelled + 10",
            "    Exit Function",
            "Fixing:",
            "    Labelled = 7",
            "    Resume Done",
            "End Function"), "Labelled", recoveries: 2);

    // the error raised in a loop goes to a handler outside it, and Resume Next back into it: the loop goes on where it was, compiled.
    [TestMethod]
    public async Task AnErrorInALoop_HandledOutsideIt_ResumesTheLoop()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted()",
            "End Sub",
            "Private Function Counted() As Long",
            "    Dim I As Long, D As Long",
            "    On Error GoTo Skip",
            "    For I = 1 To 5",
            "        D = 10 \\ (I - 3)",
            "        Counted = Counted + 1",
            "    Next",
            "    Exit Function",
            "Skip:",
            "    Counted = Counted + 100",
            "    Resume Next",
            "End Function"), "Counted", recoveries: 2);

    [TestMethod]
    public async Task AnErrorStatement_IsCaughtByTheHandler()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Raised()",
            "End Sub",
            "Private Function Raised() As Long",
            "    On Error Resume Next",
            "    Error 5",
            "    Raised = 1",
            "    On Error GoTo Caught",
            "    Error 13",
            "    Raised = 2",
            "    Exit Function",
            "Caught:",
            "    Raised = Raised + 40",
            "End Function"), "Raised", recoveries: 2);

    // an error raised after On Error GoTo 0 has no handler: it goes to the caller, from the line it was raised at.
    [TestMethod]
    public async Task AnErrorNoHandlerCatches_GoesToTheCaller()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Unguarded(0)",
            "End Sub",
            "Private Function Unguarded(ByVal D As Long) As Long",
            "    On Error Resume Next",
            "    Unguarded = 1 \\ D",
            "    On Error GoTo 0",
            "    Unguarded = 2 \\ D",
            "End Function"), "Unguarded", recoveries: 1);

    // the handler is running: a second error goes to the caller, as the interpreter has it.
    [TestMethod]
    public async Task AnErrorInTheHandler_GoesToTheCaller()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Twice(0)",
            "End Sub",
            "Private Function Twice(ByVal D As Long) As Long",
            "    On Error GoTo Failed",
            "    Twice = 1 \\ D",
            "    Exit Function",
            "Failed:",
            "    Twice = 2 \\ D",
            "End Function"), "Twice", recoveries: 1);

    // MS-VBAL §6.1.3.2.1: the Exit of a handler clears Err; an error On Error Resume Next went past is still Err when the procedure returns.
    [TestMethod]
    public async Task ErrAfterTheCall_IsClearedByTheHandlersExit_AndNotAfterResumeNext()
    {
        var program = Program(
            "Public Sub Main()",
            "    Debug.Print Caught(0); Err.Number",
            "    Debug.Print Skipped(0); Err.Number",
            "End Sub",
            "Private Function Caught(ByVal D As Long) As Long",
            "    On Error GoTo Failed",
            "    Caught = 1 \\ D",
            "    Exit Function",
            "Failed:",
            "    Caught = -1",
            "    Exit Function",
            "End Function",
            "Private Function Skipped(ByVal D As Long) As Long",
            "    On Error Resume Next",
            "    Skipped = 1 \\ D",
            "    Skipped = 3",
            "End Function");

        await AssertAlikeAsync(program, "Caught", recoveries: 1);
        await AssertAlikeAsync(program, "Skipped", recoveries: 1);
    }

    // a callee with no handler of its own raises its error into the compiled caller's, which goes past it.
    [TestMethod]
    public async Task AnErrorACalleeRaises_IsCaughtByTheCompiledCallersHandler()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer()",
            "End Sub",
            "Private Function Inner(ByVal D As Long) As Long",
            "    Inner = 10 \\ D",
            "End Function",
            "Private Function Outer() As Long",
            "    On Error Resume Next",
            "    Outer = 1",
            "    Outer = Inner(0)",
            "    Outer = Outer + 1",
            "End Function"), "Outer", recoveries: 1);

    [TestMethod]
    public async Task GoSubAndReturn_AreCompiled()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Quadrupled(3)",
            "End Sub",
            "Private Function Quadrupled(ByVal N As Long) As Long",
            "    GoSub Doubling",
            "    GoSub Doubling",
            "    Exit Function",
            "Doubling:",
            "    N = N * 2",
            "    Quadrupled = N",
            "    Return",
            "End Function"), "Quadrupled", recoveries: 0);

    // MS-VBAL §5.4.2.15: a Return with no GoSub to return to is error 3, which the interpreter raises.
    [TestMethod]
    public async Task AReturnWithoutGoSub_IsError3_WhichTheHandlerCatches()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Returned()",
            "End Sub",
            "Private Function Returned() As Long",
            "    On Error GoTo Failed",
            "    Return",
            "    Exit Function",
            "Failed:",
            "    Returned = 3",
            "End Function"), "Returned", recoveries: 1);

    [TestMethod]
    public async Task OnGoTo_GoesToTheLabelItSelects()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Picked(1); Picked(2); Picked(3)",
            "End Sub",
            "Private Function Picked(ByVal N As Long) As Long",
            "    On N GoTo One, Two",
            "    Picked = 0",
            "    Exit Function",
            "One:",
            "    Picked = 1",
            "    Exit Function",
            "Two:",
            "    Picked = 2",
            "End Function"), "Picked", recoveries: 3);

    [TestMethod]
    public async Task OnGoSub_ReturnsAfterIt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed(2)",
            "End Sub",
            "Private Function Summed(ByVal N As Long) As Long",
            "    On N GoSub One, Two",
            "    Summed = Summed + 100",
            "    Exit Function",
            "One:",
            "    Summed = 1",
            "    Return",
            "Two:",
            "    Summed = 2",
            "    Return",
            "End Function"), "Summed", recoveries: 1);

    // an error in a For header gone past: the loop the interpreter gave its state - a 0 end and a step of 1 (oracle s54) - is stepped in compiled code.
    [TestMethod]
    public async Task AnErrorInAForHeader_GonePast_RunsTheSameAsTheInterpreter()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Headed(0)",
            "End Sub",
            "Private Function Headed(ByVal D As Long) As Long",
            "    Dim I As Long",
            "    On Error Resume Next",
            "    For I = 1 To 10 \\ D",
            "        Headed = Headed + 1",
            "    Next",
            "    Headed = Headed + 100",
            "End Function"), "Headed", recoveries: 1);

    // a Resume into a loop that was never opened: its Next is error 92 (MS-VBAL §5.4.2.3), which compiled code, with no state to step the loop with, leaves
    // to the interpreter.
    [TestMethod]
    public async Task AResumeIntoALoopNeverOpened_GoesOnInTheInterpreter_WhoseNextRaisesError92()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Jumped(0); Err.Number",
            "End Sub",
            "Private Function Jumped(ByVal D As Long) As Long",
            "    Dim I As Long",
            "    I = 5",
            "    On Error GoTo Failed",
            "    Jumped = 1 \\ D",
            "    Exit Function",
            "Failed:",
            "    On Error Resume Next",
            "    Resume Inside",
            "    For I = 1 To 3",
            "Inside:",
            "        Jumped = Jumped + 1",
            "    Next",
            "End Function"), "Jumped", recoveries: 1, walkBacks: 1);

    // Erl is a library function, which compiled code does not call: the procedure is the interpreter's whole, and runs the same.
    [TestMethod]
    public async Task Erl_IsTheLineTheHandledErrorWasRaisedAt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Numbered(0)",
            "End Sub",
            "Private Function Numbered(ByVal D As Long) As Long",
            "    On Error GoTo Failed",
            "10  Numbered = 1",
            "20  Numbered = Numbered \\ D",
            "    Exit Function",
            "Failed:",
            "    Numbered = Erl",
            "End Function"));

    // a Resume back into a loop that ended: the loop has the end and the step it had, as the interpreter keeps them, and its Next steps it with them.
    [TestMethod]
    public async Task AResumeIntoALoopThatEnded_StepsItWithTheEndAndTheStepItHad()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Again(0)",
            "End Sub",
            "Private Function Again(ByVal D As Long) As Long",
            "    Dim I As Long, N As Long",
            "    On Error GoTo Fixing",
            "    For I = 1 To 2",
            "Inside:",
            "        N = N + 1",
            "    Next I",
            "    N = N \\ D",
            "    Again = N",
            "    Exit Function",
            "Fixing:",
            "    D = 1",
            "    Resume Inside",
            "End Function"), "Again", recoveries: 2);

    // a For statement that fails the second time it runs, gone past: its loop has the end and the step the first time gave it (oracle s54), which the
    // counter, not assigned, is stepped to.
    [TestMethod]
    public async Task AForHeaderThatFailsTheSecondTime_GonePast_KeepsTheEndAndTheStepTheFirstTimeGaveIt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Rerun(1)",
            "End Sub",
            "Private Function Rerun(ByVal D As Long) As Long",
            "    Dim I As Long, K As Long",
            "    On Error Resume Next",
            "    For K = 1 To 2",
            "        For I = 1 To 10 \\ D",
            "            Rerun = Rerun + 1",
            "        Next",
            "        D = 0",
            "        I = 5",
            "    Next",
            "End Function"), "Rerun", recoveries: 1);

    [TestMethod]
    public async Task ANestedGoSub_ReturnsToEachCallerInTurn()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Nested(1)",
            "End Sub",
            "Private Function Nested(ByVal N As Long) As Long",
            "    GoSub Adding",
            "    Nested = N",
            "    Exit Function",
            "Adding:",
            "    N = N + 1",
            "    GoSub Doubling",
            "    N = N * 10",
            "    Return",
            "Doubling:",
            "    N = N * 2",
            "    Return",
            "End Function"), "Nested", recoveries: 0);

    [TestMethod]
    public async Task AGoSubInAForLoop_ReturnsIntoTheLoop()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Looped()",
            "End Sub",
            "Private Function Looped() As Long",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        GoSub Adding",
            "    Next",
            "    Exit Function",
            "Adding:",
            "    Looped = Looped + I",
            "    Return",
            "End Function"), "Looped", recoveries: 0);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static Task<ExecuteResult> ResumeAsync(EnvironmentSessionProvider provider)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

    // the error a handler of compiled code catches is waited at when every error is, while the interpreter runs the instruction that raised it; the program
    // goes on to the handler, and to its end.
    [TestMethod]
    public async Task AnErrorACompiledHandlerCatches_IsWaitedAt_WhenEveryErrorIs_AndGoesToTheHandler()
    {
        CompiledExecutionTests.RequirePayloads();
        await ModuleWorkspace.InspectAsync([], Program(
            "Public Sub Main()",
            "    Debug.Print Ratio(1, 0)",
            "End Sub",
            "Private Function Ratio(ByVal A As Long, ByVal B As Long) As Long",
            "    On Error GoTo Failed",
            "    Ratio = A \\ B",
            "    Exit Function",
            "Failed:",
            "    Ratio = -1",
            "End Function"), async (provider, first, again) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, first.Outcome, first.ErrorMessage);
            Assert.IsTrue(provider.Image.Compiler.Results.Any(result => result.Procedure.Name == "Ratio" && result.Result.Body is not null), "Ratio is compiled");
            _ = await new HostDebugErrorBreakHandler(provider).Handle(new HostDebugErrorBreakParams { Mode = ErrorBreakMode.All }, CancellationToken.None);

            var stopped = await again();
            Assert.AreEqual((ExecutionOutcome.Suspended, 11, 5), (stopped.Outcome, stopped.ErrorNumber, stopped.ErrorLine), stopped.ErrorMessage);
            Assert.AreEqual("Ratio", provider.Execution.Stack().Frames[0].Procedure);

            var resumed = await ResumeAsync(provider);

            Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "-1" }, resumed.Output.Select(line => line.Trim()).ToArray());
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }

    // the program run to the Stop in Inner, at stopLine, while Outer, compiled and with an error handler, is in the middle of its call; Outer edited - a
    // line added above its body - and the program resumed to its end.
    private static async Task<ExecuteResult> EditedWhileACompiledCallerWaitsAsync(string program, string above, int stopLine)
    {
        CompiledExecutionTests.RequirePayloads();
        ExecuteResult? resumed = null;
        await ModuleWorkspace.InspectAsync([], program, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            var stopped = await ResumeAsync(provider);
            Assert.AreEqual((ExecutionOutcome.Suspended, stopLine), (stopped.Outcome, stopped.ErrorLine), stopped.ErrorMessage);
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.IsTrue(provider.Session.CallStack.Frames.OfType<RDCore.Runtime.Execution.Frames.CallStackFrame>().ElementAt(1).RunsCompiled,
                    "Outer is running compiled, in the middle of its call");
            }

            var edited = program.Replace(above, "    n = n\r\n" + above, StringComparison.Ordinal);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", RDCore.SDK.Model.AST.Declarations.ModuleType.StdModule, edited));
            resumed = await ResumeAsync(provider);
        }, debug: true, engine: VBExecutionEngine.Compiled);

        return resumed!;
    }

    // the call returns into the edited code, where compiled code wants the interpreter: the statement after the call runs once, in the edited body.
    [TestMethod]
    public async Task ACompiledCallerWithAHandler_EditedInTheMiddleOfItsCall_RunsTheStatementAfterTheCallOnce()
    {
        var resumed = await EditedWhileACompiledCallerWaitsAsync(Program(
            "Public Sub Main()",
            "    Stop",
            "    Debug.Print Outer(5)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    On Error Resume Next",
            "    Dim i As Long, t As Long, k As Long",
            "    For i = 1 To n",
            "        t = Inner(i)",
            "        k = k + 1",
            "    Next",
            "    Outer = t + k",
            "End Function",
            "Private Function Inner(ByVal x As Long) As Long",
            "    If x = 3 Then Stop",
            "    Inner = x * 10",
            "End Function"), "    For i = 1 To n\r\n", 14);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "55" }, resumed.Output.Select(line => line.Trim()).ToArray());
    }

    // the GoSub the compiled caller is in returns where its GoSub now is, in the edited code.
    [TestMethod]
    public async Task ACompiledCallerInAGoSub_EditedInTheMiddleOfItsCall_ReturnsAfterTheGoSubWhereItNowIs()
    {
        var resumed = await EditedWhileACompiledCallerWaitsAsync(Program(
            "Public Sub Main()",
            "    Stop",
            "    Debug.Print Outer(3)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim t As Long",
            "    GoSub Work",
            "    t = t + 1",
            "    Outer = t",
            "    Exit Function",
            "Work:",
            "    t = Inner(n)",
            "    Return",
            "End Function",
            "Private Function Inner(ByVal x As Long) As Long",
            "    If x = 3 Then Stop",
            "    Inner = x * 10",
            "End Function"), "    GoSub Work\r\n", 15);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "31" }, resumed.Output.Select(line => line.Trim()).ToArray());
    }

    // the error the callee raises after the edit goes to the compiled caller's handler where its label now is.
    [TestMethod]
    public async Task ACompiledCallersHandler_EditedInTheMiddleOfItsCall_IsWhereItsLabelNowIs()
    {
        var resumed = await EditedWhileACompiledCallerWaitsAsync(Program(
            "Public Sub Main()",
            "    Stop",
            "    Debug.Print Outer(3)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    On Error GoTo Failed",
            "    Dim t As Long",
            "    t = Inner(n)",
            "    Outer = t",
            "    Exit Function",
            "Failed:",
            "    Outer = -1",
            "End Function",
            "Private Function Inner(ByVal x As Long) As Long",
            "    If x = 3 Then Stop",
            "    Inner = x * 1000000000",
            "End Function"), "    t = Inner(n)\r\n", 14);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "-1" }, resumed.Output.Select(line => line.Trim()).ToArray());
    }
}
