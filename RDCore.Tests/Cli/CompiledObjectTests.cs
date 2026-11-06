using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// An object parameter or variable, compiled (IR lowering S2.5e2): held in the cell its frame holds it in, whose reference compiled code reads for <c>Is</c>
/// (MS-VBAL §5.6.9.7) and <c>TypeOf</c> (MS-VBAL §5.6.7), as the interpreter compares and tests it. A <c>Set</c> is the interpreter's, and compiled code finds
/// the cell again after it. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledObjectTests
{
    private static readonly (string Name, string Source)[] Things =
    [
        ("Thing", ModuleWorkspace.ClassModule("Thing", "Option Explicit", "Public Value As Long")),
        ("Other", ModuleWorkspace.ClassModule("Other", "Option Explicit", "Public Value As Long")),
    ];

    // an object variable compared with Nothing in a loop: the Set that makes it something is the interpreter's, and compiled code reads the cell it binds.
    [TestMethod]
    public async Task AnObjectComparedWithNothing_IsComparedInCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted()",
            "End Sub",
            "Private Function Counted() As Long",
            "    Dim T As Thing",
            "    Dim I As Long",
            "    For I = 1 To 6",
            "        If T Is Nothing Then Counted = Counted + 1",
            "        If I = 3 Then Set T = New Thing",
            "    Next",
            "End Function"), "Counted", recoveries: 1, classes: Things);

    // two references to one object are the same; Nothing is Nothing; a reference compared with the variable it was copied from, after either is Set again.
    [TestMethod]
    public async Task TwoObjectVariables_AreTheSame_WhenTheyReferToOneObject()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Compared()",
            "End Sub",
            "Private Function Compared() As Long",
            "    Dim A As Thing",
            "    Dim B As Thing",
            "    Dim I As Long",
            "    For I = 1 To 6",
            "        If A Is B Then Compared = Compared + 100 Else Compared = Compared + 1",
            "        If I = 1 Then Set A = New Thing",
            "        If I = 3 Then Set B = A",
            "        If I = 5 Then Set B = New Thing",
            "    Next",
            "End Function"), "Compared", recoveries: 3, classes: Things);

    // Nothing on the left, in parentheses, under Not, and of an Object variable; Nothing Is Nothing.
    [TestMethod]
    public async Task IsNothing_IsComparedWhicheverSideNothingIsOn()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Sides()",
            "End Sub",
            "Private Function Sides() As String",
            "    Dim O As Object",
            "    Dim I As Long",
            "    Dim N As Long",
            "    For I = 1 To 4",
            "        If Nothing Is O Then N = N + 1",
            "        If Not (O Is Nothing) Then N = N + 10",
            "        If Nothing Is Nothing Then N = N + 100",
            "        If I = 2 Then Set O = New Other",
            "    Next",
            "    Sides = CStr(N)",
            "End Function"), "Sides", recoveries: 2, classes: Things);

    // an Is of an operand declared Object is planned when it is evaluated, but is a Boolean all the same: a selector compiled code compares.
    [TestMethod]
    public async Task ASelectCaseOfIsNothing_IsComparedInCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Selected()",
            "End Sub",
            "Private Function Selected() As Long",
            "    Dim O As Object",
            "    Dim I As Long",
            "    For I = 1 To 4",
            "        Select Case O Is Nothing",
            "            Case True",
            "                Selected = Selected + 1",
            "            Case Else",
            "                Selected = Selected + 10",
            "        End Select",
            "        If I = 2 Then Set O = New Thing",
            "    Next",
            "End Function"), "Selected", recoveries: 1, classes: Things);

    // TypeOf tests the class of the object a variable refers to, of a variable declared As Object, against a class it is and one it is not.
    [TestMethod]
    public async Task TypeOf_TestsTheClassOfTheObject()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Tested()",
            "End Sub",
            "Private Function Tested() As Long",
            "    Dim O As Object",
            "    Dim I As Long",
            "    For I = 1 To 6",
            "        If I Mod 2 = 1 Then Set O = New Thing Else Set O = New Other",
            "        If TypeOf O Is Thing Then Tested = Tested + 1",
            "        If TypeOf O Is Other Then Tested = Tested + 100",
            "    Next",
            "End Function"), "Tested", recoveries: 6, classes: Things);

    // TypeOf of Nothing raises error 91 (MS-VBAL §5.6.7), which the interpreter raises, and the handler is given.
    [TestMethod]
    public async Task TypeOfNothing_IsRaisedByTheInterpreter_ToTheHandler()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Raised()",
            "End Sub",
            "Private Function Raised() As Long",
            "    On Error Resume Next",
            "    Dim T As Thing",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        If TypeOf T Is Thing Then Raised = Raised + 1",
            "        Raised = Raised + Err.Number",
            "        Err.Clear",
            "    Next",
            "End Function"), "Raised", recoveries: 9, classes: Things);

    // an object parameter passed by reference is its caller's variable: compared where the caller's cell is, after the callee Sets it.
    [TestMethod]
    public async Task AnObjectParameterPassedByReference_IsComparedInItsCallersCell()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim T As Thing",
            "    Debug.Print Made(T); T Is Nothing",
            "End Sub",
            "Private Function Made(ByRef T As Thing) As Long",
            "    Dim I As Long",
            "    For I = 1 To 4",
            "        If T Is Nothing Then Made = Made + 1",
            "        If I = 2 Then Set T = New Thing",
            "    Next",
            "End Function"), "Made", recoveries: 1, classes: Things);

    // a variable declared As New is made when it is read (MS-VBAL §5.2.3.1.4): its Is Nothing is the interpreter's, which makes it, and so is never True.
    [TestMethod]
    public async Task AnAutoInstantiatedVariable_IsMadeByTheInterpreter_WhenItIsCompared()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Made()",
            "End Sub",
            "Private Function Made() As Long",
            "    Dim T As New Thing",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        If T Is Nothing Then Made = Made + 1 Else Made = Made + 10",
            "        Set T = Nothing",
            "    Next",
            "End Function"), "Made", recoveries: 6, classes: Things);

    // a Variant that holds an object is compared by the interpreter: it is no object variable.
    [TestMethod]
    public async Task AVariantHoldingAnObject_IsComparedByTheInterpreter()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Held()",
            "End Sub",
            "Private Function Held() As Long",
            "    Dim V As Variant",
            "    Dim T As Thing",
            "    Dim I As Long",
            "    Set T = New Thing",
            "    Set V = Nothing",
            "    For I = 1 To 3",
            "        If I = 2 Then Set V = T",
            "        If V Is T Then Held = Held + 1",
            "    Next",
            "End Function"), "Held", recoveries: 6, classes: Things);
}
