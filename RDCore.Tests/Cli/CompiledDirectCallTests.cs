using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Runtime.Compiled;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Tests.Platform;

namespace RDCore.Tests.Cli;

/// <summary>
/// A call compiled code makes of a procedure that is compiled too goes through the callee's typed entry, with the arguments as compiled code holds them
/// (IR lowering S2.3b): what the program does, and what each thing that reads its activations sees of it - a debugger's stack and variables, an edit applied
/// while it waits, the stack trace of an error - is what it is when the interpreter makes the call.
/// </summary>
[TestClass]
public sealed class CompiledDirectCallTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static Task<ExecuteSessionResult> Resume(EnvironmentSessionProvider provider)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

    private static Task<HostDebugAck> Terminate(EnvironmentSessionProvider provider)
        => new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);

    // what a run is to the person who runs it: what it printed, the error it raised, where, and the stack trace of it.
    private static string Observed(ExecuteSessionResult result)
        => $"{result.Outcome} | {string.Join(" ; ", result.Output.Select(line => line.Trim()))} | {result.ErrorNumber} {result.ErrorCode} {result.ErrorMessage} "
            + $"@{result.ErrorLine}:{result.ErrorCharacter} | {string.Join(" < ", result.StackTrace.Select(frame => $"{frame.Procedure}@{frame.Line}:{frame.Character}"))}";

    private static CompiledBody BodyOf(EnvironmentSessionProvider provider, string procedure)
        => provider.Image.Compiler.Results.Single(result => result.Procedure.Name == procedure).Result.Body
            ?? throw new AssertFailedException($"{procedure} is not compiled");

    // the program run to its end with engine, and the calls the bodies named made, each directly and through the invoker.
    private static async Task<(string Observed, long Direct, long Indirect)> RunAsync(string program, VBExecutionEngine engine, params string[] callers)
    {
        var observed = string.Empty;
        long direct = 0, indirect = 0;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], program, (provider, run, _) =>
        {
            observed = Observed(run);
            if (engine is VBExecutionEngine.Compiled)
            {
                foreach (var caller in callers)
                {
                    var body = BodyOf(provider, caller);
                    (direct, indirect) = (direct + body.DirectCalls, indirect + body.IndirectCalls);
                }
            }

            return Task.CompletedTask;
        }, engine: engine);
        return (observed, direct, indirect);
    }

    // how many calls a caller made directly, and through the invoker, out of all it makes: each of them while it ran compiled. Under the random walk-back its
    // activation is handed to the interpreter at a safepoint of its own, which makes the calls after it, and neither count says how many.
    private static void AssertCalls(long all, (long Direct, long Indirect) made)
    {
        if (CompiledExecution.WalksBackAtRandom)
        {
            Assert.IsLessThanOrEqualTo(all, made.Direct + made.Indirect);
            return;
        }

        Assert.AreEqual((all, 0L), made);
    }

    // the program run both ways: the compiled run is the interpreted one, and what the callers named called directly, and through the invoker.
    private static async Task<(string Observed, long Direct, long Indirect)> AssertAlikeAsync(string program, params string[] callers)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter);
        var compiled = await RunAsync(program, VBExecutionEngine.Compiled, callers);
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        return compiled;
    }

    [TestMethod]
    public async Task ACompiledCallee_IsCalledThroughItsTypedEntry()
    {
        var (observed, direct, indirect) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Sum(100)",
            "End Sub",
            "Private Function Sum(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long, u As Long",
            "    For i = 1 To n",
            "        u = Twice(i, 0.5)",
            "        t = t + u",
            "    Next",
            "    Sum = t",
            "End Function",
            "Private Function Twice(ByVal x As Long, ByVal f As Double) As Long",
            "    Twice = x * 2 + f",
            "End Function"), "Sum");

        // x * 2 + 0.5 is let-coerced to a Long, rounded to even: x * 2.
        StringAssert.Contains(observed, "| 10100 |");
        AssertCalls(100, (direct, indirect));
    }

    // a procedure that calls itself calls its own typed entry.
    [TestMethod]
    public async Task AProcedureThatCallsItself_CallsItsOwnTypedEntry()
    {
        var (observed, direct, indirect) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Down(10)",
            "End Sub",
            "Private Function Down(ByVal n As Long) As Long",
            "    Dim r As Long",
            "    If n = 0 Then",
            "        Down = 0",
            "    Else",
            "        r = Down(n - 1)",
            "        Down = r + n",
            "    End If",
            "End Function"), "Down");

        StringAssert.Contains(observed, "| 55 |");
        AssertCalls(10, (direct, indirect));
    }

    // two procedures that call each other: the one compiled first calls the other through its typed entry, and the other - compiled for it, while the first
    // was being compiled - calls back through the invoker.
    [TestMethod]
    public async Task ProceduresThatCallEachOther_OneCallsTheOtherDirectly_TheOtherThroughTheInvoker()
    {
        var (observed, direct, indirect) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print IsEven(9); IsEven(10)",
            "End Sub",
            "Private Function IsEven(ByVal n As Long) As Long",
            "    If n = 0 Then",
            "        IsEven = 1",
            "    Else",
            "        IsEven = IsOdd(n - 1)",
            "    End If",
            "End Function",
            "Private Function IsOdd(ByVal n As Long) As Long",
            "    If n = 0 Then",
            "        IsOdd = 0",
            "    Else",
            "        IsOdd = IsEven(n - 1)",
            "    End If",
            "End Function"), "IsEven", "IsOdd");

        StringAssert.Contains(observed, "| 0  1 |");
        if (!CompiledExecution.WalksBackAtRandom)
        {
            Assert.IsGreaterThan(0L, direct, "IsEven calls IsOdd through its typed entry");
            Assert.IsGreaterThan(0L, indirect, "IsOdd calls IsEven through the invoker");
        }
    }

    // an error raised in a callee called directly is the callee's, in the stack trace and where it was raised, and a handler of a caller up the stack
    // handles it.
    [TestMethod]
    [DataRow(false, DisplayName = "not handled")]
    [DataRow(true, DisplayName = "handled by a caller up the stack")]
    public async Task AnErrorInACalleeCalledDirectly_IsTracedAsWhenTheInterpreterCallsIt(bool handled)
    {
        var (_, direct, _) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            handled ? "    On Error GoTo Failed" : "    Dim unused As Long",
            "    Debug.Print Outer(3)",
            "    Exit Sub",
            "Failed:",
            "    Debug.Print \"failed \"; Err.Number",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long, u As Long",
            "    For i = 1 To n",
            "        u = Divide(i)",
            "        t = t + u",
            "    Next",
            "    Outer = t",
            "End Function",
            "Private Function Divide(ByVal x As Long) As Long",
            "    Dim z As Long",
            "    z = x - 2",
            "    Divide = 10 \\ z",
            "End Function"), "Outer");

        if (!CompiledExecution.WalksBackAtRandom)
        {
            Assert.IsGreaterThan(0L, direct, "Outer calls Divide through its typed entry");
        }
    }

    // Outer calls Middle, compiled, through its typed entry; Middle calls Inner, which waits at its Stop, through the invoker - Inner, with a Stop, is not
    // compiled. Main waits at its own Stop first, so that the program runs under a debugger.
    private static readonly string Waiting = Program(
        "Public Sub Main()",
        "    Stop",
        "    Debug.Print Outer(3)",
        "End Sub",
        "Private Function Outer(ByVal n As Long) As Long",
        "    Dim i As Long, t As Long, u As Long",
        "    For i = 1 To n",
        "        u = Middle(i)",
        "        t = t + u",
        "    Next",
        "    Outer = t",
        "End Function",
        "Private Function Middle(ByVal x As Long) As Long",
        "    Dim w As Long",
        "    w = x * 10",
        "    w = Inner(w)",
        "    Middle = w + 1",
        "End Function",
        "Private Function Inner(ByVal y As Long) As Long",
        "    If y = 10 Then Stop",
        "    Inner = y",
        "End Function");

    // the program waits in Inner: the stack and Middle's variables as a debugger sees them, then, once an edit is applied (when there is one), what the
    // program does to its end.
    private static async Task<string> WaitedInInnerAsync(string? edited, VBExecutionEngine engine)
    {
        var observed = string.Empty;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], Waiting, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                var stopped = await Resume(provider);
                Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
                var stack = provider.Execution.Stack().Frames;
                var middle = stack.Single(frame => frame.Procedure == "Middle");
                var variables = provider.Execution.Variables(middle.Id, HostVariableScope.Locals, 0).Variables;
                observed = $"{string.Join(" < ", stack.Select(frame => $"{frame.Procedure}@{frame.Line}"))} | "
                    + $"{string.Join(", ", variables.Select(variable => $"{variable.Name}={variable.Value}"))}";
                if (engine is VBExecutionEngine.Compiled && !CompiledExecution.WalksBackAtRandom)
                {
                    Assert.AreEqual(1L, BodyOf(provider, "Outer").DirectCalls, "Outer calls Middle through its typed entry");
                }

                if (edited is not null)
                {
                    Assert.IsEmpty(await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", ModuleType.StdModule, edited)));
                }

                observed += " | " + Observed(await Resume(provider));
            }
            finally
            {
                if (provider.Execution.State is not ProgramState.Idle)
                {
                    _ = await Terminate(provider);
                }
            }
        }, debug: true, engine: engine);
        return observed;
    }

    // the debugger sees an activation of a callee called directly as an interpreted call makes it: on the stack, at its call, with its variables.
    [TestMethod]
    public async Task ACalleeCalledDirectly_IsOnTheStack_WithItsVariables_AsTheInterpreterHasIt()
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await WaitedInInnerAsync(null, VBExecutionEngine.Interpreter);
        var compiled = await WaitedInInnerAsync(null, VBExecutionEngine.Compiled);

        Assert.AreEqual(interpreted, compiled);
        StringAssert.Contains(compiled, "x=1, w=10");
        StringAssert.Contains(compiled, "| 63 |");
    }

    // an edit of a callee called directly, made while the program waits in what it calls, applies to it as to an interpreted activation of it: below its
    // call, when that call returns.
    [TestMethod]
    public async Task ACalleeCalledDirectly_EditedWhileItWaits_GoesOnInItsEditedBody()
    {
        CompiledExecutionTests.RequirePayloads();
        var edited = Waiting.Replace("    Middle = w + 1", "    Middle = w + 2", StringComparison.Ordinal);
        var interpreted = await WaitedInInnerAsync(edited, VBExecutionEngine.Interpreter);
        var compiled = await WaitedInInnerAsync(edited, VBExecutionEngine.Compiled);

        Assert.AreEqual(interpreted, compiled);
        StringAssert.Contains(compiled, "| 66 |");
    }

    // a callee called directly whose operation compiled code declines - a Double let-coerced to a Long, which rounds - is handed to the interpreter, which
    // assigns its result, and the call returns it.
    [TestMethod]
    public async Task ACalleeCalledDirectly_HandedToTheInterpreter_ReturnsWhatItAssigned()
    {
        CompiledExecutionTests.RequirePayloads();
        var program = Program(
            "Public Sub Main()",
            "    Debug.Print Sum(4)",
            "End Sub",
            "Private Function Sum(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long, u As Long",
            "    For i = 1 To n",
            "        u = Half(i)",
            "        t = t + u",
            "    Next",
            "    Sum = t",
            "End Function",
            "Private Function Half(ByVal x As Long) As Long",
            "    Dim d As Double",
            "    d = x / 2",
            "    Half = d",
            "End Function");
        var (observed, direct, indirect) = await AssertAlikeAsync(program, "Sum");

        // 0.5, 1, 1.5, 2 rounded to even: 0 + 1 + 2 + 2.
        StringAssert.Contains(observed, "| 5 |");
        AssertCalls(4, (direct, indirect));
    }

    // an End in an interpreted callee of a callee called directly ends the program there, as it does when the interpreter makes every call.
    [TestMethod]
    public async Task AnEnd_InACalleeOfACalleeCalledDirectly_EndsTheProgram()
    {
        var (observed, _, _) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(3)",
            "    Debug.Print \"not here\"",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long, u As Long",
            "    For i = 1 To n",
            "        u = Middle(i)",
            "        t = t + u",
            "    Next",
            "    Outer = t",
            "End Function",
            "Private Function Middle(ByVal x As Long) As Long",
            "    Dim w As Long",
            "    w = Ender(x)",
            "    Middle = w",
            "End Function",
            "Private Function Ender(ByVal y As Long) As Long",
            "    Debug.Print \"ender \"; y",
            "    If y = 2 Then End",
            "    Ender = y",
            "End Function"), "Outer");

        StringAssert.Contains(observed, "ender  1 ; ender  2");
        Assert.IsFalse(observed.Contains("not here", StringComparison.Ordinal), observed);
    }

    // an argument that overflows its parameter's type at a call made directly is the call's error 6, raised before anything is called.
    [TestMethod]
    public async Task AnArgumentThatOverflows_AtACallMadeDirectly_IsAnOverflowOfTheCall()
    {
        var (observed, _, _) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(3)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long, u As Long",
            "    For i = 1 To n",
            "        u = Small(i * 20000)",
            "        t = t + u",
            "    Next",
            "    Outer = t",
            "End Function",
            "Private Function Small(ByVal x As Integer) As Long",
            "    Small = x",
            "End Function"), "Outer");

        StringAssert.Contains(observed, "| 6 VBR00006");
    }

    // a Function called as a statement, through its typed entry: what it returns is not read.
    [TestMethod]
    public async Task AFunctionCalledAsAStatement_IsCalledThroughItsTypedEntry()
    {
        var (observed, direct, indirect) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Outer 3",
            "End Sub",
            "Private Sub Outer(ByVal n As Long)",
            "    Dim i As Long",
            "    For i = 1 To n",
            "        Twice i",
            "    Next",
            "End Sub",
            "Private Function Twice(ByVal x As Long) As Long",
            "    Twice = x * 2",
            "End Function"), "Outer");

        StringAssert.StartsWith(observed, "Completed");
        AssertCalls(3, (direct, indirect));
    }

    // the callee's body loaded again - an edit of its body alone, which defines no symbol again, run with its module as edited - is what the next call runs,
    // and not the IL compiled from the body it had: the caller is of another module, which is not loaded again, and keeps its compiled body.
    [TestMethod]
    public async Task ACalleeWhoseBodyAloneIsLoadedAgain_IsCalledAsItNowIs()
    {
        CompiledExecutionTests.RequirePayloads();
        static string Calling(string twice) => Program(
            "Public Sub Main()",
            "    Debug.Print Outer(3)",
            "End Sub",
            "Public Function Twice(ByVal x As Long) As Long",
            $"    Twice = {twice}",
            "End Function");
        var outer = ModuleWorkspace.StandardModule("Callers",
            "Public Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long, u As Long",
            "    For i = 1 To n",
            "        u = Twice(i)",
            "        t = t + u",
            "    Next",
            "    Outer = t",
            "End Function");

        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            using var running = CompiledExecution.Running(engine);
            await ModuleWorkspace.InspectWithModulesAsync([("Callers", outer)], Calling("x * 2"), async (provider, first, runAgain) =>
            {
                StringAssert.Contains(Observed(first), "| 12 |");
                var again = await RunWithAsync(provider, Calling("x * 3"));
                StringAssert.Contains(Observed(again), "| 18 |", engine.ToString());
                if (engine is VBExecutionEngine.Compiled && !CompiledExecution.WalksBackAtRandom)
                {
                    Assert.AreEqual(3L, BodyOf(provider, "Outer").DirectCalls, "the first run's calls only");
                }
            });
        }
    }

    private static Task<ExecuteSessionResult> RunWithAsync(EnvironmentSessionProvider provider, string source)
    {
        var uri = new UriBuilder(Wire.FileUri(ModuleWorkspace.Root)) { Fragment = "Program" }.Uri;
        var parse = new RDCore.Parsing.ModuleParser().Parse(Wire.FileUri(Path.Combine(ModuleWorkspace.Root, "Program.bas")), source);
        return new HostExecuteHandler(provider, NSubstitute.Substitute.For<RDCore.SDK.Services.VerboseMessages.IVerboseMessageBuilder>(),
                NullLogger<HostExecuteHandler>.Instance)
            .Handle(new HostExecuteParams
            {
                Json = PlatformJson.Serialize(new HostExecutePayload(uri, parse)),
                ModuleName = "Program",
                EntryPoint = "Main",
            }, CancellationToken.None).WaitAsync(Patience);
    }

    // the symbols defined again while the program waits: the caller compiled against them as they were makes no call through a typed entry after it.
    [TestMethod]
    public async Task AfterTheSymbolsAreDefinedAgain_NoCallIsMadeThroughATypedEntry()
    {
        CompiledExecutionTests.RequirePayloads();
        if (CompiledExecution.WalksBackAtRandom)
        {
            Assert.Inconclusive("under the random walk-back, the caller may be handed to the interpreter before it calls");
        }

        var edited = Waiting.Replace("    Inner = y", "    Inner = y + 0", StringComparison.Ordinal);
        using var running = CompiledExecution.Running(VBExecutionEngine.Compiled);
        await ModuleWorkspace.InspectAsync([], Waiting, async (provider, first, runAgain) =>
        {
            try
            {
                _ = await Resume(provider);
                Assert.IsEmpty(await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", ModuleType.StdModule, edited)));
                var done = await Resume(provider);
                StringAssert.Contains(Observed(done), "| 63 |");
                var outer = provider.Image.Compiler.Results.Where(result => result.Procedure.Name == "Outer").Select(result => result.Result.Body).OfType<CompiledBody>();
                Assert.AreEqual(1L, outer.Sum(body => body.DirectCalls), "only the call made before the symbols were defined again");
            }
            finally
            {
                if (provider.Execution.State is not ProgramState.Idle)
                {
                    _ = await Terminate(provider);
                }
            }
        }, debug: true, engine: VBExecutionEngine.Compiled);
    }

    // the session told to run no compiled code while a compiled caller waits on its callee: its calls after are made through the invoker, which interprets
    // them, as Run would.
    [TestMethod]
    [DoNotParallelize]
    public async Task TheEngineChangedWhileACompiledCallerWaits_ItsCallsAfterAreInterpreted()
    {
        CompiledExecutionTests.RequirePayloads();
        if (CompiledExecution.WalksBackAtRandom)
        {
            Assert.Inconclusive("under the random walk-back, the caller may be handed to the interpreter before it calls");
        }

        var engine = CompiledExecution.Engine;
        try
        {
            CompiledExecution.Engine = VBExecutionEngine.Compiled;
            await ModuleWorkspace.InspectAsync([], Waiting, async (provider, first, runAgain) =>
            {
                try
                {
                    _ = await Resume(provider);
                    CompiledExecution.Engine = VBExecutionEngine.Interpreter;
                    StringAssert.Contains(Observed(await Resume(provider)), "| 63 |");
                    var outer = BodyOf(provider, "Outer");
                    Assert.AreEqual((1L, 2L), (outer.DirectCalls, outer.IndirectCalls));
                }
                finally
                {
                    if (provider.Execution.State is not ProgramState.Idle)
                    {
                        _ = await Terminate(provider);
                    }
                }
            }, debug: true, engine: VBExecutionEngine.Compiled);
        }
        finally
        {
            CompiledExecution.Engine = engine;
        }
    }

    // a call stack as deep as it goes is error 28 (Out of stack space), with the call made directly and the interpreter making it: never the host's own stack
    // overflowing first, which a thread's default stack did at about 80 interpreted activations, crashing the host.
    [TestMethod]
    public async Task ARecursionThatNeverEnds_IsOutOfStackSpace()
    {
        var (observed, _, _) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    On Error GoTo Failed",
            "    Debug.Print Deep(1)",
            "    Exit Sub",
            "Failed:",
            "    Debug.Print \"error \"; Err.Number",
            "End Sub",
            "Private Function Deep(ByVal n As Long) As Long",
            "    Dim r As Long",
            "    r = Deep(n + 1)",
            "    Deep = r",
            "End Function"), "Deep");

        StringAssert.Contains(observed, "error  28");
    }

    // the stack trace of an error is the one captured where it was raised: every activation it unwinds, and the location in the innermost one only - not what
    // is left of the stack where a handler catches it.
    [TestMethod]
    public async Task TheStackTraceOfAnError_IsTheOneWhereItWasRaised()
    {
        CompiledExecutionTests.RequirePayloads();
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            using var running = CompiledExecution.Running(engine);
            await ModuleWorkspace.InspectAsync([], Program(
                "Public Sub Main()",
                "    Debug.Print Outer(3)",
                "End Sub",
                "Private Function Outer(ByVal n As Long) As Long",
                "    Dim u As Long",
                "    u = Divide(n)",
                "    Outer = u",
                "End Function",
                "Private Function Divide(ByVal x As Long) As Long",
                "    Divide = 10 \\ (x - 3)",
                "End Function"), (_, result, _) =>
                {
                    Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, engine.ToString());
                    CollectionAssert.AreEqual(new[] { "Divide", "Outer", "Main" }, result.StackTrace.Select(frame => frame.Procedure).ToArray(), engine.ToString());
                    return Task.CompletedTask;
                }, engine: engine);
        }
    }
}
