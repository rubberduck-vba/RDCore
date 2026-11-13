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
/// A call inside an expression (an operand, a condition, an argument of another call) is made by compiled code: what the program does is what it does
/// when the interpreter makes it, and so is what a call it is in the middle of goes on with, after the interpreter is handed the instruction there (its
/// error, a break, a variable a debugger set, an edit).
/// </summary>
[TestClass]
public sealed class CompiledNestedCallTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

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

    // a call given the value of another: as an argument of a function whose result is assigned whole, and of a call statement, by value and by reference.
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

    // an operation that overflows after the call: the interpreter is handed the instruction with the call made, and raises the error; the callee is not
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

    // an operand of an integral type or of Currency, in an expression that passes it by reference to a call: read where it is written, before the call left
    // of it, after the call right of it. Compiled code reads it, and makes the call.
    [TestMethod]
    [DataRow("Long", "x = a + Bump(a)", 1, "| 3 ; 2 |", DisplayName = "O01 read before the call")]
    [DataRow("Long", "x = Bump(a) + a", 1, "| 4 ; 2 |", DisplayName = "O02 read after the call")]
    [DataRow("Long", "x = a * 10 + Bump(a) * Bump(a)", 2, "| 16 ; 3 |", DisplayName = "O03 read before two calls")]
    [DataRow("Integer", "x = a + Bump(a)", 1, "| 3 ; 2 |", DisplayName = "O04 an Integer")]
    [DataRow("Long", "If a < Bump(a) Then x = 1 Else x = 2", 1, "| 1 ; 2 |", DisplayName = "O09 a condition")]
    [DataRow("Long", "x = (a) + Bump(a)", 1, "| 3 ; 2 |", DisplayName = "O11 in parentheses")]
    [DataRow("Long", "x = a + 0 + Bump(a)", 1, "| 3 ; 2 |", DisplayName = "O12 an operation of it")]
    [DataRow("Long", "x = -a + Bump(a)", 1, "| 1 ; 2 |", DisplayName = "O13 negated")]
    [DataRow("Byte", "x = a + Bump(a)", 1, "| 3 ; 2 |", DisplayName = "P01 a Byte")]
    [DataRow("Currency", "x = a + Bump(a)", 1, "| 3 ; 2 |", DisplayName = "P04 a Currency")]
    [DataRow("LongLong", "x = a + Bump(a)", 1, "| 3 ; 2 |", DisplayName = "P06 a LongLong")]
    [DataRow("Long", "x = Bump(a) + a + Bump(a)", 2, "| 7 ; 3 |", DisplayName = "P21 read between two calls")]
    public async Task AnOperandOfAnIntegralTypeOrCurrency_PassedByReferenceBesideIt_IsReadWhereItIsWritten(string type, string statement, long calls, string printed)
        => StringAssert.Contains(await AssertAlikeAsync(Changing(type, statement), "Outer", calls), printed);

    [TestMethod]
    [DataRow("x = a + Bump(a)", DisplayName = "P02 a Boolean")]
    public async Task ABooleanOperand_PassedByReferenceBesideIt_IsReadWhereItIsWritten(string statement)
        => StringAssert.Contains(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Outer",
            "End Sub",
            "Private Sub Outer()",
            "    Dim a As Boolean, x As Integer",
            "    a = False",
            $"    {statement}",
            "    Debug.Print x",
            "    Debug.Print a",
            "End Sub",
            "Private Function Bump(ByRef y As Boolean) As Boolean",
            "    y = Not y",
            "    Bump = y",
            "End Function"), "Outer", 1), "| -1 ; True |");

    // an operand of a floating-point type or a Date, in an expression that passes it by reference to a call: read when its operation is computed, after the
    // call. Compiled code leaves the statement to the interpreter.
    [TestMethod]
    [DataRow("Double", "Double", "x = a + Bump(a)", "| 4 ; 2 |", DisplayName = "O05 a Double")]
    [DataRow("Single", "Single", "x = a + Bump(a)", "| 4 ; 2 |", DisplayName = "P03 a Single")]
    [DataRow("Date", "Double", "x = a + Bump(a)", "| 4 ; 2 |", DisplayName = "P05 a Date")]
    [DataRow("Double", "Double", "x = (a) + Bump(a)", "| 4 ; 2 |", DisplayName = "P14 in parentheses")]
    [DataRow("Double", "Double", "If a < Bump(a) Then x = 1 Else x = 2", "| 2 ; 2 |", DisplayName = "P20 a condition")]
    [DataRow("Double", "Double", "x = Sum2(a, Bump(a))", "| 22 ; 2 |", DisplayName = "P23 an argument beside the call")]
    public async Task AnOperandOfAFloatingPointTypeOrDate_PassedByReferenceBesideIt_IsReadWhenItsOperationIsComputed(string type, string result, string statement,
        string printed)
        => StringAssert.Contains(await AssertSteppedAlikeAsync(Changing(type, statement, result, "CDbl(a)"), "a"), printed);

    // an operand compiled code does not read, in an expression that passes it by reference to a call: of a Variant, a String, a variable of the module, a
    // field of a record, an element of an array. Its type decides when it is read, whatever it is.
    [TestMethod]
    [DataRow("Variant", "", "Dim a As Variant, x As Variant", "a = 1", "x = a + Bump(a)", "x ;; a", "| 4 ; 2 |", DisplayName = "O06 a Variant")]
    [DataRow("Variant", "", "Dim a As Variant, x As Variant", "a = CLng(1)", "x = a + Bump(a)", "x ;; a ;; TypeName(a)", "| 4 ; 2 ; Long |",
        DisplayName = "P08 a Variant holding a Long")]
    [DataRow("Long", "", "Dim s As String, t As String", "s = \"a\"", "t = s & Ap(s)", "t ;; s", "| abab ; ab |", DisplayName = "O07 a String")]
    [DataRow("Long", "", "Dim s As String, t As String", "s = \"a\"", "t = (s) & Ap(s)", "t ;; s", "| abab ; ab |", DisplayName = "P17 a String in parentheses")]
    [DataRow("Long", "", "Dim s As String, t As String", "s = \"a\"", "t = s & \"\" & Ap(s)", "t ;; s", "| aab ; ab |", DisplayName = "P18 an operation of a String")]
    [DataRow("Long", "", "Dim s As String * 3, t As String", "s = \"a\"", "t = s & Pre(s)", "\"[\" & t & \"]\"", "| [a  ba  ] |",
        DisplayName = "P09 a fixed-length String")]
    [DataRow("Long", "Private g As Long ;; Private Function SetG() As Long ;; g = g + 1 ;; SetG = 0 ;; End Function", "Dim x As Long", "g = 1",
        "x = g + SetG()", "x ;; g", "| 1 ; 2 |", DisplayName = "O08 a Long of the module")]
    [DataRow("Long", "Private gd As Double ;; Private Function SetD() As Double ;; gd = gd + 1 ;; SetD = 0 ;; End Function", "Dim x As Double", "gd = 1",
        "x = gd + SetD()", "x ;; gd", "| 2 ; 2 |", DisplayName = "P07 a Double of the module")]
    [DataRow("Long", "Private Type TPair ;; X As Long ;; Y As Long ;; End Type", "Dim r As TPair, x As Long", "r.X = 1", "x = r.X + Bump(r.X)", "x ;; r.X",
        "| 3 ; 2 |", DisplayName = "P10 a Long field")]
    [DataRow("Long", "", "Dim v(1 To 2) As Long, x As Long", "v(1) = 1", "x = v(1) + Bump(v(1))", "x ;; v(1)", "| 3 ; 2 |", DisplayName = "P12 a Long element")]
    [DataRow("Double", "", "Dim v(1 To 2) As Double, x As Double", "v(1) = 1", "x = v(1) + Bump(v(1))", "x ;; v(1)", "| 4 ; 2 |", DisplayName = "P13 a Double element")]
    [DataRow("Long", "", "Dim v(1 To 3) As Long, i As Long, x As Long", "v(1) = 10 : v(2) = 20 : v(3) = 30 : i = 1", "x = v(i) + Bump(i)", "x ;; i",
        "| 12 ; 2 |", DisplayName = "Q10 a Long element, its index changed")]
    [DataRow("Long", "", "Dim v(1 To 3) As Long, i As Long, x As Long", "v(1) = 10 : v(2) = 20 : v(3) = 30 : i = 1", "x = v(i) + Poke(v, i)", "x ;; i ;; v(1)",
        "| 10 ; 2 ; 11 |", DisplayName = "Q11 a Long element, it and its index changed")]
    [DataRow("Double", "", "Dim v(1 To 3) As Double, i As Long, x As Double", "v(1) = 10 : v(2) = 20 : v(3) = 30 : i = 1", "x = v(i) + Poke(v, i)",
        "x ;; i ;; v(1)", "| 20 ; 2 ; 11 |", DisplayName = "Q12 a Double element, it and its index changed")]
    public async Task AnOperandCompiledCodeDoesNotRead_PassedByReferenceBesideIt_IsReadAsItsTypeHasIt(string type, string head, string declared, string assigned,
        string statement, string printed, string expected)
        => StringAssert.Contains(await AssertInterpretedAlikeAsync(Ordering(type, head, declared, assigned, statement, printed)), expected);

    // what MS-VBA computes of an operand after a call right of it, beside one it computes before.
    [TestMethod]
    [Ignore("measured in MS-VBA, rule not yet known; ticket")]
    [DataRow("Double", "", "Dim a As Double, x As Double", "a = 1", "x = a + 0 + Bump(a)", "x ;; a", "| 4 ; 2 |", DisplayName = "P15 an operation of a Double")]
    [DataRow("Double", "", "Dim a As Double, x As Double", "a = 1", "x = -a + Bump(a)", "x ;; a", "| 0 ; 2 |", DisplayName = "P16 a Double negated")]
    [DataRow("Long", "", "Dim s As String, x As Double", "s = \"1\"", "x = -s + One(s)", "x ;; s", "| 0 ; 11 |", DisplayName = "P19 a String negated")]
    [DataRow("Double", "", "Dim a As Double, x As Double", "a = 1", "x = Bump(a) + a + Bump(a)", "x ;; a", "| 8 ; 3 |",
        DisplayName = "P22 a Double read between two calls")]
    public async Task AnOperationOfAnOperand_PassedByReferenceBesideIt_IsComputedAsMSVBAComputesIt(string type, string head, string declared, string assigned,
        string statement, string printed, string expected)
        => StringAssert.Contains(await AssertInterpretedAlikeAsync(Ordering(type, head, declared, assigned, statement, printed)), expected);

    // a field of a record of a floating-point type, read beside a call given it by reference: read when the operation is computed.
    [TestMethod]
    [Ignore("measured in MS-VBA: a Double field is read when its operation is computed; the interpreter reads a field where it is written; ticket")]
    public async Task AFieldOfAFloatingPointType_PassedByReferenceBesideIt_IsReadWhenItsOperationIsComputed()
        => StringAssert.Contains(await AssertInterpretedAlikeAsync(Ordering("Double", "Private Type TReal ;; X As Double ;; Y As Double ;; End Type",
            "Dim r As TReal, x As Double", "r.X = 1", "x = r.X + Bump(r.X)", "x ;; r.X")), "| 4 ; 2 |");

    // an argument that is not a variable, beside a call given that variable by reference right of it.
    [TestMethod]
    [Ignore("measured in MS-VBA: an argument that makes no call takes its value after the arguments that do; the order arguments are evaluated in is its own ticket")]
    [DataRow("x = Sum2(a + 0, Bump(a))", DisplayName = "P24 an operation of it")]
    [DataRow("x = Sum2((a), Bump(a))", DisplayName = "P25 in parentheses")]
    [DataRow("x = Sum2(a * 1, Bump(a)) + 0", DisplayName = "Q08 an operation of it, in an operation")]
    public async Task AnArgumentReadingAVariable_BesideACallGivenItByReference_TakesItsValueAfterTheCall(string statement)
        => StringAssert.Contains(await AssertInterpretedAlikeAsync(Changing("Long", statement)), "| 22 ; 2 |");

    // what MS-VBA may compute after the call, compiled code leaves to the interpreter.
    [TestMethod]
    [DataRow("Double", "x = a + 0 + Bump(a)", DisplayName = "an operation of a Double")]
    [DataRow("Double", "x = -a + Bump(a)", DisplayName = "a Double negated")]
    [DataRow("Long", "x = Sum2(a + 0, Bump(a))", DisplayName = "an argument, an operation of it")]
    [DataRow("Long", "x = Sum2((a), Bump(a))", DisplayName = "an argument in parentheses")]
    public async Task AnOperandMSVBAMayComputeAfterTheCall_IsLeftToTheInterpreter(string type, string statement)
        => await AssertSteppedAlikeAsync(Changing(type, statement, printed: "CDbl(a)"), "a");

    // a variable given to a parameter passed by value, beside a call given it by reference: its value is the one it has when the call is made.
    [TestMethod]
    [DataRow("x = Sum2(a, Bump(a))", DisplayName = "O10 left of the call")]
    [DataRow("x = Sum2(Bump(a), a)", DisplayName = "P26 right of the call")]
    public async Task AVariableGivenByValue_BesideACallGivenItByReference_IsReadWhenTheCallIsMade(string statement)
        => StringAssert.Contains(await AssertSteppedAlikeAsync(Changing("Long", statement), "a"), "| 22 ; 2 |");

    // two parameters passed by reference, which may be the same variable of the caller: one read, the other passed by reference inside the same expression.
    // The one read is a Long, read where it is written.
    [TestMethod]
    public async Task AParameterReadInAnExpressionThatPassesAnotherByReference_IsReadWhereItIsWritten()
        => StringAssert.Contains(await AssertAlikeAsync(Program(
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
            "End Function"), "Outer", 1), "| 30 ; 2 |");

    // a variable read beside a call given another by reference: compiled code reads it, and makes the call.
    [TestMethod]
    [DataRow("Long", DisplayName = "a Long")]
    [DataRow("Double", DisplayName = "a Double")]
    public async Task AVariableReadBesideACallGivenAnotherByReference_IsCompiled(string type)
        => StringAssert.Contains(await AssertAlikeAsync(Ordering(type, "", $"Dim a As {type}, b As {type}, x As {type}", "a = 1 : b = 1", "x = a + Bump(b)",
            "x ;; a"), "Outer", 1), "| 3 ; 1 |");

    // the program run both ways, alike, whatever runs it.
    private static async Task<string> AssertInterpretedAlikeAsync(string program)
    {
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter);
        var compiled = await RunAsync(program, VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        return compiled.Observed;
    }

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

    // a statement of Outer that reads a and passes it by reference to Bump, which adds 1 to it (or to Sum2, which is given two values by value).
    private static string Changing(string type, string statement, string? result = null, string printed = "a")
        => Ordering(type, "", $"Dim a As {type}, x As {result ?? type}", "a = 1", statement, $"x ;; {printed}");

    // a program whose Outer declares, assigns, runs a statement and prints what it says (lines apart by " ;; "), the module's head first. Bump adds 1 to a
    // variable of type it is given by reference, Sum2 is given two by value; Ap appends "b" to a String, Pre prepends "b", One appends "1"; Poke adds 1 to
    // the element of an array of type it is given the index of, and 1 to the index.
    private static string Ordering(string type, string head, string declared, string assigned, string statement, string printed) => Program([
        .. head.Split(" ;; ", StringSplitOptions.RemoveEmptyEntries),
        "Public Sub Main()",
        "    Outer",
        "End Sub",
        "Private Sub Outer()",
        $"    {declared}",
        $"    {assigned}",
        $"    {statement}",
        .. printed.Split(" ;; ").Select(line => $"    Debug.Print {line}"),
        "End Sub",
        $"Private Function Bump(ByRef y As {type}) As {type}",
        "    y = y + 1",
        "    Bump = y",
        "End Function",
        $"Private Function Sum2(ByVal u As {type}, ByVal w As {type}) As {type}",
        "    Sum2 = u * 10 + w",
        "End Function",
        "Private Function Ap(ByRef y As String) As String",
        "    y = y & \"b\"",
        "    Ap = y",
        "End Function",
        "Private Function Pre(ByRef y As String) As String",
        "    y = \"b\" & y",
        "    Pre = y",
        "End Function",
        "Private Function One(ByRef y As String) As String",
        "    y = y & \"1\"",
        "    One = y",
        "End Function",
        $"Private Function Poke(ByRef w() As {type}, ByRef k As Long) As {type}",
        "    w(k) = w(k) + 1",
        "    k = k + 1",
        "    Poke = 0",
        "End Function"]);

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
                    var made = await new HostDebugExecuteHandler(provider, Substitute.For<IErrorDetailsRecorder>())
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
