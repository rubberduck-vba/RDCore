using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// A call inside the value of a Let of a String, such as <c>S = "(" &amp; G.Size &amp; ")"</c>, is made by compiled code: what the program does is what it
/// does when the interpreter makes it, and so is what the statement goes on with after the interpreter is handed it there.
/// </summary>
[TestClass]
public sealed class CompiledNestedTextCallTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static (string Name, string Source)[] Gadget() => [("Gadget", ModuleWorkspace.ClassModule("Gadget",
        "Option Explicit",
        "Public Calls As Long",
        "Public Title As String",
        "Public Property Get Size() As Long",
        "    Calls = Calls + 1",
        "    Size = Calls",
        "End Property",
        "Public Property Get Kind() As Shape",
        "    Calls = Calls + 1",
        "    Kind = Sevens",
        "End Property",
        "Public Function Bad() As Long",
        "    Calls = Calls + 1",
        "    Bad = -1",
        "End Function",
        "Public Property Get Retitled() As Long",
        "    Calls = Calls + 1",
        "    Title = \"z\"",
        "    Retitled = Calls",
        "End Property",
        "Public Property Get Failing() As Long",
        "    Calls = Calls + 1",
        "    Err.Raise 13",
        "End Property"))];

    // Outer runs the statement three times, with G the object of a record field, and gives what s is then; Main prints it, and how many calls were made.
    private static string Looping(string type, params string[] body) => Program([
        "Public Enum Shape",
        "    Sevens = 7",
        "End Enum",
        "Private Type THolder",
        "    G As Gadget",
        "End Type",
        "Private this As THolder",
        "Public Sub Main()",
        "    Set this.G = New Gadget",
        "    this.G.Title = \"t\"",
        "    Debug.Print Outer(3)",
        "    Debug.Print this.G.Calls",
        "End Sub",
        $"Private Function Outer(ByVal n As Long) As {type}",
        "    Dim G As Gadget, i As Long, s As String",
        "    Set G = this.G",
        .. body,
        "End Function",
        "Private Function Twice(ByVal x As Long) As Long",
        "    Twice = x * 2",
        "End Function"]);

    [TestMethod]
    [DataRow("s = \"(\" & G.Size & \")\"", 3, "| (3) ; 3 |", DisplayName = "a member of an object variable")]
    [DataRow("s = \"k\" & G.Kind", 3, "| k7 ; 3 |", DisplayName = "a member of an Enum type")]
    [DataRow("s = \"a\" & this.G.Size", 3, "| a3 ; 3 |", DisplayName = "a member of an object field of a record")]
    [DataRow("s = \"a\" & Twice(i)", 3, "| a6 ; 0 |", DisplayName = "a function of a standard module")]
    [DataRow("s = i & \",\" & G.Size", 3, "| 3,3 ; 3 |", DisplayName = "a whole number read before the call")]
    [DataRow("s = G.Size & \"-\" & G.Size", 6, "| 5-6 ; 6 |", DisplayName = "two calls")]
    [DataRow("s = \"a\" & G.Size & G.Title", 3, "| a3t ; 3 |", DisplayName = "a String public variable read after the call")]
    [DataRow("s = \"a\" & G.Size & s", 3, "| a3a2a1 ; 3 |", DisplayName = "the String variable read after the call")]
    [DataRow("s = Chr$(64 + G.Size)", 3, "| C ; 3 |", DisplayName = "Chr$ of a call")]
    public async Task ACallInsideTheValueAStringIsGiven_IsCompiled(string statement, long calls, string printed)
        => StringAssert.Contains(await CompiledNestedCallTests.AssertAlikeAsync(Looping("String",
            "    For i = 1 To n",
            $"        {statement}",
            "    Next",
            "    Outer = s"), "Outer", calls, classes: Gadget()), printed);

    [TestMethod]
    public async Task ACallInsideTheValueTheStringResultIsGiven_IsCompiled()
        => StringAssert.Contains(await CompiledNestedCallTests.AssertAlikeAsync(Looping("String",
            "    For i = 1 To n",
            "        Outer = \"r\" & G.Size",
            "    Next"), "Outer", 3, classes: Gadget()), "| r3 ; 3 |");

    [TestMethod]
    [DataRow("Debug.Print \"(\" & G.Size & \")\"", "| (1) ; (2) ; (3) ; end ; 3 |", DisplayName = "an item that calls")]
    [DataRow("Debug.Print \"a\" & G.Size; G.Title", "| a1t ; a2t ; a3t ; end ; 3 |", DisplayName = "a String public variable read after the call")]
    public async Task ACallInsideAnItemOfDebugPrint_IsCompiled(string statement, string printed)
        => StringAssert.Contains(await CompiledNestedCallTests.AssertAlikeAsync(Looping("String",
            "    For i = 1 To n",
            $"        {statement}",
            "    Next",
            "    Outer = \"end\""), "Outer", 3, classes: Gadget()), printed);

    // a String is read when the operation of it is computed (RD-VBAL §5.6.9.2), after the call right of it that changes it: the interpreter reads it.
    [TestMethod]
    [DataRow("s = G.Title & G.Retitled", DisplayName = "a String public variable read left of the call")]
    [DataRow("s = \"a\" & G.Title & G.Retitled", DisplayName = "after a literal")]
    [DataRow("Debug.Print G.Title; G.Retitled & \"\"", DisplayName = "an item of Debug.Print left of an item that calls")]
    public async Task AStringReadLeftOfACallThatChangesIt_IsReadAsTheInterpreterReadsIt(string statement)
    {
        var program = Looping("String", $"    {statement}", "    Outer = s");
        var interpreted = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Interpreter, Gadget());
        var compiled = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Compiled, Gadget());
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
    }

    // an error raised inside the call goes to the handler of the caller, as the interpreter's does, in compiled code; the calls right of it are not made.
    [TestMethod]
    [DataRow("s = \"a\" & G.Size & G.Failing & G.Size", DisplayName = "a String Let")]
    [DataRow("Debug.Print \"a\" & G.Size & G.Failing & G.Size", DisplayName = "an item of Debug.Print")]
    public async Task AnErrorRaisedInsideTheCall_GoesToTheCallersHandler_TheCallsMadeOnce(string statement)
        => StringAssert.Contains(await CompiledNestedCallTests.AssertAlikeAsync(Looping("String",
            "    On Error GoTo Handler",
            $"    {statement}",
            "    Outer = s",
            "    Exit Function",
            "Handler:",
            "    Outer = \"e\" & Err.Number"), "Outer", 2, classes: Gadget()), "| e13 ; 2 |");

    [TestMethod]
    public async Task AnErrorRaisedInsideTheCall_UnhandledAnywhere_EndsTheProgramWhereTheInterpreterEndsIt()
    {
        var observed = await CompiledNestedCallTests.AssertAlikeAsync(Looping("String",
            "    s = \"a\" & G.Size & G.Failing",
            "    Outer = s"), "Outer", 2, walksBack: true, classes: Gadget());
        StringAssert.Contains(observed, "| 13 ");
    }

    // Chr$ of a code that names no character fails after the calls: the interpreter is given what they returned, and raises the error without making them
    // again.
    [TestMethod]
    public async Task AnErrorAfterTheCall_UnderResumeNext_TheCallsMadeOnce()
        => StringAssert.Contains(await CompiledNestedCallTests.AssertAlikeAsync(Looping("String",
            "    On Error Resume Next",
            "    s = \"a\" & G.Size & Chr$(G.Bad)",
            "    Outer = s & Err.Number"), "Outer", 2, classes: Gadget()), "| 5 ; 2 |");
}
