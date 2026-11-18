using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Let</c> of a <c>String</c> or a <c>Debug.Print</c> that reads a variable a call in it may change (a <c>Static</c>, a variable of the module, a
/// field of a record, an element of an array, a public variable of an object, a variable it passes by reference), after every call in it, such as
/// <c>Debug.Print "a" &amp; G.Size &amp; shots</c>, is run by compiled code: MS-VBA reads the variable after the calls (RD-VBAL §5.6.9.2), and so does
/// compiled code, which reads what the calls left in it, a <c>Static</c> included, whose variable has module extent (MS-VBAL §5.4.3.1). A read left
/// of a call is the interpreter's.
/// </summary>
[TestClass]
public sealed class CompiledReadAfterCallsTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static (string Name, string Source)[] Gadget() => [("Gadget", ModuleWorkspace.ClassModule("Gadget",
        "Option Explicit",
        "Public Calls As Long",
        "Public Property Get Size() As Long",
        "    Calls = Calls + 1",
        "    Size = Calls",
        "End Property"))];

    // Outer runs the statement n times, each variable one more before it: shots its Static, j a local it passes by reference, a(1) an element of its
    // array, Total a variable of the module, this.N a field of a record; G is the object Held holds. Main calls it twice, so that the Static is what the
    // first call left it, and prints what s, the Static and the error number are then.
    private static string Looping(string statement, string handling = "") => Program(
        "Private Type THolder",
        "    N As Long",
        "End Type",
        "Private this As THolder",
        "Private Total As Long",
        "Private Held As Gadget",
        "Public Sub Main()",
        "    Set Held = New Gadget",
        "    Debug.Print Outer(3)",
        "    Debug.Print Outer(2)",
        "End Sub",
        "Private Function Outer(ByVal n As Long) As String",
        "    Static shots As Long",
        "    Dim i As Long, j As Long, s As String, a(1 To 2) As Long, G As Gadget",
        "    Set G = Held",
        $"    {handling}",
        "    For i = 1 To n",
        "        shots = shots + 1",
        "        j = i",
        "        a(1) = a(1) + 1",
        "        Total = Total + 1",
        "        this.N = this.N + 1",
        $"        {statement}",
        "    Next",
        "    Outer = s & \";\" & shots & \";\" & Err.Number",
        "End Function",
        "Private Function Twice(ByVal x As Long) As Long",
        "    Twice = x * 2",
        "End Function",
        "Private Function Bumped(ByRef x As Long) As Long",
        "    x = x + 10",
        "    Bumped = 1",
        "End Function",
        "Private Function Changed() As Long",
        "    Total = Total + 100",
        "    this.N = this.N + 100",
        "    Changed = 1",
        "End Function",
        "Private Function Fails(ByVal x As Long) As Long",
        "    If x = 2 Then Err.Raise 13",
        "    Fails = x",
        "End Function");

    private static void AssertWhole(CompilationResult compiled)
    {
        Assert.IsEmpty(compiled.Stepped, string.Join("; ", compiled.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
        Assert.IsEmpty(compiled.SupportCalls, string.Join("; ", compiled.SupportCalls.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }

    // the program run both ways, as CompiledNestedCallTests.AssertAlikeAsync runs it; and no statement of the caller is the interpreter's to run.
    private static async Task<string> AssertWholeAsync(string program, string caller, long calls, bool walksBack = false)
    {
        var observed = await CompiledNestedCallTests.AssertAlikeAsync(program, caller, calls, walksBack, Gadget());
        AssertWhole((await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Compiled, Gadget())).Compiled[caller]);
        return observed;
    }

    // the program run both ways: the compiled run is the interpreted one, and the caller's statement that reads the variable left of a call is stepped.
    private static async Task AssertSteppedAlikeAsync(string program, string caller)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Interpreter, Gadget());
        var compiled = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Compiled, Gadget());
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        Assert.IsNotNull(compiled.Compiled[caller].Body, $"{caller} was not compiled");
        Assert.IsNotEmpty(compiled.Compiled[caller].Stepped, $"no statement of {caller} was stepped");
    }

    [TestMethod]
    [DataRow("s = \"a\" & Twice(i) & \",\" & shots", 5, "| a6,3;3;0 ; a4,5;5;0 |", DisplayName = "a Static")]
    [DataRow("s = \"a\" & Changed() & \",\" & Total", 5, "| a1,303;3;0 ; a1,505;5;0 |", DisplayName = "a variable of the module, which the call changes")]
    [DataRow("s = \"a\" & Changed() & \",\" & this.N", 5, "| a1,303;3;0 ; a1,505;5;0 |", DisplayName = "a field of a record, which the call changes")]
    [DataRow("s = \"a\" & Twice(i) & \",\" & a(1)", 5, "| a6,3;3;0 ; a4,2;5;0 |", DisplayName = "an element of an array")]
    [DataRow("s = \"a\" & G.Size & \",\" & G.Calls", 5, "| a3,3;3;0 ; a5,5;5;0 |", DisplayName = "a public variable of an object, which the call changes")]
    [DataRow("s = \"a\" & Bumped(j) & \",\" & j", 5, "| a1,13;3;0 ; a1,12;5;0 |", DisplayName = "a variable passed by reference to the call")]
    [DataRow("s = Twice(i) & \"-\" & Twice(i) & \",\" & shots", 10, "| 6-6,3;3;0 ; 4-4,5;5;0 |", DisplayName = "a Static, after two calls")]
    public async Task AStringLetThatReadsAVariableRightOfEveryCall_IsCompiled(string statement, long calls, string printed)
        => StringAssert.Contains(await AssertWholeAsync(Looping(statement), "Outer", calls), printed);

    [TestMethod]
    [DataRow("Debug.Print \"P \" & Twice(i) & \"(\" & shots & \") has played \" & i & \" turns\"", 5, DisplayName = "a Static")]
    [DataRow("Debug.Print \"a\" & Changed(); \",\" & Total", 5, DisplayName = "a variable of the module, an item after the item that calls")]
    [DataRow("Debug.Print \"a\" & G.Size; \",\" & G.Calls", 5, DisplayName = "a public variable of an object, an item after the item that calls")]
    public async Task ADebugPrintThatReadsAVariableRightOfEveryCall_IsCompiled(string statement, long calls)
        => _ = await AssertWholeAsync(Looping(statement), "Outer", calls);

    // MS-VBA reads the variable when its operation is computed, after the call right of it (RD-VBAL §5.6.9.2): the interpreter reads it.
    [TestMethod]
    [DataRow("s = shots & \",\" & Twice(i)", DisplayName = "a Static")]
    [DataRow("s = Total & \",\" & Changed() & \",\" & Total", DisplayName = "a variable of the module, left of and right of the call that changes it")]
    [DataRow("s = \"a\" & this.N & \",\" & Changed()", DisplayName = "a field of a record")]
    [DataRow("s = a(1) & \",\" & Twice(i)", DisplayName = "an element of an array")]
    [DataRow("s = G.Calls & \",\" & G.Size", DisplayName = "a public variable of an object")]
    [DataRow("s = \"a\" & j & \",\" & Bumped(j)", DisplayName = "a variable passed by reference to the call")]
    [DataRow("Debug.Print this.N & \",\"; \"a\" & Changed()", DisplayName = "a field of a record, an item before the item that calls")]
    public async Task AReadLeftOfACall_IsReadAsTheInterpreterReadsIt(string statement)
        => await AssertSteppedAlikeAsync(Looping(statement), "Outer");

    // Fails raises error 13 inside the call at the second pass, left of the read: the read is not made, and the error goes where the interpreter's goes.
    [TestMethod]
    [DataRow("s = \"a\" & Fails(i) & \",\" & shots", DisplayName = "a Static")]
    [DataRow("s = \"a\" & Fails(i) & \",\" & Total", DisplayName = "a variable of the module")]
    [DataRow("s = \"a\" & Fails(i) & \",\" & this.N", DisplayName = "a field of a record")]
    [DataRow("s = \"a\" & Fails(i) & \",\" & a(1)", DisplayName = "an element of an array")]
    [DataRow("s = \"a\" & Fails(i) & \",\" & G.Calls", DisplayName = "a public variable of an object")]
    [DataRow("s = \"a\" & Fails(i) & \",\" & Bumped(j) & \",\" & j", DisplayName = "a variable passed by reference")]
    [DataRow("Debug.Print \"a\" & Fails(i) & \",\" & shots", DisplayName = "a Static, in a Debug.Print")]
    public async Task AnErrorInsideACallLeftOfTheRead_UnderResumeNext_GoesOnAsTheInterpretersDoes(string statement)
        => _ = await AssertWholeAsync(Looping(statement, "On Error Resume Next"), "Outer", statement.Contains("Bumped") ? 8 : 5);

    [TestMethod]
    [DataRow("s = \"a\" & Fails(i) & \",\" & shots", DisplayName = "a Static")]
    [DataRow("s = \"a\" & Fails(i) & \",\" & Total", DisplayName = "a variable of the module")]
    [DataRow("s = \"a\" & Fails(i) & \",\" & this.N", DisplayName = "a field of a record")]
    [DataRow("s = \"a\" & Fails(i) & \",\" & a(1)", DisplayName = "an element of an array")]
    [DataRow("s = \"a\" & Fails(i) & \",\" & G.Calls", DisplayName = "a public variable of an object")]
    [DataRow("s = \"a\" & Fails(i) & \",\" & Bumped(j) & \",\" & j", DisplayName = "a variable passed by reference")]
    [DataRow("Debug.Print \"a\" & Fails(i) & \",\" & shots", DisplayName = "a Static, in a Debug.Print")]
    public async Task AnErrorInsideACallLeftOfTheRead_UnhandledAnywhere_EndsTheProgramWhereTheInterpreterEndsIt(string statement)
        => StringAssert.Contains(await AssertWholeAsync(Looping(statement), "Outer", statement.Contains("Bumped") ? 3 : 2, walksBack: true), "| 13 ");

    // the read left of the call that fails is the interpreter's under Resume Next too.
    [TestMethod]
    [DataRow("s = shots & \",\" & Fails(i)", DisplayName = "a Static")]
    [DataRow("s = G.Calls & \",\" & Fails(i)", DisplayName = "a public variable of an object")]
    [DataRow("Debug.Print shots & \",\" & Fails(i)", DisplayName = "a Static, in a Debug.Print")]
    public async Task AnErrorInsideACallRightOfTheRead_UnderResumeNext_IsReadAsTheInterpreterReadsIt(string statement)
        => await AssertSteppedAlikeAsync(Looping(statement, "On Error Resume Next"), "Outer");

    // Deep calls itself, which adds one to its Static each time: the read right of the call is what the calls it made left, as it is in the
    // interpreter; the read left of it is the interpreter's.
    private static string Recursive(string statement, string handling = "") => Program(
        "Public Sub Main()",
        "    Debug.Print Deep(3)",
        "End Sub",
        "Private Function Deep(ByVal depth As Long) As Long",
        "    Static shots As Long",
        "    Dim s As String",
        $"    {handling}",
        "    shots = shots + 1",
        "    If depth > 0 Then",
        $"        {statement}",
        "        Debug.Print s",
        "    End If",
        "    Deep = shots",
        "End Function");

    // the program run both ways: the compiled run is the interpreted one; Deep is compiled, and none of its statements is stepped. A call of the procedure
    // itself is made through the invoker, and Err.Raise is a support call; under the random walk-back, its activation may be handed to the interpreter.
    private static async Task<string> AssertRecursiveAsync(string program, bool walksBack = false)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Interpreter);
        var compiled = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        var deep = compiled.Compiled["Deep"];
        Assert.IsNotNull(deep.Body, string.Join("; ", deep.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
        Assert.IsEmpty(deep.Stepped, string.Join("; ", deep.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
        if (walksBack)
        {
            Assert.IsGreaterThan(0L, deep.Body.WalkBacks, "Deep ran to its end in compiled code");
        }
        else if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(0L, deep.Body.WalkBacks, "Deep was handed to the interpreter");
        }

        return compiled.Observed;
    }

    [TestMethod]
    public async Task AStaticReadRightOfACallOfItsOwnProcedure_IsWhatTheCallLeftIt()
        => StringAssert.Contains(await AssertRecursiveAsync(Recursive("s = \"a\" & Deep(depth - 1) & \",\" & shots")), "| a4,4 ; a4,4 ; a4,4 ; 4 |");

    [TestMethod]
    public async Task AStaticReadLeftOfACallOfItsOwnProcedure_IsReadAsTheInterpreterReadsIt()
        => await AssertSteppedAlikeAsync(Recursive("s = shots & \",\" & Deep(depth - 1)"), "Deep");

    // the innermost call raises error 13 after it adds one to the Static: under Resume Next it goes on, and each caller reads what it left; unhandled,
    // no caller's read right of the call is made, and the program ends where the interpreter ends it.
    [TestMethod]
    [DataRow("On Error Resume Next", DisplayName = "under Resume Next")]
    [DataRow("", DisplayName = "unhandled")]
    public async Task AnErrorInsideTheCallOfItsOwnProcedure_GoesWhereTheInterpretersGoes(string handling)
    {
        var program = Recursive("s = \"a\" & Deep(depth - 1) & \",\" & shots", handling).Replace("    Deep = shots\r\n",
            "    If depth = 0 Then Err.Raise 13\r\n    Deep = shots\r\n");
        StringAssert.Contains(await AssertRecursiveAsync(program, walksBack: handling.Length == 0), handling.Length == 0 ? "| 13 " : "| a4,4 ; a4,4 ; a4,4 ; 4 |");
    }
}
