using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>TypeName</c> inside a statement that calls inside an expression, such as <c>Debug.Print "a" &amp; G.Size &amp; "(" &amp; TypeName(G) &amp; ")"</c>,
/// is called by compiled code, of an object variable, of <c>Me</c>, of <c>Nothing</c> and of a value: it gives the text MS-VBAL §6.1.2.7.1.12 gives,
/// as the interpreter's call does, and an object variable left of a call that changes it is read as the interpreter reads it.
/// </summary>
[TestClass]
public sealed class CompiledNestedTypeNameTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static (string Name, string Source)[] Gadget() => [("Gadget", ModuleWorkspace.ClassModule("Gadget",
        "Option Explicit",
        "Public Calls As Long",
        "Public Property Get Size() As Long",
        "    Calls = Calls + 1",
        "    Size = Calls",
        "End Property",
        "Public Function Bad() As Long",
        "    Calls = Calls + 1",
        "    Bad = -1",
        "End Function",
        "Public Property Get Failing() As Long",
        "    Calls = Calls + 1",
        "    Err.Raise 13",
        "End Property",
        "Public Function Named(ByVal n As Long) As String",
        "    Dim i As Long",
        "    For i = 1 To n",
        "        Named = Size & \"(\" & TypeName(Me) & \")\"",
        "    Next",
        "End Function"))];

    // Outer runs the body with G the object Held holds, and gives what s is then; Main prints it, and how many calls of the object were made.
    private static string Looping(string[] declared, params string[] body) => Program([
        "Private Held As Gadget",
        "Public Sub Main()",
        "    Set Held = New Gadget",
        "    Debug.Print Outer(3)",
        "    Debug.Print Held.Calls",
        "End Sub",
        "Private Function Outer(ByVal n As Long) As String",
        "    Dim G As Gadget, i As Long, s As String",
        .. declared,
        "    Set G = Held",
        .. body,
        "End Function",
        "Private Function Twice(ByVal x As Long) As Long",
        "    Twice = x * 2",
        "End Function",
        "Private Function Dropped(ByRef g As Gadget) As Long",
        "    Set g = Nothing",
        "    Dropped = 1",
        "End Function"]);

    private static string InLoop(string statement, string result = "s") => Looping([],
        "    For i = 1 To n",
        $"        {statement}",
        "    Next",
        $"    Outer = {result}");

    [TestMethod]
    [DataRow("s = \"a\" & G.Size & \"(\" & TypeName(G) & \")\"", 3, "| a3(Gadget) ; 3 |", DisplayName = "of an object variable, right of a member call")]
    [DataRow("s = \"a\" & Twice(i) & \"(\" & TypeName(G) & \")\"", 3, "| a6(Gadget) ; 0 |", DisplayName = "of an object variable, right of a function call")]
    [DataRow("s = \"a\" & Twice(i) & TypeName(Nothing)", 3, "| a6Nothing ; 0 |", DisplayName = "of Nothing")]
    [DataRow("s = TypeName(Nothing) & Twice(i)", 3, "| Nothing6 ; 0 |", DisplayName = "of Nothing, left of a call")]
    [DataRow("s = \"a\" & Twice(i) & \"(\" & TypeName(i) & \")\"", 3, "| a6(Long) ; 0 |", DisplayName = "of a value")]
    [DataRow("s = TypeName(i) & Twice(i)", 3, "| Long6 ; 0 |", DisplayName = "of a value, left of a call")]
    [DataRow("s = \"a\" & TypeName(Twice(i))", 3, "| aLong ; 0 |", DisplayName = "of what a call returns")]
    public async Task TypeNameInsideTheValueOfAStringLet_IsCompiled(string statement, long calls, string printed)
        => StringAssert.Contains(await CompiledNestedCallTests.AssertAlikeAsync(InLoop(statement), "Outer", calls, classes: Gadget()), printed);

    [TestMethod]
    [DataRow("Debug.Print \"a\" & G.Size & \"(\" & TypeName(G) & \")\"", 3, "| a1(Gadget) ; a2(Gadget) ; a3(Gadget) ; end ; 3 |",
        DisplayName = "of an object variable")]
    [DataRow("Debug.Print \"a\" & Twice(i) & \"(\" & TypeName(i) & \")\"", 3, "| a2(Long) ; a4(Long) ; a6(Long) ; end ; 0 |", DisplayName = "of a value")]
    [DataRow("Debug.Print \"a\" & Twice(i); TypeName(Nothing)", 3, "| a2Nothing ; a4Nothing ; a6Nothing ; end ; 0 |", DisplayName = "of Nothing")]
    public async Task TypeNameInsideAnItemOfDebugPrint_IsCompiled(string statement, long calls, string printed)
        => StringAssert.Contains(await CompiledNestedCallTests.AssertAlikeAsync(InLoop(statement, "\"end\""), "Outer", calls, classes: Gadget()), printed);

    // the text of each type of value MS-VBAL §6.1.2.7.1.12 names, of a variable declared of that type.
    [TestMethod]
    [DataRow("Byte", "Byte")]
    [DataRow("Integer", "Integer")]
    [DataRow("Long", "Long")]
    [DataRow("Single", "Single")]
    [DataRow("Double", "Double")]
    [DataRow("Currency", "Currency")]
    [DataRow("Date", "Date")]
    [DataRow("Boolean", "Boolean")]
    public async Task TypeNameOfAValue_InsideAStatementThatCalls_NamesItsType(string type, string text)
        => StringAssert.Contains(await CompiledNestedCallTests.AssertAlikeAsync(Looping([$"    Dim v As {type}"],
            "    For i = 1 To n",
            "        s = \"a\" & Twice(i) & TypeName(v)",
            "    Next",
            "    Outer = s"), "Outer", 3, classes: Gadget()), $"| a6{text} ; 0 |");

    [TestMethod]
    public async Task TypeNameOfMe_RightOfACall_IsCompiled()
        => StringAssert.Contains(await CompiledNestedCallTests.AssertAlikeAsync(Program(
            "Private Held As Gadget",
            "Public Sub Main()",
            "    Set Held = New Gadget",
            "    Debug.Print Held.Named(3)",
            "    Debug.Print Held.Calls",
            "End Sub"), "Named", 3, classes: Gadget()), "| 3(Gadget) ; 3 |");

    // a call given the variable by reference, which the invoker makes, puts Nothing in it: TypeName, right of the call, reads it after the call, compiled.
    [TestMethod]
    public async Task TypeNameOfAnObjectVariableRightOfACallThatChangesIt_IsCompiled()
    {
        var program = InLoop("s = Dropped(G) & TypeName(G)");
        var interpreted = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Interpreter, Gadget());
        var compiled = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Compiled, Gadget());
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        StringAssert.Contains(compiled.Observed, "| 1Nothing ; 0 |");
        Assert.IsTrue(compiled.Compiled.TryGetValue("Outer", out var outer));
        Assert.IsNotNull(outer.Body, string.Join("; ", outer.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(0L, outer.Body.WalkBacks, "Outer was handed to the interpreter");
        }
    }

    // the object is read as TypeName is called, before the call right of it changes the variable: the interpreter reads it.
    [TestMethod]
    [DataRow("s = TypeName(G) & Dropped(G)", DisplayName = "a String Let")]
    [DataRow("Debug.Print TypeName(G); Dropped(G) & \"\"", DisplayName = "an item of Debug.Print left of an item that calls")]
    public async Task TypeNameOfAnObjectVariableLeftOfACallThatChangesIt_IsReadAsTheInterpreterReadsIt(string statement)
    {
        var program = Looping([], $"    {statement}", "    Outer = s");
        var interpreted = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Interpreter, Gadget());
        var compiled = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Compiled, Gadget());
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        StringAssert.Contains(compiled.Observed, "Gadget");
    }

    // Chr$ of a code that names no character fails after the calls: the interpreter is given what they returned and the operands as they were read, calls
    // TypeName again of them, and raises the error without making the calls again.
    [TestMethod]
    [DataRow("s = \"a\" & G.Size & TypeName(i) & Chr$(G.Bad)", DisplayName = "TypeName of a value, between the calls")]
    [DataRow("s = \"a\" & G.Size & TypeName(Nothing) & Chr$(G.Bad)", DisplayName = "TypeName of Nothing, between the calls")]
    [DataRow("s = \"a\" & G.Size & Chr$(G.Bad) & TypeName(G)", DisplayName = "TypeName of an object variable, after the calls")]
    public async Task AnErrorAfterTheCalls_UnderResumeNext_TheCallsMadeOnce(string statement)
        => StringAssert.Contains(await CompiledNestedCallTests.AssertAlikeAsync(Looping([],
            "    On Error Resume Next",
            $"    {statement}",
            "    Outer = s & Err.Number"), "Outer", 2, classes: Gadget()), "| 5 ; 2 |");

    [TestMethod]
    public async Task AnErrorRaisedInsideACallRightOfTypeName_UnhandledAnywhere_EndsTheProgramWhereTheInterpreterEndsIt()
        => StringAssert.Contains(await CompiledNestedCallTests.AssertAlikeAsync(Looping([],
            "    s = \"a\" & G.Size & TypeName(i) & G.Failing",
            "    Outer = s"), "Outer", 2, walksBack: true, classes: Gadget()), "| 13 ");

    // an error raised inside a call left of TypeName goes to the handler of the caller, as the interpreter's does; TypeName is not called.
    [TestMethod]
    public async Task AnErrorRaisedInsideACallLeftOfTypeName_GoesToTheCallersHandler()
        => StringAssert.Contains(await CompiledNestedCallTests.AssertAlikeAsync(Looping([],
            "    On Error GoTo Handler",
            "    s = \"a\" & G.Size & G.Failing & TypeName(G)",
            "    Outer = s",
            "    Exit Function",
            "Handler:",
            "    Outer = \"e\" & Err.Number"), "Outer", 2, classes: Gadget()), "| e13 ; 2 |");
}
