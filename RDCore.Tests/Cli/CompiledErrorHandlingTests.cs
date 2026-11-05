using RDCore.CLI.Host;
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
    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> RunAsync(string program, VBExecutionEngine engine)
    {
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], program, (provider, run, _) =>
        {
            made = (Observed(run), provider.Image.Compiler.Results.ToDictionary(entry => entry.Procedure.Name, entry => entry.Result));
            return Task.CompletedTask;
        }, engine: engine);
        return made;
    }

    // the program run both ways: the compiled run is the interpreted one, and the procedure named was compiled, and ran to its end in compiled code, having
    // the interpreter run recovered instructions alone.
    private static async Task<CompiledBody> AssertAlikeAsync(string program, string compiled, long recoveries)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter);
        var run = await RunAsync(program, VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted.Observed, run.Observed);

        Assert.IsTrue(run.Compiled.TryGetValue(compiled, out var result), $"{compiled} was not called");
        Assert.IsNotNull(result.Body, $"{compiled}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");

        // under the random walk-back every activation may be handed to the interpreter, which is what that run is for.
        if (!CompiledExecution.WalksBackAtRandom)
        {
            Assert.AreEqual(0L, result.Body.WalkBacks, $"{compiled} was handed to the interpreter");
            Assert.AreEqual(recoveries, result.Body.Recoveries, $"{compiled}'s recoveries");
        }

        return result.Body;
    }

    // the program run both ways, whatever compiled code does of it.
    private static async Task AssertAlikeAsync(string program)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter);
        var run = await RunAsync(program, VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted.Observed, run.Observed);
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
            "End Function"));

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
            "End Function"));

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

    // an error in a For header gone past: the body the interpreter goes on in has no loop state compiled code could step it with.
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
            "End Function"));

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
            "End Function"));

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
}
