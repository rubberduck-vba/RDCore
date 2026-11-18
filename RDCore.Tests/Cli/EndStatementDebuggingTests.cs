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

    private static Task<ExecuteResult> Step(EnvironmentSessionProvider provider, StepKind kind)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams { Step = kind }, CancellationToken.None).WaitAsync(Patience);

    // the lines each step stops at until the program ends, then how it ended, and what the steps printed.
    private static async Task<(string[] Stops, string[] Printed)> StepsToTheEnd(EnvironmentSessionProvider provider, StepKind kind)
    {
        var (stops, printed) = (new List<string>(), new List<string>());
        for (var step = 0; step < 50; step++)
        {
            var stop = await Step(provider, kind);
            printed.AddRange(stop.Output.Select(line => line.Trim()));
            if (stop.Outcome is not ExecutionOutcome.Suspended)
            {
                stops.Add(stop.Outcome.ToString());
                break;
            }
            stops.Add(stop.ErrorLine.ToString());
        }
        return ([.. stops], [.. printed]);
    }

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
    [DataRow("If t = 0 Then|    t = 1|ElseIf t = 7 Then|    t = 2|End If", 8, "2", DisplayName = "End If, after an ElseIf branch")]
    [DataRow("If t = 7 Then|    If t = 7 Then|        t = 1|    End If|End If", 7, "1", DisplayName = "End If of an If in an If")]
    [DataRow("If t = 7 Then|    If t = 7 Then|        t = 1|    End If|End If", 8, "1", DisplayName = "End If, after the End If of an If in it")]
    [DataRow("Select Case t|    Case 7|        t = 1|    Case Else|        t = 2|End Select", 9, "1", DisplayName = "End Select, after a Case")]
    [DataRow("Select Case t|    Case 0|        t = 1|End Select", 7, "7", DisplayName = "End Select, no Case matched")]
    [DataRow("With New Collection|    t = 1|End With", 6, "1", DisplayName = "End With")]
    [DataRow("Do While t = 7|    t = 1|Loop", 6, "1", DisplayName = "Loop")]
    [DataRow("For t = 1 To 1|    t = t + 10|Next", 6, "11", DisplayName = "Next")]
    [DataRow("Do|    t = t + 1|Loop While t < 8", 6, "8", DisplayName = "Loop While")]
    [DataRow("Do|    t = t + 1|Loop Until t > 7", 6, "8", DisplayName = "Loop Until")]
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

    // a breakpoint on the End Property of a Property Let and of a Property Get is waited at before each returns, the Let's with the value it was given.
    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, DisplayName = "interpreted")]
    [DataRow(VBExecutionEngine.Compiled, DisplayName = "compiled")]
    public async Task ABreakpointOnEndProperty_IsVerified_AndWaitedAt_BeforeThePropertyReturns(VBExecutionEngine engine)
        => await ModuleWorkspace.InspectAsync([], Program(
            "Private mValue As Long",
            "Public Sub Main()",
            "    Stop",
            "    Value = 21",
            "    Debug.Print Value",
            "End Sub",
            "Public Property Get Value() As Long",
            "    Value = mValue * 2",
            "End Property",
            "Public Property Let Value(ByVal v As Long)",
            "    mValue = v",
            "End Property"), async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            CollectionAssert.AreEqual(new[] { true, true }, (await SetBreakpoints(provider, 8, 11)).Breakpoints.Select(set => set.Verified).ToArray());

            var let = await Resume(provider);
            Assert.AreEqual((ExecutionOutcome.Suspended, 11), (let.Outcome, let.ErrorLine), let.ErrorMessage);
            Assert.AreEqual("21", LocalsOf(provider, 0)["v"]);

            var get = await Resume(provider);
            Assert.AreEqual((ExecutionOutcome.Suspended, 8), (get.Outcome, get.ErrorLine), get.ErrorMessage);
            Assert.AreEqual(("Value", "Main"), (provider.Execution.Stack().Frames[0].Procedure, provider.Execution.Stack().Frames[1].Procedure));

            var done = await Resume(provider);
            Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, done.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "42" }, done.Output.Select(line => line.Trim()).ToArray());
        }, debug: true, engine: engine);

    // Step Into from T's Stop to the end of the program stops where MS-VBA stops. Measured in Excel (64-bit): a post-test loop's Do once, on entry, then the
    // body and its Loop line on each pass; a pre-test loop's Do While on entry and after each pass, its Loop on each pass. MS-VBA also stops at an End If
    // after its branch, and at the End Sub of a procedure that runs to its end: T's End Sub, then the line after the call, then Main's End Sub. T's lines are
    // written from line 6, its Stop at line 8, its block from line 9.
    [TestMethod]
    [DataRow("Do|    i = i + 1|Loop While i < 3", "9 10 11 10 11 10 11 12 13 4 5", DisplayName = "Do ... Loop While")]
    [DataRow("Do|    i = i + 1|Loop Until i >= 3", "9 10 11 10 11 10 11 12 13 4 5", DisplayName = "Do ... Loop Until")]
    [DataRow("Do While i < 3|    i = i + 1|Loop", "9 10 11 9 10 11 9 10 11 9 12 13 4 5", DisplayName = "Do While ... Loop")]
    [DataRow("If i = 0 Then|    i = 1|End If", "9 10 11 12 13 4 5", DisplayName = "If ... End If")]
    public async Task AStepInto_StopsAtABlocksClosingLine_AsMSVBAStops(string block, string stops)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            await ModuleWorkspace.InspectAsync([], Program([
                "Public R As Variant",
                "",
                "Public Sub Main()",
                "    T",
                "    R = Empty",
                "End Sub",
                "Public Sub T()",
                "    Dim i As Long",
                "    Stop",
                .. block.Split('|').Select(text => "    " + text),
                "    i = 0",
                "End Sub"]), async (provider, first, _) =>
            {
                Assert.AreEqual((ExecutionOutcome.Suspended, 8), (first.Outcome, first.ErrorLine), $"{engine}: {first.ErrorMessage}");

                var (stepped, _) = await StepsToTheEnd(provider, StepKind.Into);

                CollectionAssert.AreEqual(
                    stops.Split(' ').Append(nameof(ExecutionOutcome.Completed)).ToArray(), stepped, $"{engine}: {string.Join(" ", stepped)}");
            }, debug: true, engine: engine);
        }
    }

    // Outer runs compiled, and is on the stack at its call while Inner waits at its Stop, in the second pass of Outer's loop: the breakpoint on the loop's
    // closing line is set then. The compiled activation stops there, and the steps from there over the rest of Outer stop where MS-VBA stops. Outer's block
    // is written from line 6, its closing line at line 9.
    [TestMethod]
    [DataRow("Do While i < n|    i = i + 1|    t = Inner(i)|Loop", "6 7 8 9 6 10 11 3", "30", DisplayName = "Do While ... Loop")]
    [DataRow("Do|    i = i + 1|    t = Inner(i)|Loop While i < n", "7 8 9 10 11 3", "30", DisplayName = "Do ... Loop While")]
    [DataRow("Do|    i = i + 1|    t = Inner(i)|Loop Until i >= n", "7 8 9 10 11 3", "30", DisplayName = "Do ... Loop Until")]
    [DataRow("i = 2|If i = 2 Then|    t = Inner(i)|End If", "10 11 3", "20", DisplayName = "If ... End If")]
    public async Task AStepOver_FromTheClosingLine_ACompiledActivationStoppedAt_StopsAsMSVBAStops(string block, string stops, string printed)
    {
        CompiledExecutionTests.RequirePayloads();
        await ModuleWorkspace.InspectAsync([], Program([
            "Public Sub Main()",
            "    Stop",
            "    Debug.Print Outer(3)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long",
            .. block.Split('|').Select(text => "    " + text),
            "    Outer = t",
            "End Function",
            "Private Function Inner(ByVal x As Long) As Long",
            "    If x = 2 Then Stop",
            "    Inner = x * 10",
            "End Function"]), async (provider, first, _) =>
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
            Assert.AreEqual((ExecutionOutcome.Suspended, 13), (stopped.Outcome, stopped.ErrorLine), stopped.ErrorMessage);
            Assert.IsNotNull(outer, "Outer runs compiled");
            var walkedBack = outer.WalkBacks;
            Assert.IsTrue((await SetBreakpoints(provider, 9)).Breakpoints.Single().Verified);

            var hit = await Resume(provider);

            Assert.AreEqual((ExecutionOutcome.Suspended, 9), (hit.Outcome, hit.ErrorLine), hit.ErrorMessage);
            var locals = LocalsOf(provider, 0);
            Assert.AreEqual(("2", "20"), (locals["i"], locals["t"]));
            if (!RDCore.Runtime.Compiled.CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(walkedBack + 1, outer.WalkBacks, "the compiled activation walked back to stop there");
            }

            await SetBreakpoints(provider);
            var (stepped, output) = await StepsToTheEnd(provider, StepKind.Over);

            CollectionAssert.AreEqual(stops.Split(' ').Append(nameof(ExecutionOutcome.Completed)).ToArray(), stepped, string.Join(" ", stepped));
            CollectionAssert.AreEqual(new[] { printed }, output);
        }, debug: true, engine: VBExecutionEngine.Compiled);
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
