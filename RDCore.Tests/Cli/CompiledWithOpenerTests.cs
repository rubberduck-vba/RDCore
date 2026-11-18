using RDCore.Runtime.Compiled;
using RDCore.Runtime.LanguageCore.Instructions;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The <c>With</c> statement of a block of an object (<strong>MS-VBAL §5.4.2.21</strong>), run by compiled code: the object of an object variable, of
/// <c>New</c> or of a call that returns one is held by the block, as the interpreter holds it, until its <c>End With</c>; a <c>Set</c> of the variable
/// inside the block leaves the block's object as it was. A program runs as it does in the interpreter, the <c>Terminate</c> of the block's object included.
/// </summary>
[TestClass]
public sealed class CompiledWithOpenerTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Item", ModuleWorkspace.ClassModule("Item",
            "Private mN As Long",
            "Private mChild As Item",
            "Public Property Get N() As Long",
            "    N = mN",
            "End Property",
            "Public Property Let N(ByVal Value As Long)",
            "    mN = Value",
            "End Property",
            "Public Property Get Child() As Item",
            "    If mChild Is Nothing Then",
            "        Set mChild = New Item",
            "        mChild.N = mN * 10",
            "    End If",
            "    Set Child = mChild",
            "End Property",
            "Private Sub Class_Terminate()",
            "    Log = Log & \"gone \" & mN & \";\"",
            "End Sub")),
    ];

    private static string Program(params string[] lines) => string.Join("\r\n", ["Public Log As String", .. lines]) + "\r\n";

    // the program run both ways, printing expected, with as many instructions handed to the interpreter as recoveries says; with the With statements of
    // the procedure compiled code's when opened is true, and the interpreter's when not.
    private static async Task AssertWithAsync(string program, string procedure, string expected, long recoveries, bool opened = true)
    {
        var result = await AssertAlikeAsync(program, procedure, recoveries, classes: Classes, expected: expected);
        var stepped = result.Stepped.Any(reason => reason is { Kind: IneligibilityKind.Instruction, Detail: nameof(InstructionKind.With) });
        Assert.AreEqual(!opened, stepped, $"{procedure}: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
    }

    // a With block of an object made with New: compiled code opens it, and the End With that lets go of the only reference to the object is the
    // interpreter's, where its Terminate runs, before the statement after the block. The recoveries are the End With and the Let of Log, a String of the
    // module, which the interpreter assigns.
    [TestMethod]
    public async Task AWithOfNew_IsOpenedByCompiledCode_AndTerminateRunsAtItsEndWith()
        => await AssertWithAsync(Program(
            "Public Sub Main()",
            "    Made",
            "    Debug.Print Log",
            "End Sub",
            "Private Sub Made()",
            "    With New Item",
            "        .N = 1",
            "    End With",
            "    Log = Log & \"after;\"",
            "End Sub"), "Made", "gone 1;after;", recoveries: 2);

    // a Set of the variable a With block was opened of, inside the block, to another object and then to Nothing: the block's object is the one the
    // variable held at the With (MS-VBAL §5.4.2.21, as MS-VBA holds it), which only the block holds after the Sets, until its End With destroys it. The
    // recovery is that End With.
    [TestMethod]
    public async Task ASetOfTheVariableInsideTheBlock_LeavesTheBlocksObject()
        => await AssertWithAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Kept() & \" \" & Log",
            "End Sub",
            "Private Function Kept() As Long",
            "    Dim P As Item",
            "    Dim Q As Item",
            "    Set P = New Item",
            "    P.N = 1",
            "    Set Q = New Item",
            "    Q.N = 2",
            "    With P",
            "        Set P = Q",
            "        Kept = .N * 10",
            "        Set P = Nothing",
            "        Kept = Kept + .N",
            "    End With",
            "    Kept = Kept * 10 + Q.N",
            "End Function"), "Kept", "112 gone 1;gone 2;", recoveries: 1);

    // a With block of an object variable that holds Nothing: a member of it is error 91, which the interpreter raises, and the handler outside the block
    // handles. The recovery is the error.
    [TestMethod]
    public async Task AWithOfNothing_IsOpened_AndAMemberOfItIsError91()
        => await AssertWithAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Missing()",
            "End Sub",
            "Private Function Missing() As Long",
            "    On Error GoTo Failed",
            "    Dim P As Item",
            "    With P",
            "        Missing = .N",
            "    End With",
            "    Exit Function",
            "Failed:",
            "    Missing = Err.Number",
            "End Function"), "Missing", "91", recoveries: 1);

    // a With block inside another, of what a Property Get of the outer block's object returns: compiled code opens both, the inner block holding the child,
    // and the outer block's object found again after the inner End With. The recovery is the Let of the inner block's member, which the analysis binds
    // late.
    [TestMethod]
    public async Task ANestedWith_OfWhatAMemberOfTheOuterObjectReturns_IsOpenedByCompiledCode()
        => await AssertWithAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Nested() & \" \" & Log",
            "End Sub",
            "Private Function Nested() As Long",
            "    Dim P As Item",
            "    Set P = New Item",
            "    P.N = 3",
            "    With P",
            "        With .Child",
            "            Nested = .N",
            "        End With",
            "        Nested = Nested + .N",
            "    End With",
            "End Function"), "Nested", "33 gone 3;gone 30;", recoveries: 1);

    // an Exit Function from inside a With block of an object made with New: the block holds it until the activation lets go of what it holds, as it
    // returns, where its Terminate runs, before the caller goes on. Nothing is handed to the interpreter.
    [TestMethod]
    public async Task AnExitFromTheBlock_LetsGoOfTheBlocksObject_AsTheFunctionReturns()
        => await AssertWithAsync(Program(
            "Public Sub Main()",
            "    Dim K As Long",
            "    K = Quit()",
            "    Debug.Print K & \" \" & Log",
            "End Sub",
            "Private Function Quit() As Long",
            "    With New Item",
            "        .N = 5",
            "        Quit = .N",
            "        If Quit = 5 Then Exit Function",
            "        Quit = 0",
            "    End With",
            "End Function"), "Quit", "5 gone 5;", recoveries: 0);

    // an error inside a With block, handled outside it: the block still holds its object while the handler runs (MS-VBA), and lets go of it as the
    // function returns. The recoveries are the error and the Let of Log.
    [TestMethod]
    public async Task AnErrorJumpOutOfTheBlock_KeepsTheBlocksObject_UntilTheFunctionReturns()
        => await AssertWithAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Handled() & \" \" & Log",
            "End Sub",
            "Private Function Handled() As Long",
            "    On Error GoTo Failed",
            "    With New Item",
            "        .N = 6",
            "        Err.Raise 5",
            "    End With",
            "    Exit Function",
            "Failed:",
            "    Log = Log & \"handled \" & Err.Number & \";\"",
            "    Handled = 1",
            "End Function"), "Handled", "1 handled 5;gone 6;", recoveries: 2);

    // an instruction inside a With block handed to the interpreter - a division by zero, resumed past by the handler outside the block - and compiled code
    // going on after it inside the block, which finds the block's object the With statement compiled code ran gave it. The recoveries are the error, the
    // Let of Log and the Resume.
    [TestMethod]
    public async Task AHandOverInsideTheBlock_FindsTheBlocksObject()
        => await AssertWithAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Resumed() & \" \" & Log",
            "End Sub",
            "Private Function Resumed() As Long",
            "    On Error GoTo Failed",
            "    Dim P As Item",
            "    Dim Z As Long",
            "    Set P = New Item",
            "    With P",
            "        .N = 7",
            "        .N = 1 / Z",
            "        Resumed = .N",
            "    End With",
            "    Exit Function",
            "Failed:",
            "    Log = Log & \"e\" & Err.Number & \";\"",
            "    Resume Next",
            "End Function"), "Resumed", "7 e11;gone 7;", recoveries: 3);

    // a With block of a Variant that holds an object: the interpreter's statement, and so are the Let and the read of the member of its object.
    [TestMethod]
    public async Task AWithOfAVariant_IsTheInterpreters()
        => await AssertWithAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Held() & \" \" & Log",
            "End Sub",
            "Private Function Held() As Long",
            "    Dim V As Variant",
            "    Set V = New Item",
            "    With V",
            "        .N = 8",
            "        Held = .N",
            "    End With",
            "End Function"), "Held", "8 gone 8;", recoveries: 3, opened: false);
}
