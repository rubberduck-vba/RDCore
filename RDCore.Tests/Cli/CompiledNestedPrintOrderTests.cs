using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// The order a <c>Debug.Print</c> with a call in it writes its items in: each item before the next is evaluated (MS-VBAL §5.4.5.8), whether compiled code
/// runs the statement or the interpreter does.
/// </summary>
[TestClass]
public sealed class CompiledNestedPrintOrderTests
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
        "Public Property Get Retitled() As Long",
        "    Calls = Calls + 1",
        "    Title = Title & \"z\"",
        "    Retitled = Calls",
        "End Property",
        "Public Property Get Loud() As Long",
        "    Calls = Calls + 1",
        "    Debug.Print \"in\"",
        "    Loud = Calls",
        "End Property",
        "Public Property Get Failing() As Long",
        "    Calls = Calls + 1",
        "    Err.Raise 13",
        "End Property"))];

    // Outer runs the body and gives what it says; Main prints it, and how many calls were made.
    private static string Outer(params string[] body) => Program([
        "Public Sub Main()",
        "    Dim G As Gadget",
        "    Set G = New Gadget",
        "    G.Title = \"t\"",
        "    Debug.Print Outer(G, 3)",
        "    Debug.Print G.Calls",
        "End Sub",
        "Private Function Outer(ByVal G As Gadget, ByVal n As Long) As String",
        "    Dim i As Long",
        .. body,
        "End Function"]);

    // an item Debug.Print writes before an item that calls is written before the call, as the interpreter writes it: what the call writes comes after it,
    // and an error the call raises leaves it written.
    [TestMethod]
    [DataRow("Debug.Print \"x\"; G.Loud & \"\"", DisplayName = "a call that writes")]
    [DataRow("Debug.Print \"x\"; G.Failing & \"\"", DisplayName = "a call that raises an error")]
    [DataRow("Debug.Print \"x\"; \"y\" & G.Loud; \"w\"", DisplayName = "a call in an item between two others")]
    [DataRow("Debug.Print G.Title; G.Retitled & \"\"", DisplayName = "a String read before the call")]
    public async Task AnItemOfDebugPrintLeftOfAnItemThatCalls_IsWrittenBeforeTheCall(string statement)
    {
        var program = Outer("    On Error Resume Next", "    For i = 1 To n", $"        {statement}", "        Debug.Print", "    Next",
            "    Outer = \"e\" & Err.Number");
        var interpreted = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Interpreter, Gadget());
        var compiled = await CompiledNestedCallTests.RunAsync(program, VBExecutionEngine.Compiled, Gadget());
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
    }

    // a call in the first item comes before anything the statement writes: compiled code makes it, and writes every item after it.
    [TestMethod]
    [DataRow("Debug.Print \"(\" & G.Size & \")\"; \"x\"", "| (1)x ; (2)x ; (3)x ; end ; 3 |", DisplayName = "a call in the first item")]
    [DataRow("Debug.Print G.Loud & \"\"; \"x\"", "| in ; 1x ; in ; 2x ; in ; 3x ; end ; 3 |", DisplayName = "a call that writes, in the first item")]
    public async Task ACallInTheFirstItemOfDebugPrint_IsCompiled(string statement, string printed)
        => StringAssert.Contains(await CompiledNestedCallTests.AssertAlikeAsync(
            Outer("    For i = 1 To n", $"        {statement}", "    Next", "    Outer = \"end\""), "Outer", 3, classes: Gadget()), printed);
}
