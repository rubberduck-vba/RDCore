using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledModuleFieldTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Set</c> statement, compiled: of an object variable of the activation, or the function result variable, to an object variable,
/// <c>Me</c> or <c>Nothing</c>, Set-coerced, stored and counted as the interpreter does it, so that each object's <c>Terminate</c> runs at the instruction
/// that lets go of it last, which the interpreter runs alone. Any other <c>Set</c> (of what a call, a member or <c>New</c> gives, or of a <c>WithEvents</c>
/// variable, a parameter passed by reference, an element or a member) is a support call: the interpreter runs the statement, and compiled code goes on
/// after it; one inside a <c>With</c> block the interpreter steps. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledSetTests
{
    // an object that says when it is destroyed.
    private static readonly (string Name, string Source)[] Noisy = [("Noisy", ModuleWorkspace.ClassModule("Noisy", "Option Explicit",
        "Public Name As String",
        "Public Property Get Self() As Noisy",
        "    Set Self = Me",
        "End Property",
        "Private Sub Class_Terminate()",
        "    Debug.Print \"bye \"; Name",
        "End Sub"))];

    // what compiled code Sets, it counts: the object lives for as long as anything holds it, and is destroyed when the caller lets go of it, last.
    [TestMethod]
    public async Task ASetOfAnObjectVariable_HoldsTheObject_AsTheInterpretersDoes()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As Noisy",
            "    Set A = New Noisy",
            "    A.Name = \"first\"",
            "    Passed A",
            "    Debug.Print \"back\"",
            "End Sub",
            "Private Sub Passed(ByVal Given As Noisy)",
            "    Dim Kept As Noisy, Other As Noisy",
            "    Set Kept = Given",
            "    Set Other = Kept",
            "    Set Kept = Nothing",
            "    Set Given = Other",
            "    Set Other = Nothing",
            "End Sub"), [("Passed", 0)], classes: Noisy);

    // a Set that lets go of the last reference to an object is the interpreter's, and the object's Terminate runs there: between what is printed before it
    // and after it. One that lets go of a reference something else still holds is compiled code's.
    [TestMethod]
    public async Task ASetThatLetsGoOfTheLastReference_TerminatesTheObjectThere()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Moved",
            "    Debug.Print \"back\"",
            "End Sub",
            "Private Sub Moved()",
            "    Dim Held As Noisy, Other As Noisy",
            "    Set Held = New Noisy",
            "    Held.Name = \"first\"",
            "    Set Other = New Noisy",
            "    Other.Name = \"second\"",
            "    Debug.Print \"before\"",
            "    Set Held = Other",
            "    Debug.Print \"between\"",
            "    Set Other = Nothing",
            "    Debug.Print \"after\"",
            "    Set Held = Nothing",
            "    Debug.Print \"end\"",
            "End Sub"), [("Moved", 4)], classes: Noisy);

    // the function result variable is Set as a variable is, and holds what it is Set to until the caller takes it.
    [TestMethod]
    public async Task TheFunctionResult_IsSetToAnObject_OrToMe()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As Noisy, B As Noisy",
            "    Set A = New Noisy",
            "    A.Name = \"left\"",
            "    Set B = New Noisy",
            "    B.Name = \"right\"",
            "    Debug.Print Picked(A, B, True).Name; Picked(A, B, False).Self.Name",
            "    Set A = Nothing",
            "    Debug.Print \"one\"",
            "    Set B = Picked(Nothing, B, True)",
            "    Debug.Print \"two\"; B Is Nothing",
            "End Sub",
            "Private Function Picked(ByVal Left As Noisy, ByVal Right As Noisy, ByVal First As Boolean) As Noisy",
            "    If First Then Set Picked = Left Else Set Picked = Right",
            "End Function"), [("Picked", 0), ("Self", 0)], classes: Noisy);

    // a Set in a loop, of whichever object each pass picks: compiled code's, each pass.
    [TestMethod]
    public async Task ASetInALoop_IsCompiledCodes_EachPass()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As Noisy, B As Noisy",
            "    Set A = New Noisy",
            "    A.Name = \"odd\"",
            "    Set B = New Noisy",
            "    B.Name = \"even\"",
            "    Debug.Print Last(A, B, 7).Name; Last(A, B, 10).Name",
            "End Sub",
            "Private Function Last(ByVal First As Noisy, ByVal Second As Noisy, ByVal N As Long) As Noisy",
            "    Dim I As Long, Current As Noisy",
            "    For I = 1 To N",
            "        If I Mod 2 = 0 Then Set Current = Second Else Set Current = First",
            "    Next",
            "    Set Last = Current",
            "End Function"), [("Last", 0)], classes: Noisy);

    // an object that is not of the class of the variable it is Set to does not Set-coerce (MS-VBAL §5.5.2.2): the interpreter runs the Set, and raises error
    // 13 there - to the handler, or to the caller, with the procedure on the stack. A Set whose error leaves the procedure is no recovery: compiled code
    // goes on nowhere after it. The Set of Some to a New object is a support call, no recovery either.
    [TestMethod]
    public async Task AnObjectOfAnotherClass_IsTheInterpretersTypeMismatch()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Handled",
            "    Unhandled",
            "End Sub",
            "Private Sub Handled()",
            "    On Error GoTo Failed",
            "    Dim Some As Object, Typed As Noisy",
            "    Set Some = New Collection",
            "    Set Typed = Some",
            "    Debug.Print \"not here\"",
            "    Exit Sub",
            "Failed:",
            "    Debug.Print \"handled\"; Err.Number",
            "End Sub",
            "Private Sub Unhandled()",
            "    Dim Some As Object, Typed As Noisy",
            "    Set Some = New Collection",
            "    Set Typed = Some",
            "    Debug.Print \"not here\"",
            "End Sub"), [("Handled", 2), ("Unhandled", 0)], classes: Noisy);

    // an error a Terminate leaves unhandled ends the program, whatever handler the code that let go of the object has (MS-VBA): at the Set that
    // destroys the object, and nothing after it runs, compiled or not - which the field the next run prints shows. The interpreter runs the Set alone, then
    // goes on with the activation (a walk-back), and stops the program before the next instruction.
    [TestMethod]
    public async Task AnErrorATerminateLeavesUnhandled_EndsTheProgramAtTheSet()
        => await AssertAlikeAsync(Program(
            "Private Total As Long",
            "Public Sub Main()",
            "    Debug.Print \"total\"; Total",
            "    On Error Resume Next",
            "    Raised",
            "    Debug.Print \"after the call\"",
            "End Sub",
            "Private Sub Raised()",
            "    Dim Held As Failing, Other As Failing",
            "    Set Held = New Failing",
            "    Set Other = Held",
            "    Set Held = Nothing",
            "    Set Other = Nothing",
            "    Total = Total + 1",
            "End Sub"), [("Raised", 0)], classes: [("Failing", ModuleWorkspace.ClassModule("Failing", "Option Explicit",
                "Private Sub Class_Terminate()",
                "    Debug.Print \"bye\"",
                "    Err.Raise 5",
                "End Sub"))], between: [_ => Task.CompletedTask], walkBacks: 1);

    // an activation handed to the interpreter after compiled code Set its variables lets go of what they hold as the interpreter does, when the error it
    // raises leaves the procedure.
    [TestMethod]
    public async Task AWalkBackAfterASet_LetsGoOfTheObjectsAsTheInterpreterDoes()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    On Error GoTo Failed",
            "    Overflowed",
            "    Exit Sub",
            "Failed:",
            "    Debug.Print \"caught\"; Err.Number",
            "End Sub",
            "Private Sub Overflowed()",
            "    Dim Held As Noisy, Other As Noisy, N As Integer",
            "    Set Held = New Noisy",
            "    Held.Name = \"held\"",
            "    Set Other = Held",
            "    Set Held = Nothing",
            "    N = 32000",
            "    N = N + 1000",
            "    Debug.Print \"not here\"",
            "End Sub"), [("Overflowed", 1)], classes: Noisy, walkBacks: 1);
}
