using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program under a debugger runs compiled where it can, until something only the interpreter does is asked for: a breakpoint, or a step. A compiled
/// activation that is running then is handed to the interpreter at its next safepoint, and stops where the interpreter stops.
/// </summary>
[TestClass]
public sealed class CompiledDebuggingTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // Main waits at its Stop, then calls Spin once, whose loop compiles and never ends: its Step of 0 leaves the counter where it is. Whatever the test asks
    // of the program while it runs is asked of that one activation of Spin, however late it comes.
    private static readonly string Spinning = Program(
        "Public Sub Main()",
        "    Stop",
        "    Spin",
        "End Sub",
        "Private Sub Spin()",
        "    Dim i As Long",
        "    Dim n As Long",
        "    For i = 1 To 2 Step 0",
        "        n = n Xor 1",
        "    Next",
        "End Sub");

    private static Task<ExecuteResult> Resume(EnvironmentSessionProvider provider)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

    private static Task<DebugBreakpointsResult> SetBreakpoints(EnvironmentSessionProvider provider, params int[] lines)
        => new HostDebugBreakpointsHandler(provider).Handle(new HostDebugBreakpointsParams
        {
            ModuleName = "Program",
            Breakpoints = [.. lines.Select(line => new HostBreakpointLine { Line = line })],
        }, CancellationToken.None).WaitAsync(Patience);

    private static Task<DebugAck> Pause(EnvironmentSessionProvider provider)
        => new HostDebugPauseHandler(provider).Handle(new HostDebugPauseParams(), CancellationToken.None).WaitAsync(Patience);

    private static Task<DebugAck> Terminate(EnvironmentSessionProvider provider)
        => new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);

    // the compiled body of Spin, once its activation has first looked whether the program is to stop, at the back edge of its loop: it is running compiled
    // code then, and a breakpoint or a pause asked for after it is seen at its next look. Called before the program goes on to call Spin.
    private static Task<RDCore.Runtime.Compiled.CompiledBody> SpinLookedAsync(EnvironmentSessionProvider provider)
    {
        var looked = new TaskCompletionSource<RDCore.Runtime.Compiled.CompiledBody>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Image.Compiler.Compiled += (procedure, result) =>
        {
            if (procedure.Name == "Spin" && result.Body is { } body)
            {
                _ = body.Looked.ContinueWith(_ => looked.TrySetResult(body), TaskScheduler.Default);
            }
        };

        return looked.Task.WaitAsync(Patience);
    }

    [TestMethod]
    public async Task ABreakpointSetWhileACompiledLoopRuns_IsWaitedAt_InTheLoop()
    {
        CompiledExecutionTests.RequirePayloads();
        await ModuleWorkspace.InspectAsync([], Spinning, async (provider, first, runAgain) =>
        {
            // the program waits at Main's Stop, before Spin is called: its call compiles Spin, and that activation runs compiled until it is stopped.
            Assert.AreEqual((ExecutionOutcome.Suspended, 1), (first.Outcome, first.ErrorLine), first.ErrorMessage);
            try
            {
                var looked = SpinLookedAsync(provider);
                var running = Resume(provider);
                var spin = await looked;
                var walkedBack = spin.WalkBacks;

                _ = await SetBreakpoints(provider, 8);
                var hit = await running;

                Assert.AreEqual((ExecutionOutcome.Suspended, 8), (hit.Outcome, hit.ErrorLine), hit.ErrorMessage);

                // under the random walk-back the activation may have been handed to the interpreter at its first look, before the breakpoint was set, which
                // stops it all the same.
                if (!RDCore.Runtime.Compiled.CompiledExecution.HandsOverAtRandom)
                {
                    Assert.AreEqual(walkedBack + 1, spin.WalkBacks, "the compiled activation that was running walked back to stop there");
                }
            }
            finally
            {
                // a program that is left running spins on its own thread until it is ended.
                _ = await Terminate(provider);
            }
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }

    // Main waits at its Stop, then calls Outer, which runs compiled - no breakpoint is set - and calls Inner, which a Stop keeps interpreted, and which
    // stops the program at its third call.
    private static readonly string Calling = Program(
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
        "End Function");

    // a compiled caller in the middle of its call is on the stack at the call, with its variables as they are.
    [TestMethod]
    public async Task ACompiledCaller_OfACalleeThatWaits_IsOnTheStackAtItsCall_WithItsVariables()
    {
        await ModuleWorkspace.InspectAsync([], Calling, async (provider, first, runAgain) =>
        {
            Assert.AreEqual((ExecutionOutcome.Suspended, 1), (first.Outcome, first.ErrorLine), first.ErrorMessage);
            try
            {
                var stopped = await Resume(provider);
                Assert.AreEqual((ExecutionOutcome.Suspended, 12), (stopped.Outcome, stopped.ErrorLine), stopped.ErrorMessage);

                var stack = provider.Execution.Stack().Frames;
                Assert.AreEqual(("Inner", "Outer", 7), (stack[0].Procedure, stack[1].Procedure, stack[1].Line));
                var outer = provider.Execution.Variables(stack[1].Id, DebugVariableScope.Locals, 0).Variables.ToDictionary(variable => variable.Name, variable => variable.Value);
                Assert.AreEqual(("3", "20"), (outer["i"], outer["t"]), "the caller's variables, as they were when it made the call");
            }
            finally
            {
                _ = await Terminate(provider);
            }
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }

    // the program goes on: the compiled caller reads back its variables, which the debugger could have changed while it waited, and goes on from its call.
    [TestMethod]
    public async Task ACompiledCaller_OfACalleeThatWaited_GoesOnFromItsCall()
    {
        await ModuleWorkspace.InspectAsync([], Calling, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            var stopped = await Resume(provider);
            Assert.AreEqual((ExecutionOutcome.Suspended, 12), (stopped.Outcome, stopped.ErrorLine), stopped.ErrorMessage);

            var done = await Resume(provider);

            Assert.AreEqual(ExecutionOutcome.Completed, done.Outcome, done.ErrorMessage);
            CollectionAssert.AreEqual(new[] { "50" }, done.Output.Select(line => line.Trim()).ToArray());
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }

    // the program run to the Stop in Inner, at stopLine, while Outer, compiled, is in the middle of its call; edited; and resumed.
    private static async Task<ExecuteResult> EditedWhileACompiledCallerWaitsAsync(string program, string edited, int stopLine)
    {
        CompiledExecutionTests.RequirePayloads();
        ExecuteResult? resumed = null;
        await ModuleWorkspace.InspectAsync([], program, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                var stopped = await Resume(provider);
                Assert.AreEqual((ExecutionOutcome.Suspended, stopLine), (stopped.Outcome, stopped.ErrorLine), stopped.ErrorMessage);
                Assert.AreEqual("Outer", provider.Execution.Stack().Frames[1].Procedure);

                // under the random walk-back Outer may have been handed to the interpreter before it made the call, and is edited as any frame is.
                if (!RDCore.Runtime.Compiled.CompiledExecution.HandsOverAtRandom)
                {
                    Assert.IsTrue(provider.Session.CallStack.Frames.OfType<RDCore.Runtime.Execution.Frames.CallStackFrame>().ElementAt(1).RunsCompiled,
                        "Outer is running compiled, in the middle of its call");
                }

                _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", RDCore.SDK.Model.AST.Declarations.ModuleType.StdModule, edited));
                resumed = await Resume(provider);
            }
            finally
            {
                _ = await Terminate(provider);
            }
        }, debug: true, engine: VBExecutionEngine.Compiled);

        return resumed!;
    }

    // the compiled caller's procedure edited while the program waits in its callee: the caller goes on in the edited code once its call returns, from the
    // instruction after its call, with its variables and its loop as they are.
    [TestMethod]
    [DataRow("    Outer = t|    Outer = t + 1", "51", DisplayName = "a line below the call")]
    [DataRow("    Dim i As Long, t As Long|    Dim i As Long, t As Long\r\n    n = n", "50", DisplayName = "a line added above the loop the call is in")]
    [DataRow("    Next|    Next\r\n    t = t * 2", "100", DisplayName = "a line added after the loop the call is in")]
    public async Task AnEditOfACompiledCallerInTheMiddleOfItsCall_IsApplied_WhenTheCallReturns(string edit, string expected)
    {
        var (find, replace) = (edit.Split('|')[0], edit.Split('|')[1]);

        var resumed = await EditedWhileACompiledCallerWaitsAsync(Calling, Calling.Replace(find, replace, StringComparison.Ordinal), 12);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { expected }, resumed.Output.Select(line => line.Trim()).ToArray());
    }

    // where the call it is in the middle of goes can't be told, or there is none: the edit is refused, never guessed.
    [TestMethod]
    [DataRow("        t = Inner(i)|        t = Inner(i + 0)", DisplayName = "the call edited")]
    [DataRow("        t = Inner(i)\r\n|", DisplayName = "the call removed")]
    [DataRow("    For i = 1 To n|    For i = 1 To n + 1", DisplayName = "the loop the call is in, its opening edited")]
    public async Task AnEditOfTheCallACompiledCallerIsInTheMiddleOf_IsRefused(string edit)
    {
        var (find, replace) = (edit.Split('|')[0], edit.Split('|')[1]);

        var resumed = await EditedWhileACompiledCallerWaitsAsync(Calling, Calling.Replace(find, replace, StringComparison.Ordinal), 12);

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "Program.Outer");
    }

    // the call moved out of its loop into another: its loop's state is of a loop it is no longer in.
    [TestMethod]
    public async Task TheCallACompiledCallerIsInTheMiddleOf_MovedIntoAnotherLoop_IsRefused()
    {
        CompiledExecutionTests.RequirePayloads();
        var program = Program(
            "Public Sub Main()",
            "    Stop",
            "    Debug.Print Outer(5)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, j As Long, t As Long",
            "    For i = 1 To n",
            "        t = Inner(i)",
            "    Next",
            "    For j = 1 To 2",
            "        t = t + 1",
            "    Next",
            "    Outer = t",
            "End Function",
            "Private Function Inner(ByVal x As Long) As Long",
            "    If x = 3 Then Stop",
            "    Inner = x * 10",
            "End Function");
        var edited = program.Replace("        t = Inner(i)\r\n", "", StringComparison.Ordinal)
            .Replace("        t = t + 1\r\n", "        t = t + 1\r\n        t = Inner(i)\r\n", StringComparison.Ordinal);

        var resumed = await EditedWhileACompiledCallerWaitsAsync(program, edited, 15);

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "Program.Outer");
    }

    // the callee raises an error after the edit: it is the call's, where the call now is in the edited code, and the caller's caller catches it.
    [TestMethod]
    public async Task AnErrorTheCalleeRaises_AfterAnEditOfTheCompiledCaller_IsTheCallsWhereItNowIs()
    {
        CompiledExecutionTests.RequirePayloads();
        var program = Program(
            "Public Sub Main()",
            "    On Error GoTo Caught",
            "    Stop",
            "    Debug.Print Outer(5)",
            "    Exit Sub",
            "Caught:",
            "    Debug.Print \"caught \" & Err.Number",
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
            "    Inner = x * 1000000000",
            "End Function");
        var edited = program.Replace("    Dim i As Long, t As Long\r\n", "    Dim i As Long, t As Long\r\n    n = n\r\n", StringComparison.Ordinal);

        var resumed = await EditedWhileACompiledCallerWaitsAsync(program, edited, 16);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "caught 6" }, resumed.Output.Select(line => line.Trim()).ToArray());
    }

    // Main waits at its Stop, then calls Outer, which sums a Variant speculated to hold a Long in a loop that compiles, and calls Inner, which stops the
    // program at its third call.
    private static readonly string VariantCalling = Program(
        "Public Sub Main()",
        "    Stop",
        "    Debug.Print Outer(5)",
        "End Sub",
        "Private Function Outer(ByVal n As Long) As String",
        "    Dim i As Long, t As Long",
        "    Dim S As Variant",
        "    S = 0",
        "    For i = 1 To n",
        "        t = Inner(i)",
        "        S = S + t",
        "    Next",
        "    Outer = S & \" \" & TypeName(S)",
        "End Function",
        "Private Function Inner(ByVal x As Long) As Long",
        "    If x = 3 Then Stop",
        "    Inner = x * 10",
        "End Function");

    // VariantCalling run by engine to the Stop in Inner, in the middle of Outer's loop; edited, when edited is given; and resumed to its end.
    private static async Task<ExecuteResult> StoppedInASpeculatedLoopAsync(VBExecutionEngine engine, string? edited)
    {
        ExecuteResult? resumed = null;
        await ModuleWorkspace.InspectAsync([], VariantCalling, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                var stopped = await Resume(provider);
                Assert.AreEqual((ExecutionOutcome.Suspended, 15), (stopped.Outcome, stopped.ErrorLine), stopped.ErrorMessage);
                Assert.AreEqual("Outer", provider.Execution.Stack().Frames[1].Procedure);
                if (edited is not null)
                {
                    var errors = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", RDCore.SDK.Model.AST.Declarations.ModuleType.StdModule, edited));
                    Assert.IsEmpty(errors, string.Join("; ", errors));
                }

                resumed = await Resume(provider);
            }
            finally
            {
                _ = await Terminate(provider);
            }
        }, debug: true, engine: engine);

        return resumed!;
    }

    // a stop in the middle of a loop that speculates what a Variant holds, and an edit there that makes it hold a Double from then on: the loop goes on with
    // what the Variant holds, as the interpreter's does. An edit that declares a Variant W, set to 1 in the loop after the call that stops and added to
    // the sum, adds 1 at each of the 3 passes that are left: 153.
    [TestMethod]
    [DataRow(null, "150 Long", DisplayName = "resumed")]
    [DataRow("        S = S + t|        S = S + t + 0.5", "151.5 Double", DisplayName = "edited to add a Double")]
    [DataRow("    Dim i As Long, t As Long|    Dim W As Variant\r\n    Dim i As Long, t As Long|        S = S + t|        W = 1\r\n        S = S + t + W", "153 Long",
        DisplayName = "a Variant declared above the others")]
    [DataRow("    Dim S As Variant|    Dim W As Variant, S As Variant|        S = S + t|        W = 1\r\n        S = S + t + W", "153 Long",
        DisplayName = "a Variant declared before the summed one")]
    public async Task ASpeculatedLoop_StoppedInAndEdited_GoesOnAsTheInterpretersDoes(string? edit, string expected)
    {
        CompiledExecutionTests.RequirePayloads();

        // the edit is pairs of the text replaced and the text it is replaced with, separated by |.
        var edited = edit?.Split('|').Chunk(2).Aggregate(VariantCalling, (source, pair) => source.Replace(pair[0], pair[1], StringComparison.Ordinal));

        var interpreted = await StoppedInASpeculatedLoopAsync(VBExecutionEngine.Interpreter, edited);
        var compiled = await StoppedInASpeculatedLoopAsync(VBExecutionEngine.Compiled, edited);

        Assert.AreEqual((ExecutionOutcome.Completed, ExecutionOutcome.Completed), (interpreted.Outcome, compiled.Outcome), $"{interpreted.ErrorMessage} / {compiled.ErrorMessage}");
        CollectionAssert.AreEqual(new[] { expected }, interpreted.Output.Select(line => line.Trim()).ToArray());
        CollectionAssert.AreEqual(interpreted.Output.ToArray(), compiled.Output.ToArray());
    }

    // Calling, with Inner stopping twice in the same call: at line 12, then at line 13.
    private static readonly string StoppingTwice = Calling.Replace("    If x = 3 Then Stop\r\n", "    If x = 3 Then Stop\r\n    If x = 3 Then Stop\r\n", StringComparison.Ordinal);

    // the program run to the first Stop in Inner while Outer, compiled, is in the middle of its call; Outer edited as firstEdit, resumed to the second Stop
    // in the same call, edited as secondEdit, and resumed to its end.
    private static async Task<ExecuteResult> EditedAtTwoWaitsOfACompiledCallerAsync(string firstEdit, string secondEdit)
    {
        CompiledExecutionTests.RequirePayloads();
        ExecuteResult? resumed = null;
        await ModuleWorkspace.InspectAsync([], StoppingTwice, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                var stopped = await Resume(provider);
                Assert.AreEqual((ExecutionOutcome.Suspended, 12), (stopped.Outcome, stopped.ErrorLine), stopped.ErrorMessage);
                if (!RDCore.Runtime.Compiled.CompiledExecution.HandsOverAtRandom)
                {
                    Assert.IsTrue(provider.Session.CallStack.Frames.OfType<RDCore.Runtime.Execution.Frames.CallStackFrame>().ElementAt(1).RunsCompiled,
                        "Outer is running compiled, in the middle of its call");
                }

                var errors = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", RDCore.SDK.Model.AST.Declarations.ModuleType.StdModule, firstEdit));
                Assert.IsEmpty(errors, string.Join("; ", errors));
                var again = await Resume(provider);
                Assert.AreEqual((ExecutionOutcome.Suspended, 13), (again.Outcome, again.ErrorLine), again.ErrorMessage);

                errors = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", RDCore.SDK.Model.AST.Declarations.ModuleType.StdModule, secondEdit));
                Assert.IsEmpty(errors, string.Join("; ", errors));
                resumed = await Resume(provider);
            }
            finally
            {
                _ = await Terminate(provider);
            }
        }, debug: true, engine: VBExecutionEngine.Compiled);

        return resumed!;
    }

    // the compiled caller edited at one wait in its call, and edited again at a later wait in the same call: the later edit is the one it goes on in - the
    // code as it was when undone, or the second edit.
    [TestMethod]
    [DataRow("    Outer = t", "50", DisplayName = "the edit undone")]
    [DataRow("    Outer = t + 2", "52", DisplayName = "edited again")]
    public async Task ACompiledCaller_EditedAtTwoWaitsOfItsCall_GoesOnInTheLaterEdit(string second, string expected)
    {
        var resumed = await EditedAtTwoWaitsOfACompiledCallerAsync(
            StoppingTwice.Replace("    Outer = t", "    Outer = t + 1", StringComparison.Ordinal),
            StoppingTwice.Replace("    Outer = t", second, StringComparison.Ordinal));

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { expected }, resumed.Output.Select(line => line.Trim()).ToArray());
    }

    // a loop paused where it goes round again is shown at its Next, as MS-VBA shows it: the back edge has no statement of its own.
    [TestMethod]
    public async Task ACompiledLoopPaused_IsShownWaitingAtItsNext()
    {
        CompiledExecutionTests.RequirePayloads();
        await ModuleWorkspace.InspectAsync([], Spinning, async (provider, first, runAgain) =>
        {
            Assert.AreEqual((ExecutionOutcome.Suspended, 1), (first.Outcome, first.ErrorLine), first.ErrorMessage);
            try
            {
                var looked = SpinLookedAsync(provider);
                var running = Resume(provider);
                _ = await looked;

                Assert.IsTrue((await Pause(provider)).Acted);
                var paused = await running;

                // compiled code polls at the loop's back edge, and is handed to the interpreter there; under the random walk-back, at any line of the loop.
                Assert.AreEqual(ExecutionOutcome.Suspended, paused.Outcome, paused.ErrorMessage);
                if (RDCore.Runtime.Compiled.CompiledExecution.HandsOverAtRandom)
                {
                    CollectionAssert.Contains(new[] { 8, 9 }, paused.ErrorLine, "a line of the loop, and not none");
                }
                else
                {
                    Assert.AreEqual(9, paused.ErrorLine, "the loop's Next");
                }

                Assert.AreEqual("Spin", provider.Execution.Stack().Frames[0].Procedure);
            }
            finally
            {
                _ = await Terminate(provider);
            }
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }
}
