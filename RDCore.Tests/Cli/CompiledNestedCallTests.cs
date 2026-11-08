using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Parsing;
using RDCore.Runtime.Compiled;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Cli;

/// <summary>
/// A call inside an expression - an operand, a condition, an argument of another call - is made by compiled code: what the program does is what it does
/// when the interpreter makes it, and so is what a call it is in the middle of goes on with, after the interpreter is handed the instruction there - its
/// error, a break, a variable a debugger set, an edit.
/// </summary>
[TestClass]
public sealed class CompiledNestedCallTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private sealed record class Run(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled);

    private static async Task<Run> RunAsync(string program, VBExecutionEngine engine)
    {
        var observed = string.Empty;
        IReadOnlyDictionary<string, CompilationResult> compiled = new Dictionary<string, CompilationResult>();
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], program, (provider, run, _) =>
        {
            observed = CompiledDirectCallTests.Observed(run);
            compiled = CompiledExecutionTests.ByName(provider);
            return Task.CompletedTask;
        }, engine: engine);
        return new Run(observed, compiled);
    }

    // the program run both ways: the compiled run is the interpreted one; the caller is compiled, and made its calls, all directly, in compiled code; it was
    // handed to the interpreter only where it is said to be.
    private static async Task<string> AssertAlikeAsync(string program, string caller, long calls, bool walksBack = false)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter);
        var compiled = await RunAsync(program, VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted.Observed, compiled.Observed);

        Assert.IsTrue(compiled.Compiled.TryGetValue(caller, out var result), $"{caller} was not called");
        Assert.IsNotNull(result.Body, $"{caller}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        if (walksBack)
        {
            Assert.IsGreaterThan(0L, result.Body.WalkBacks, $"{caller} ran to its end in compiled code");
        }
        else if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(0L, result.Body.WalkBacks, $"{caller} was handed to the interpreter");
            CompiledDirectCallTests.AssertCalls(calls, (result.Body.DirectCalls, result.Body.IndirectCalls));
        }

        return compiled.Observed;
    }

    private static string Twice(string type = "Long") => $"Private Function Twice(ByVal x As {type}) As {type}\r\n    Twice = x * 2\r\nEnd Function";

    [TestMethod]
    [DataRow("t = t + Twice(i) + 1", "| 120 |", DisplayName = "a variable given a call and more")]
    [DataRow("t = Twice(i) * Twice(i) - t", "| 220 |", DisplayName = "two calls of one operation")]
    [DataRow("t = t + (Twice(i) \\ 3) Mod 2", "| 3 |", DisplayName = "a call in parentheses")]
    [DataRow("t = t - -Twice(i)", "| 110 |", DisplayName = "a call negated")]
    public async Task ACallInsideTheValueAVariableIsGiven_IsCompiled(string statement, string printed)
    {
        var observed = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(10)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long",
            "    For i = 1 To n",
            $"        {statement}",
            "    Next",
            "    Outer = t",
            "End Function",
            Twice()), "Outer", statement.Split("Twice").Length - 1 == 2 ? 20 : 10);
        StringAssert.Contains(observed, printed);
    }

    // the function result variable given a call, and read with one: the result read is the one before the call.
    [TestMethod]
    public async Task ACallInsideTheValueTheFunctionResultIsGiven_IsCompiled()
        => StringAssert.Contains(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(10)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long",
            "    For i = 1 To n",
            "        Outer = Outer + Twice(i) - 1",
            "    Next",
            "End Function",
            Twice()), "Outer", 10), "| 100 |");

    [TestMethod]
    [DataRow("If Twice(i) > 10 Then t = t + i", "| 40 |", DisplayName = "an If")]
    [DataRow("If Twice(i) Mod 4 = 0 Then t = t + i Else t = t - 1", "| 25 |", DisplayName = "an If with an Else")]
    [DataRow("If Twice(i) Then t = t + 1", "| 10 |", DisplayName = "a call as the condition")]
    public async Task ACallInsideACondition_IsCompiled(string statement, string printed)
        => StringAssert.Contains(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(10)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long",
            "    For i = 1 To n",
            $"        {statement}",
            "    Next",
            "    Outer = t",
            "End Function",
            Twice()), "Outer", 10), printed);

    [TestMethod]
    [DataRow("Do While Twice(i) < 20", "Loop", 11, DisplayName = "a Do While")]
    [DataRow("Do", "Loop Until Twice(i) >= 20", 10, DisplayName = "a Loop Until")]
    [DataRow("While Twice(i) < 20", "Wend", 11, DisplayName = "a While")]
    public async Task ACallInsideTheConditionOfALoop_IsCompiled(string opener, string closer, long calls)
        => StringAssert.Contains(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(0)",
            "End Sub",
            "Private Function Outer(ByVal i As Long) As Long",
            $"    {opener}",
            "        i = i + 1",
            $"    {closer}",
            "    Outer = i",
            "End Function",
            Twice()), "Outer", calls), "| 10 |");

    // a call given the value of another: as an argument of a function whose result is assigned whole, and of a call statement - by value, and by reference.
    [TestMethod]
    [DataRow("t = t + Twice(Twice(i))", 20, "| 220 |", DisplayName = "a call of a call")]
    [DataRow("t = Twice(Twice(i) + t)", 20, "| 8144 |", DisplayName = "a call of an operation of a call")]
    [DataRow("Add t, Twice(i)", 20, "| 110 |", DisplayName = "a call statement")]
    [DataRow("Call Add(t, Twice(i) - 1)", 20, "| 100 |", DisplayName = "an explicit call statement")]
    public async Task ACallInsideAnArgument_IsCompiled(string statement, long calls, string printed)
        => StringAssert.Contains(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(10)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long",
            "    For i = 1 To n",
            $"        {statement}",
            "    Next",
            "    Outer = t",
            "End Function",
            "Private Sub Add(ByRef total As Long, ByVal x As Long)",
            "    total = total + x",
            "End Sub",
            Twice()), "Outer", calls), printed);

    // a function called by its bare name, and a function member of an object.
    [TestMethod]
    public async Task AFunctionCalledByItsName_InsideAnExpression_IsCompiled()
        => StringAssert.Contains(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(10)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long",
            "    For i = 1 To n",
            "        t = t + Seven + i",
            "    Next",
            "    Outer = t",
            "End Function",
            "Private Function Seven() As Long",
            "    Seven = 7",
            "End Function"), "Outer", 10), "| 125 |");

    // a function that calls itself inside an expression.
    [TestMethod]
    public async Task AFunctionThatCallsItselfInsideAnExpression_IsCompiled()
        => StringAssert.Contains(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Fib(15)",
            "End Sub",
            "Private Function Fib(ByVal n As Long) As Long",
            "    If n < 2 Then",
            "        Fib = n",
            "    Else",
            "        Fib = Fib(n - 1) + Fib(n - 2)",
            "    End If",
            "End Function"), "Fib", 1972), "| 610 |");

    // a callee the interpreter runs: compiled code calls it through the invoker.
    [TestMethod]
    public async Task ACallee_TheInterpreterRuns_IsCalledInsideAnExpression()
    {
        CompiledExecutionTests.RequirePayloads();
        var program = Program(
            "Public Sub Main()",
            "    Debug.Print Outer(10)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long",
            "    For i = 1 To n",
            "        t = t + Parsed(i) * 2",
            "    Next",
            "    Outer = t",
            "End Function",
            "Private Function Parsed(ByVal x As Long) As Long",
            "    Parsed = CLng(Val(CStr(x)))",
            "End Function");
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter);
        var compiled = await RunAsync(program, VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        StringAssert.Contains(compiled.Observed, "| 110 |");
        Assert.IsNull(compiled.Compiled["Parsed"].Body, "Parsed is the interpreter's");
        var outer = compiled.Compiled["Outer"].Body;
        Assert.IsNotNull(outer, string.Join("; ", compiled.Compiled["Outer"].Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual((0L, 10L, 0L), (outer.DirectCalls, outer.IndirectCalls, outer.WalkBacks));
        }
    }

    // an operation that overflows after the call: the interpreter is handed the instruction with the call made, and raises the error - the callee is not
    // called again.
    private static string Overflowing(params string[] handling) => Program([
        "Private Calls As Long",
        "Public Sub Main()",
        "    Debug.Print Outer(32000)",
        "    Debug.Print Calls",
        "End Sub",
        "Private Function Outer(ByVal k As Integer) As Integer",
        "    Dim x As Integer",
        .. handling,
        "End Function",
        "Private Function Big() As Integer",
        "    Calls = Calls + 1",
        "    Big = 1000",
        "End Function"]);

    [TestMethod]
    public async Task AnOperationThatOverflowsAfterTheCall_RaisesItsError_TheCallMadeOnce()
    {
        var observed = await AssertAlikeAsync(Overflowing("    x = Big() + k", "    Outer = x"), "Outer", 1, walksBack: true);
        StringAssert.Contains(observed, "| 6 ");
    }

    [TestMethod]
    public async Task AnOperationThatOverflowsAfterTheCall_UnderResumeNext_GoesOnAfterIt()
        => StringAssert.Contains(await AssertAlikeAsync(Overflowing(
            "    On Error Resume Next",
            "    x = 5",
            "    x = Big() + k",
            "    Outer = x"), "Outer", 1, walksBack: false), "| 5 ; 1 |");

    // Resume runs the statement again, from its start: the call is made again.
    [TestMethod]
    public async Task AnOperationThatOverflowsAfterTheCall_ResumedByAHandler_MakesTheCallAgain()
        => StringAssert.Contains(await AssertAlikeAsync(Overflowing(
            "    On Error GoTo Handler",
            "    x = Big() + k",
            "    Outer = x",
            "    Exit Function",
            "Handler:",
            "    k = 1",
            "    Resume"), "Outer", 2, walksBack: false), "| 1001 ; 2 |");

    // a variable read in an expression that also passes it by reference to a call: when it is read is the interpreter's to say, which runs the statement
    // alone, in a caller that runs compiled around it.
    [TestMethod]
    [DataRow("x = a + Bump(a)", "| 4 ; 2 |", DisplayName = "read before the call")]
    [DataRow("x = Bump(a) + a", "| 4 ; 2 |", DisplayName = "read after the call")]
    [DataRow("x = a * 10 + Bump(a) * Bump(a)", "| 16 ; 3 |", DisplayName = "read before two calls")]
    [DataRow("x = Sum2(a, Bump(a))", "| 12 ; 2 |", DisplayName = "an argument beside the call")]
    [DataRow("If a < Bump(a) Then x = 1 Else x = 2", "| 2 ; 2 |", DisplayName = "a condition")]
    public async Task AVariableReadInAnExpressionThatPassesItByReference_IsReadByTheInterpreter(string statement, string printed)
        => StringAssert.Contains(await AssertSteppedAlikeAsync(Changing("Dim a As Long, x As Long", "a = 1", statement), "a"), printed);

    // two parameters passed by reference, which may be the same variable of the caller: one read, the other passed by reference inside the same expression.
    [TestMethod]
    public async Task AParameterReadInAnExpressionThatPassesAnotherByReference_IsReadByTheInterpreter()
        => StringAssert.Contains(await AssertSteppedAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim a As Long",
            "    a = 1",
            "    Outer a, a",
            "End Sub",
            "Private Sub Outer(ByRef p As Long, ByRef q As Long)",
            "    Dim x As Long",
            "    x = p + Bump(q)",
            "    x = x * 10",
            "    Debug.Print x",
            "    Debug.Print p",
            "End Sub",
            "Private Function Bump(ByRef y As Long) As Long",
            "    y = y + 1",
            "    Bump = y",
            "End Function"), "p"), "| 40 ; 2 |");

    // a variable read beside a call given another by reference: compiled code reads it, and makes the call.
    [TestMethod]
    public async Task AVariableReadBesideACallGivenAnotherByReference_IsCompiled()
        => StringAssert.Contains(await AssertAlikeAsync(Changing("Dim a As Long, b As Long, x As Long", "a = 1 : b = 1", "x = a + Bump(b)"), "Outer", 1), "| 3 ; 1 |");

    private static async Task<string> AssertSteppedAlikeAsync(string program, string variable)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter);
        var compiled = await RunAsync(program, VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        Assert.IsTrue(compiled.Compiled.TryGetValue("Outer", out var result), "Outer was not called");
        Assert.IsNotNull(result.Body, $"Outer: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        Assert.IsTrue(result.Stepped.Any(reason => reason is { Kind: IneligibilityKind.OperandPassedByReference } && reason.Detail == variable),
            string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
        return compiled.Observed;
    }

    private static string Changing(string declared, string assigned, string statement) => Program(
        "Public Sub Main()",
        "    Outer",
        "End Sub",
        "Private Sub Outer()",
        $"    {declared}",
        $"    {assigned}",
        $"    {statement}",
        "    Debug.Print x",
        "    Debug.Print a",
        "End Sub",
        "Private Function Bump(ByRef y As Long) As Long",
        "    y = y + 1",
        "    Bump = y",
        "End Function",
        "Private Function Sum2(ByVal u As Long, ByVal w As Long) As Long",
        "    Sum2 = u * 10 + w",
        "End Function");

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

    // Main waits at its Stop; resumed, it calls Outer, which waits in a call inside an expression, of Inner; then the module is defined again as edited, or
    // a debugger runs statements in Outer's frame, and the program goes on to its end.
    private static async Task<(string Observed, CompiledBody? Outer)> WaitedInAsync(string program, string? edited, string[] statements, VBExecutionEngine engine)
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
                if (edited is not null)
                {
                    Assert.IsEmpty(await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", ModuleType.StdModule, edited)));
                }

                foreach (var statement in statements)
                {
                    var frame = provider.Execution.Stack().Frames.Single(frame => frame.Procedure == "Outer");
                    var made = await new HostDebugExecuteHandler(provider, Substitute.For<IVerboseMessageBuilder>())
                        .Handle(new HostDebugExecuteParams { FrameId = frame.Id, Json = PlatformJson.Serialize(Statement(statement)) }, CancellationToken.None)
                        .WaitAsync(Patience);
                    Assert.IsTrue(made.Success, made.Error);
                }

                var run = await Resume(provider);
                observed = $"{run.Outcome} | {string.Join(" ; ", run.Output.Select(line => line.Trim()))} | {run.ErrorNumber} {run.ErrorMessage} @{run.ErrorLine}";
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

    private static async Task<string> AssertWaitedAlikeAsync(string program, string? edited, params string[] statements)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await WaitedInAsync(program, edited, statements, VBExecutionEngine.Interpreter);
        var (observed, outer) = await WaitedInAsync(program, edited, statements, VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted.Observed, observed);
        Assert.IsNotNull(outer, "Outer runs compiled");
        Assert.IsGreaterThan(0L, outer.DirectCalls, "Outer called Inner in compiled code");
        return observed;
    }

    // Outer sums a and what Inner returns, and waits in Inner's first call, inside the sum: a was read before the call.
    private static string Summing(string sum = "t = t + a + Inner(i)", string after = "a = a * 2") => Program(
        "Public Sub Main()",
        "    Stop",
        "    Debug.Print Outer(3)",
        "End Sub",
        "Private Function Outer(ByVal n As Long) As Long",
        "    Dim i As Long, t As Long, a As Long",
        "    a = 1",
        "    For i = 1 To n",
        $"        {sum}",
        $"        {after}",
        "    Next",
        "    Outer = t",
        "End Function",
        "Private Function Inner(ByVal x As Long) As Long",
        "    If x = 1 Then Stop",
        "    Inner = x * 10",
        "End Function");

    [TestMethod]
    public async Task ACallInsideAnExpression_ThatWaits_GoesOnWithWhatWasEvaluated()
        => StringAssert.Contains(await AssertWaitedAlikeAsync(Summing(), null), "| 67 |");

    // a variable read before the call, that a debugger sets while the program waits in the callee: the sum is of what it was read as.
    [TestMethod]
    public async Task AnOperandReadBeforeTheCall_SetWhileItWaits_IsWhatItWasRead()
        => StringAssert.Contains(await AssertWaitedAlikeAsync(Summing(), null, "a = 100", "t = 1000"), "| 661 |");

    // the procedure edited while the program waits in the call: the instruction it waits in is as it was, and what follows it is as it was edited.
    [TestMethod]
    public async Task ACallInsideAnExpression_EditedAroundWhileItWaits_GoesOnInTheEditedBody()
        => StringAssert.Contains(await AssertWaitedAlikeAsync(Summing(), Summing(after: "a = a * 3")), "| 73 |");
}
