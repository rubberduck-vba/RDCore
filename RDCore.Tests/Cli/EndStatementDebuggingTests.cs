using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// A procedure's <c>End Sub</c>, <c>End Function</c> or <c>End Property</c> is a line a program under a debugger stops on before the procedure returns, with
/// its variables still there, as MS-VBA stops there: a breakpoint on it is verified and waited at, whichever engine runs the program.
/// </summary>
[TestClass]
public sealed class EndStatementDebuggingTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static Task<ExecuteResult> Resume(EnvironmentSessionProvider provider)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

    private static Task<DebugBreakpointsResult> SetBreakpoints(EnvironmentSessionProvider provider, params int[] lines)
        => new HostDebugBreakpointsHandler(provider).Handle(new HostDebugBreakpointsParams
        {
            ModuleName = "Program",
            Breakpoints = [.. lines.Select(line => new HostBreakpointLine { Line = line })],
        }, CancellationToken.None).WaitAsync(Patience);

    private static Task<DebugGotoResult> Goto(EnvironmentSessionProvider provider, int line)
        => new HostDebugGotoHandler(provider).Handle(new HostDebugGotoParams { Line = line }, CancellationToken.None).WaitAsync(Patience);

    private static Dictionary<string, string> LocalsOf(EnvironmentSessionProvider provider, int frame)
        => provider.Execution.Variables(provider.Execution.Stack().Frames[frame].Id, DebugVariableScope.Locals, 0).Variables
            .ToDictionary(variable => variable.Name, variable => variable.Value);

    // Main waits at its Stop, then calls Work, whose End Sub is line 8.
    private static readonly string Working = Program(
        "Public Sub Main()",
        "    Stop",
        "    Work",
        "End Sub",
        "Private Sub Work()",
        "    Dim n As Long",
        "    n = 41",
        "    n = n + 1",
        "End Sub");

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "interpreted")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "compiled")]
    public async Task ABreakpointOnEndSub_IsVerified_AndWaitedAt_BeforeTheProcedureReturns(VBExecutionEngine engine)
        => await ModuleWorkspace.InspectAsync([], Working, async (provider, first, _) =>
        {
            Assert.AreEqual((ExecutionOutcome.Suspended, 1), (first.Outcome, first.ErrorLine), first.ErrorMessage);

            var set = await SetBreakpoints(provider, 8);
            Assert.IsTrue(set.Breakpoints.Single().Verified, "an End Sub is a line the program can wait at");

            var hit = await Resume(provider);

            Assert.AreEqual((ExecutionOutcome.Suspended, 8), (hit.Outcome, hit.ErrorLine), hit.ErrorMessage);
            var stack = provider.Execution.Stack().Frames;
            Assert.AreEqual(("Work", 8, "Main"), (stack[0].Procedure, stack[0].Line, stack[1].Procedure), "Work has not returned");
            Assert.AreEqual("42", LocalsOf(provider, 0)["n"], "its variables are still there, as its last statement left them");

            var done = await Resume(provider);
            Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, done.ErrorMessage);
        }, debug: true, engine: engine);

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "interpreted")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "compiled")]
    public async Task ABreakpointOnEndFunction_IsWaitedAt_WithTheResultAssigned(VBExecutionEngine engine)
        => await ModuleWorkspace.InspectAsync([], Program(
            "Public Sub Main()",
            "    Stop",
            "    Debug.Print Twice(21)",
            "End Sub",
            "Private Function Twice(ByVal x As Long) As Long",
            "    Twice = x * 2",
            "End Function"), async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            Assert.IsTrue((await SetBreakpoints(provider, 6)).Breakpoints.Single().Verified);

            var hit = await Resume(provider);

            Assert.AreEqual((ExecutionOutcome.Suspended, 6), (hit.Outcome, hit.ErrorLine), hit.ErrorMessage);
            Assert.AreEqual("21", LocalsOf(provider, 0)["x"]);

            var done = await Resume(provider);
            Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, done.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "42" }, done.Output.Select(line => line.Trim()).ToArray());
        }, debug: true, engine: engine);

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "interpreted")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "compiled")]
    public async Task AnExitSub_GoesThroughEndSub_WhereABreakpointIsWaitedAt(VBExecutionEngine engine)
        => await ModuleWorkspace.InspectAsync([], Program(
            "Public Sub Main()",
            "    Stop",
            "    Work",
            "End Sub",
            "Private Sub Work()",
            "    Dim n As Long",
            "    n = 1",
            "    If n = 1 Then Exit Sub",
            "    n = 99",
            "End Sub"), async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            Assert.IsTrue((await SetBreakpoints(provider, 9)).Breakpoints.Single().Verified);

            var hit = await Resume(provider);

            Assert.AreEqual((ExecutionOutcome.Suspended, 9), (hit.Outcome, hit.ErrorLine), hit.ErrorMessage);
            Assert.AreEqual("1", LocalsOf(provider, 0)["n"], "the statement after the Exit Sub has not run");

            var done = await Resume(provider);
            Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, done.ErrorMessage);
        }, debug: true, engine: engine);

    // a breakpoint on a block's closing line is waited at whichever way the block was gone through (a branch that ends with a jump, the last branch falling
    // through, no branch taken), with t as the block left it. The block is written at line 4, after t = 7.
    [TestMethod]
    [DataRow("If t = 7 Then|    t = 1|Else|    t = 2|End If", 8, "1", DisplayName = "End If, after a Then branch")]
    [DataRow("If t = 0 Then|    t = 1|Else|    t = 2|End If", 8, "2", DisplayName = "End If, after the Else branch")]
    [DataRow("If t = 0 Then|    t = 1|End If", 6, "7", DisplayName = "End If, no branch taken")]
    [DataRow("Select Case t|    Case 7|        t = 1|    Case Else|        t = 2|End Select", 9, "1", DisplayName = "End Select, after a Case")]
    [DataRow("Select Case t|    Case 0|        t = 1|End Select", 7, "7", DisplayName = "End Select, no Case matched")]
    [DataRow("With New Collection|    t = 1|End With", 6, "1", DisplayName = "End With")]
    [DataRow("Do While t = 7|    t = 1|Loop", 6, "1", DisplayName = "Loop")]
    [DataRow("For t = 1 To 1|    t = t + 10|Next", 6, "11", DisplayName = "Next")]
    [DataRow("Do|    t = t + 1|Loop While t < 8", 6, "8", DisplayName = "Loop While")]
    public async Task ABreakpointOnABlocksClosingLine_IsVerified_AndWaitedAt(string block, int line, string value)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            await ModuleWorkspace.InspectAsync([], Program([
                "Public Sub Main()",
                "    Dim t As Long",
                "    Stop",
                "    t = 7",
                .. block.Split('|').Select(text => "    " + text),
                "    Debug.Print \"done\"",
                "End Sub"]), async (provider, first, _) =>
            {
                Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
                Assert.IsTrue((await SetBreakpoints(provider, line)).Breakpoints.Single().Verified, $"{engine}: a closing line is one the program can wait at");

                var hit = await Resume(provider);

                Assert.AreEqual((ExecutionOutcome.Suspended, line), (hit.Outcome, hit.ErrorLine), $"{engine}: {hit.ErrorMessage}");
                Assert.AreEqual(value, LocalsOf(provider, 0)["t"], engine.ToString());

                var done = await Resume(provider);
                Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, $"{engine}: {done.ErrorMessage}");
                CollectionAssert.AreEqual(new[] { "done" }, done.Output.Select(text => text.Trim()).ToArray(), $"{engine}: waited at once");
            }, debug: true, engine: engine);
        }
    }

    // an error that leaves Fail, for its caller's handler, does not go through Fail's End Sub: nothing of Fail runs after the statement that raised it.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "interpreted")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "compiled")]
    public async Task AnErrorThatLeavesAProcedure_DoesNotStopAtItsEndSub(VBExecutionEngine engine)
        => await ModuleWorkspace.InspectAsync([], Program(
            "Public Sub Main()",
            "    On Error GoTo Handled",
            "    Stop",
            "    Fail",
            "    Exit Sub",
            "Handled:",
            "    Debug.Print \"handled\"",
            "End Sub",
            "Private Sub Fail()",
            "    Err.Raise 5",
            "End Sub"), async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            Assert.IsTrue((await SetBreakpoints(provider, 10)).Breakpoints.Single().Verified);

            var done = await Resume(provider);

            Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, done.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "handled" }, done.Output.Select(line => line.Trim()).ToArray());
        }, debug: true, engine: engine);

    // Outer runs compiled, and is on the stack at its call while Inner waits at its Stop: the breakpoint on its End Function is set then, and the compiled
    // activation stops there before it returns, as an interpreted one does.
    [TestMethod]
    public async Task ABreakpointOnTheEndFunction_OfACompiledActivationThatIsRunning_IsWaitedAt()
    {
        CompiledExecutionTests.RequirePayloads();
        await ModuleWorkspace.InspectAsync([], Program(
            "Public Sub Main()",
            "    Stop",
            "    Debug.Print Outer(5)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long",
            "    For i = 1 To n",
            "        t = Inner(i)",
            "    Next",
            "    Outer = t",
            "End Function",
            "Private Function Inner(ByVal x As Long) As Long",
            "    If x = 3 Then Stop",
            "    Inner = x * 10",
            "End Function"), async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            RDCore.Runtime.Compiled.CompiledBody? outer = null;
            provider.Image.Compiler.Compiled += (procedure, result) =>
            {
                if (procedure.Name == "Outer")
                {
                    outer = result.Body;
                }
            };

            var stopped = await Resume(provider);
            Assert.AreEqual((ExecutionOutcome.Suspended, 12), (stopped.Outcome, stopped.ErrorLine), stopped.ErrorMessage);
            Assert.IsNotNull(outer, "Outer runs compiled");
            var walkedBack = outer.WalkBacks;
            Assert.IsTrue((await SetBreakpoints(provider, 10)).Breakpoints.Single().Verified);

            var hit = await Resume(provider);

            Assert.AreEqual((ExecutionOutcome.Suspended, 10), (hit.Outcome, hit.ErrorLine), hit.ErrorMessage);
            var locals = LocalsOf(provider, 0);
            Assert.AreEqual(("6", "50"), (locals["i"], locals["t"]));
            if (!RDCore.Runtime.Compiled.CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(walkedBack + 1, outer.WalkBacks, "the compiled activation walked back to stop there");
            }

            var done = await Resume(provider);
            Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, done.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "50" }, done.Output.Select(line => line.Trim()).ToArray());
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }

    // the program counter moved onto End Sub: the rest of Work does not run, and Work returns from there.
    [TestMethod]
    public async Task TheNextStatement_SetOnEndSub_ReturnsFromTheProcedure()
        => await ModuleWorkspace.InspectAsync([], Program(
            "Public Total As Long",
            "Public Sub Main()",
            "    Work",
            "    Debug.Print Total",
            "End Sub",
            "Private Sub Work()",
            "    Stop",
            "    Total = 99",
            "End Sub"), async (provider, first, _) =>
        {
            Assert.AreEqual((ExecutionOutcome.Suspended, 6), (first.Outcome, first.ErrorLine), first.ErrorMessage);

            var moved = await Goto(provider, 8);
            Assert.IsTrue(moved.Moved, moved.Reason);
            Assert.AreEqual(8, moved.Line);

            var done = await Resume(provider);
            Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, done.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "0" }, done.Output.Select(line => line.Trim()).ToArray());
        }, debug: true);
}
