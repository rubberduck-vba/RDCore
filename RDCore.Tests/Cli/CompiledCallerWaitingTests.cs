using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Parsing;
using RDCore.Runtime.Compiled;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Platform.Components;
using RDCore.Runtime.Semantics;

namespace RDCore.Tests.Cli;

/// <summary>
/// A compiled caller of a callee the program waits in goes on as an interpreted caller does, whatever was changed while it waited: the module defined
/// again with the caller's body as it was, its function result, or its variables.
/// </summary>
[TestClass]
public sealed class CompiledCallerWaitingTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static Task<ExecuteResult> Resume(EnvironmentSessionProvider provider)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

    private static Task<DebugAck> Terminate(EnvironmentSessionProvider provider)
        => new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);

    private static SyntaxNode Statement(string text)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Execute.bas"), $"Public Sub E()\r\n{text}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children.OfType<StatementNode>().Single();
    }

    // a statement a debugger runs in the frame of Outer.
    private static async Task InOuter(EnvironmentSessionProvider provider, string statement)
    {
        var outer = provider.Execution.Stack().Frames.Single(frame => frame.Procedure == "Outer");
        var made = await new HostDebugExecuteHandler(provider, Substitute.For<IErrorDetailsRecorder>())
            .Handle(new HostDebugExecuteParams { FrameId = outer.Id, Json = PlatformJson.Serialize(Statement(statement)) }, CancellationToken.None).WaitAsync(Patience);
        Assert.IsTrue(made.Success, made.Error);
    }

    private static string Observed(ExecuteResult result)
        => $"{result.Outcome} | {string.Join(" ; ", result.Output.Select(line => line.Trim()))} | {result.ErrorNumber} {result.ErrorMessage} @{result.ErrorLine}";

    // Main waits at its Stop; resumed, it calls Outer, which waits in its call of Inner; then the module is defined again as edited, or a debugger runs
    // statements in Outer's frame, and the program goes on to its end. What it is to the person who runs it.
    private static async Task<string> WaitedInAsync(string program, string? edited, string[] statements, VBExecutionEngine engine)
        => (await WaitedInWithOuterAsync(program, edited, statements, engine)).Observed;

    // the same, and the compiled body of Outer, what its activation did counted there: none when Outer is not compiled.
    private static async Task<(string Observed, CompiledBody? Outer)> WaitedInWithOuterAsync(string program, string? edited, string[] statements,
        VBExecutionEngine engine)
    {
        var observed = "";
        CompiledBody? outer = null;
        await ModuleWorkspace.InspectAsync([], program, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                var stopped = await Resume(provider);
                Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
                Assert.AreEqual(("Inner", "Outer"), (provider.Execution.Stack().Frames[0].Procedure, provider.Execution.Stack().Frames[1].Procedure));
                if (engine is VBExecutionEngine.Compiled)
                {
                    Assert.IsTrue(provider.Image.Compiler.Results.Any(result => result.Procedure.Name == "Outer" && result.Result.Body is not null), "Outer runs compiled");
                }

                if (edited is not null)
                {
                    Assert.IsEmpty(await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", ModuleType.StdModule, edited)));
                }

                foreach (var statement in statements)
                {
                    await InOuter(provider, statement);
                }

                observed = Observed(await Resume(provider));
                outer = provider.Image.Compiler.Results.SingleOrDefault(result => result.Procedure.Name == "Outer").Result?.Body;
            }
            finally
            {
                if (provider.Execution.State is not ProgramState.Idle)
                {
                    _ = await Terminate(provider);
                }
            }
        }, debug: true, engine: engine);
        return (observed, outer);
    }

    private static async Task<string> AssertAlikeAsync(string program, string? edited, params string[] statements)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await WaitedInAsync(program, edited, statements, VBExecutionEngine.Interpreter);
        var compiled = await WaitedInAsync(program, edited, statements, VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted, compiled);
        return compiled;
    }

    // Outer calls Inner, which waits at its first call, then Helper; only Helper, after Outer, and the module's declarations are edited.
    private static string Calling(string declarations, string helper, string helperCall = "Helper(i)") => Program(
        declarations,
        "Public Sub Main()",
        "    Stop",
        "    Debug.Print Outer(3)",
        "End Sub",
        "Private Function Outer(ByVal n As Long) As Long",
        "    Dim i As Long, t As Long, u As Long",
        "    For i = 1 To n",
        "        u = Inner(i)",
        "        t = t + u",
        $"        u = {helperCall}",
        "        t = t + u + K",
        "    Next",
        "    Outer = t",
        "End Function",
        "Private Function Inner(ByVal x As Long) As Long",
        "    If x = 1 Then Stop",
        "    Inner = x",
        "End Function",
        helper);

    private const string Constant = "Private Const K As Long = 5";

    // the header of a callee edited while the caller waits: the caller's instructions are as they were, and what they call is bound as it now is.
    [TestMethod]
    [DataRow("ByVal y As Integer", "ByVal y As Double", "As Long", "Helper = y * 10", "Helper(i + 0.5)", DisplayName = "a parameter of another type")]
    [DataRow("ByVal y As Long", "ByRef y As Long", "As Long", "Helper = y: y = y + 100", "Helper(t)", DisplayName = "a parameter by reference")]
    [DataRow("ByVal y As Long", "ByVal y As Long, Optional ByVal z As Long = 7", "As Long", "Helper = y", "Helper(i)", DisplayName = "an optional parameter added")]
    [DataRow("ByVal y As Long", "ByVal y As Long", "As Double", "Helper = y * 1.5", "Helper(i)", DisplayName = "a result of another type")]
    public async Task ACallee_EditedWhileACompiledCallerWaits_IsCalledAsItNowIs(string parameters, string edited, string returns, string body, string call)
    {
        var helper = $"Private Function Helper({parameters}) As Long\r\n    {body}\r\nEnd Function";
        var editedHelper = $"Private Function Helper({edited}) {returns}\r\n    {body}\r\nEnd Function";
        if (edited.Contains("Optional", StringComparison.Ordinal))
        {
            editedHelper = editedHelper.Replace("Helper = y", "Helper = y + z", StringComparison.Ordinal);
        }

        _ = await AssertAlikeAsync(Calling(Constant, helper, call), Calling(Constant, editedHelper, call));
    }

    // a constant the program already read, given another value, is not applied to a program that waits: refused as for an interpreted caller.
    [TestMethod]
    public async Task AConstant_EditedWhileACompiledCallerWaits_IsRefusedAsForAnInterpretedCaller()
    {
        const string helper = "Private Function Helper(ByVal y As Long) As Long\r\n    Helper = y\r\nEnd Function";
        var observed = await AssertAlikeAsync(Calling(Constant, helper), Calling("Private Const K As Long = 6", helper));
        StringAssert.StartsWith(observed, "Refused");
        StringAssert.Contains(observed, "Program.K");
    }

    private static readonly string Returning = Program(
        "Public Sub Main()",
        "    Stop",
        "    Debug.Print Outer(3)",
        "End Sub",
        "Private Function Outer(ByVal n As Long) As Long",
        "    Dim u As Long",
        "    u = Inner(1)",
        "    n = n + u",
        "End Function",
        "Private Function Inner(ByVal x As Long) As Long",
        "    If x = 1 Then Stop",
        "    Inner = x",
        "End Function");

    // what a debugger sets in the compiled caller's frame while the program waits in the callee is what the caller goes on with.
    [TestMethod]
    public async Task TheResultOfACompiledCaller_SetWhileItWaits_IsWhatItReturns()
        => StringAssert.Contains(await AssertAlikeAsync(Returning, null, "Outer = 1000"), "| 1000 |");

    [TestMethod]
    public async Task AVariableOfACompiledCaller_SetWhileItWaits_IsWhatItGoesOnWith()
        => StringAssert.Contains(await AssertAlikeAsync(Returning.Replace("    n = n + u", "    Outer = n + u", StringComparison.Ordinal), null, "n = 1000"), "| 1001 |");

    // Outer sums what Inner returns of each pass of its loops, and waits in Inner's first call: the loops it goes on with are the ones it had, counting the way
    // they counted, in the type they counted in. A Variant counter is no scalar, and keeps Outer in the interpreter.
    private static string Looping(string declarations, string first, params string[] loops) => Program([
        "Public Sub Main()",
        "    Stop",
        "    Debug.Print Outer(3)",
        "End Sub",
        "Private Function Outer(ByVal n As Long) As Double",
        $"    Dim t As Double, u As Double, {declarations}",
        .. loops,
        "    Outer = t",
        "End Function",
        "Private Function Inner(ByVal x As Double) As Double",
        $"    If x = {first} Then Stop",
        "    Inner = x",
        "End Function"]);

    [TestMethod]
    [DataRow("i As Long", "3", "For i = n To 1 Step -1", "6", DisplayName = "a Long counting down")]
    [DataRow("i As Integer", "3", "For i = 3 To 1 Step -1", "6", DisplayName = "an Integer counting down")]
    [DataRow("i As Double", "0.5", "For i = 0.5 To 2 Step 0.5", "5", DisplayName = "a Double")]
    [DataRow("i As Double", "2", "For i = 2 To 0.5 Step -0.5", "5", DisplayName = "a Double counting down")]
    [DataRow("i As Single", "1.5", "For i = 1.5 To 0 Step -0.75", "2.25", DisplayName = "a Single counting down")]
    public async Task ALoopOfACompiledCaller_ThatWaitsInACallee_GoesOnAsItCounted(string declarations, string first, string header, string sum)
        => StringAssert.Contains(await AssertAlikeAsync(Looping(declarations, first, $"    {header}", "        u = Inner(i)", "        t = t + u", "    Next"), null),
            $"| {sum} |");

    [TestMethod]
    public async Task NestedLoopsOfACompiledCaller_ThatWaitsInACallee_GoOnAsTheyCounted()
        => StringAssert.Contains(await AssertAlikeAsync(Looping("i As Long, j As Double", "31",
            "    For i = n To 1 Step -1",
            "        For j = 1 To 2",
            "            u = Inner(i * 10 + j)",
            "            t = t + u",
            "        Next",
            "    Next"), null), "| 129 |");

    // Outer's call of Inner is a statement the interpreter runs alone - its argument is a String - and the program waits in Inner.
    private static readonly string SteppedCalling = Program(
        "Public Sub Main()",
        "    Stop",
        "    Debug.Print Outer(3)",
        "End Sub",
        "Private Function Outer(ByVal n As Long) As Long",
        "    Dim i As Long, t As Long, s As String, u As Long",
        "    For i = 1 To n",
        "        s = \"n\" & CDbl(i)",
        "        t = t + Inner(s)",
        "        u = u + i",
        "    Next",
        "    Outer = t + u",
        "End Function",
        "Private Function Inner(ByVal x As String) As Long",
        "    If x = \"n1\" Then Stop",
        "    Inner = Len(x)",
        "End Function");

    // SteppedCalling run both ways, waiting in Inner's first call: Outer compiled, the interpreter having run one statement of it alone that compiled code
    // went on after - the first s = - before the call it waits in, after which the interpreter runs the rest of the activation.
    private static async Task<string> AssertSteppedAsync(string? edited, params string[] statements)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await WaitedInAsync(SteppedCalling, edited, statements, VBExecutionEngine.Interpreter);
        var (compiled, outer) = await WaitedInWithOuterAsync(SteppedCalling, edited, statements, VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted, compiled);
        Assert.IsNotNull(outer, "Outer is compiled");
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(1L, outer.Recoveries, "Outer's recoveries");
        }

        return compiled;
    }

    // Outer's compiled call of Inner waited, and a debugger set a variable of Outer meanwhile. Compiled code reads its variables back after the call and goes
    // on with what was set, handed to no one. Under the random failed accesses the first read-back after a wait fails, whatever the seed, and Outer, which
    // reads nothing back before the call, is handed to the interpreter there, from the statement after it: what reaches that hand-over is asserted here.
    [TestMethod]
    public async Task ACompiledCallerThatWaited_ReadsBackWhatADebuggerSet_OrIsHandedOverFromAfterItsCall()
    {
        CompiledExecutionTests.RequirePayloads();
        var program = Calling(Constant, "Private Function Helper(ByVal y As Long) As Long\r\n    Helper = y\r\nEnd Function");
        var interpreted = await WaitedInAsync(program, null, ["t = 100"], VBExecutionEngine.Interpreter);
        var (compiled, outer) = await WaitedInWithOuterAsync(program, null, ["t = 100"], VBExecutionEngine.Compiled);

        Assert.AreEqual(interpreted, compiled);
        StringAssert.Contains(compiled, "| 127 |");
        Assert.IsNotNull(outer, "Outer is compiled");
        if (CompiledExecution.FailsReloadsAtRandom && !CompiledExecution.WalksBackAtRandom)
        {
            Assert.AreEqual(1L, outer.WalkBacks, "Outer's walk-backs");
            Assert.AreEqual(9, outer.LastWalkedBackAt?.Range.Start.Line, "where Outer was handed over: the statement after its call");
        }
        else if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(0L, outer.WalkBacks, "Outer's walk-backs");
        }
    }

    // the program waited while the interpreter ran the statement alone: the activation goes on with what the debugger set, as the interpreter's does.
    [TestMethod]
    public async Task AVariableOfACompiledCaller_SetWhileItWaitsInAStatementTheInterpreterRuns_IsWhatItGoesOnWith()
        => StringAssert.Contains(await AssertSteppedAsync(null, "i = 3"), "| 5 |");

    // the edit is the callee's where the program waits in it too: it goes on in the edited body.
    [TestMethod]
    public async Task ACallee_EditedWhileACompiledCallerWaitsInAStatementTheInterpreterRuns_IsCalledAsItNowIs()
        => StringAssert.Contains(await AssertSteppedAsync(SteppedCalling.Replace("    Inner = Len(x)", "    Inner = Len(x) * 10", StringComparison.Ordinal)),
            "| 66 |");
}
