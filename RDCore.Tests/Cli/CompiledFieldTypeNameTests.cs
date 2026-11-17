using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>TypeName</c> of an object field of a record, such as <c>TypeName(this.Strategy)</c>, is called by compiled code, in a statement that calls inside
/// an expression too: it gives the text MS-VBAL §6.1.2.7.1.12 gives, the name of the object's class or <c>Nothing</c>, as the interpreter's call does,
/// and a field left of a call that puts another object in it is read as the interpreter reads it.
/// </summary>
[TestClass]
public sealed class CompiledFieldTypeNameTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static (string Name, string Source)[] Classes() => [
        ("Gadget", ModuleWorkspace.ClassModule("Gadget",
            "Option Explicit",
            "Public Calls As Long",
            "Public Property Get Size() As Long",
            "    Calls = Calls + 1",
            "    Size = Calls",
            "End Property",
            "Public Property Get Bad() As Long",
            "    Calls = Calls + 1",
            "    Bad = -1",
            "End Property")),
        ("Widget", ModuleWorkspace.ClassModule("Widget",
            "Option Explicit"))];

    // Outer runs the body with this.G the object Main made, and gives what s is then; Main prints it, and how many calls of the object were made.
    private static string Looping(params string[] body) => Program([
        "Private Type THolder",
        "    G As Gadget",
        "    W As Widget",
        "End Type",
        "Private this As THolder",
        "Private Made As Gadget",
        "Public Sub Main()",
        "    Set Made = New Gadget",
        "    Set this.G = Made",
        "    Debug.Print Outer(3)",
        "    Debug.Print Made.Calls",
        "End Sub",
        "Private Function Outer(ByVal n As Long) As String",
        "    Dim i As Long, s As String",
        .. body,
        "End Function",
        "Private Function Twice(ByVal x As Long) As Long",
        "    Twice = x * 2",
        "End Function",
        "Private Function Replaced() As Long",
        "    Set this.G = Nothing",
        "    Replaced = 1",
        "End Function"]);

    // the program run both ways, as CompiledNestedCallTests.AssertAlikeAsync runs it; and no statement of the caller is the interpreter's to run.
    private static async Task<string> AssertWholeAsync(string program, string caller, long calls)
    {
        var observed = await CompiledNestedCallTests.AssertAlikeAsync(program, caller, calls, classes: Classes());
        var compiled = (await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Compiled, Classes())).Compiled[caller];
        Assert.IsEmpty(compiled.Stepped, string.Join("; ", compiled.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}")));
        Assert.IsEmpty(compiled.SupportCalls, string.Join("; ", compiled.SupportCalls.Select(reason => $"{reason.Kind} {reason.Detail}")));
        return observed;
    }

    private static string InLoop(string statement, string result = "s") => Looping(
        "    For i = 1 To n",
        $"        {statement}",
        "    Next",
        $"    Outer = {result}");

    [TestMethod]
    [DataRow("s = \"a\" & TypeName(this.G)", 0, "| aGadget ; 0 |", DisplayName = "of an object field")]
    [DataRow("s = TypeName(this.W) & TypeName(this.G)", 0, "| NothingGadget ; 0 |", DisplayName = "of an object field that holds Nothing")]
    [DataRow("s = \"a\" & this.G.Size & \"(\" & TypeName(this.G) & \")\"", 3, "| a3(Gadget) ; 3 |", DisplayName = "right of a member call")]
    [DataRow("s = \"a\" & Twice(i) & \"(\" & TypeName(this.G) & \")\"", 3, "| a6(Gadget) ; 0 |", DisplayName = "right of a function call")]
    [DataRow("s = Replaced() & TypeName(this.G)", 3, "| 1Nothing ; 0 |", DisplayName = "right of the call that changes it")]
    public async Task TypeNameOfAnObjectFieldInAStringLet_IsCompiled(string statement, long calls, string printed)
        => StringAssert.Contains(await AssertWholeAsync(InLoop(statement), "Outer", calls), printed);

    [TestMethod]
    [DataRow("Debug.Print \"a\" & TypeName(this.G)", 0, "| aGadget ; aGadget ; aGadget ; end ; 0 |", DisplayName = "of an object field")]
    [DataRow("Debug.Print \"P \" & this.G.Size & \"(\" & TypeName(this.G) & \") has played \" & i & \" turns\"", 3,
        "| P 1(Gadget) has played 1 turns ; P 2(Gadget) has played 2 turns ; P 3(Gadget) has played 3 turns ; end ; 3 |",
        DisplayName = "right of a member call, and a whole number after it")]
    public async Task TypeNameOfAnObjectFieldInAnItemOfDebugPrint_IsCompiled(string statement, long calls, string printed)
        => StringAssert.Contains(await AssertWholeAsync(InLoop(statement, "\"end\""), "Outer", calls), printed);

    // the field is read as TypeName is called, before the call right of it puts Nothing in it: the interpreter reads it.
    [TestMethod]
    [DataRow("s = TypeName(this.G) & Replaced()", DisplayName = "a String Let")]
    [DataRow("Debug.Print TypeName(this.G); Replaced() & \"\"", DisplayName = "an item of Debug.Print left of an item that calls")]
    public async Task TypeNameOfAnObjectFieldLeftOfACallThatChangesIt_IsReadAsTheInterpreterReadsIt(string statement)
    {
        var program = Looping($"    {statement}", "    Outer = s");
        var interpreted = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Interpreter, Classes());
        var compiled = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Compiled, Classes());
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        StringAssert.Contains(compiled.Observed, "Gadget");
    }

    // Chr$ of a code that names no character fails after the calls: the interpreter is given what they returned, and raises the error without making them
    // again.
    [TestMethod]
    public async Task AnErrorAfterTheCalls_UnderResumeNext_TheCallsMadeOnce()
        => StringAssert.Contains(await AssertWholeAsync(Looping(
            "    On Error Resume Next",
            "    s = \"a\" & this.G.Size & Chr$(this.G.Bad) & TypeName(this.G)",
            "    Outer = s & Err.Number"), "Outer", 2), "| 5 ; 2 |");
}
