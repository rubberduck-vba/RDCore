using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>Let</c> of a member of an object, compiled: of an object variable, of <c>Me</c>, of the object of a <c>With</c> block (a public field assigned the
/// value let-coerced to its type, a <c>Property Let</c> called with it, MS-VBAL §5.4.3.8), as the interpreter assigns it, the value evaluated first; and a
/// member of the object of a <c>With</c> block called, the object the one the block holds (MS-VBAL §5.4.2.21), wherever the program goes in and out of the
/// block. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledMemberLetTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Noisy", ModuleWorkspace.ClassModule("Noisy", "Public Id As Long", "Private Sub Class_Terminate()", "    Log = Log & \"gone \" & Id & \";\"", "End Sub")),
        ("Point", ModuleWorkspace.ClassModule("Point",
            "Private mX As Long",
            "Public Sum As Long",
            "Public Property Get X() As Long",
            "    X = mX",
            "End Property",
            "Public Property Let X(ByVal Value As Long)",
            "    Log = Log & \"x=\" & Value & \";\"",
            "    mX = Value",
            "End Property",
            "Public Sub Add(ByVal N As Long)",
            "    Sum = Sum + N",
            "End Sub",
            "Public Function Total() As Long",
            "    Total = Sum + mX",
            "End Function",
            "Public Property Get Self() As Point",
            "    Set Self = Me",
            "End Property",
            "Public Sub Fill(ByVal N As Long)",
            "    Dim K As Long",
            "    For K = 1 To N",
            "        Me.X = K",
            "        Me.Sum = K * 2",
            "    Next",
            "End Sub")),
    ];

    // the program run both ways, printing what MS-VBA prints, each procedure named compiled with the recoveries and walk-backs it says.
    private static async Task AssertRunAsync(string program, string expected, params (string Procedure, long Recoveries, long WalkBacks)[] procedures)
    {
        var compiled = await AssertAlikeAsync(program, Classes, expected);
        foreach (var (procedure, recoveries, walkBacks) in procedures)
        {
            Assert.IsTrue(compiled.TryGetValue(procedure, out var result), $"{procedure} was not called");
            Assert.IsNotNull(result.Body, $"{procedure}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");

            // under the random walk-back every activation may be handed to the interpreter, which is what that run is for.
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(walkBacks, result.Body.WalkBacks, $"{procedure}'s walk-backs, the last at {result.Body.LastWalkedBackAt}");
                Assert.AreEqual(recoveries, result.Body.Recoveries, $"{procedure}'s recoveries");
            }
        }
    }

    // a public field of an object a variable refers to, assigned in a loop compiled code runs. The recovery is the read of the field, which is a member of
    // an object; the Set of the object made with New is a support call.
    [TestMethod]
    public async Task AFieldOfAnObject_IsLetInCompiledCode()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Debug.Print Filled()",
            "End Sub",
            "Private Function Filled() As Long",
            "    Dim P As Point",
            "    Dim K As Long",
            "    Set P = New Point",
            "    For K = 1 To 10",
            "        P.Sum = K * 3",
            "        Filled = Filled + K",
            "    Next",
            "    Filled = Filled * 1000 + P.Sum",
            "End Function"), "55030", ("Filled", 1, 0));

    // a Property Let called with the value let-coerced to the type of its parameter: a Double rounded to even, a Boolean as -1. The Set is a support call.
    [TestMethod]
    public async Task APropertyLet_IsCalledWithTheValueLetCoercedToItsParameter()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Lets",
            "    Debug.Print Log",
            "End Sub",
            "Private Sub Lets()",
            "    Dim P As Point",
            "    Dim K As Long",
            "    Dim D As Double",
            "    Set P = New Point",
            "    For K = 1 To 3",
            "        D = K + 0.5",
            "        P.X = D",
            "    Next",
            "    P.X = True",
            "End Sub"), "x=2;x=2;x=4;x=-1;", ("Lets", 0, 0));

    // a member of Me assigned in a member of the class: a Property Let and a public field.
    [TestMethod]
    public async Task AMemberOfMe_IsLetInCompiledCode()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Dim P As Point",
            "    Set P = New Point",
            "    P.Fill 3",
            "    Debug.Print Log & P.Sum",
            "End Sub"), "x=1;x=2;x=3;6", ("Fill", 0, 0));

    // in a With block, members of its object let and called, and what one of them returns assigned whole. The recoveries are the With and its End With;
    // the Set is a support call.
    [TestMethod]
    public async Task MembersOfTheObjectOfAWithBlock_AreLetAndCalledInCompiledCode()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Debug.Print Summed() & \";\" & Log",
            "End Sub",
            "Private Function Summed() As Long",
            "    Dim P As Point",
            "    Dim K As Long",
            "    Set P = New Point",
            "    With P",
            "        For K = 1 To 4",
            "            .X = K",
            "            .Add K",
            "        Next",
            "        Summed = .Total",
            "    End With",
            "End Function"), "14;x=1;x=2;x=3;x=4;", ("Summed", 2, 0));

    // the shape of a factory: a With block of an object made with New, its members let, the object given back through a member of it. The procedure is the
    // interpreter's: its With statement, its End With and its Set are, and a Let of a member, which is the interpreter's assignment, is not what compiling it
    // would gain.
    [TestMethod]
    public async Task TheMembersOfANewObjectOfAWithBlock_AreLetAsTheInterpreterLetsThem()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Dim K As Long",
            "    Dim S As String",
            "    For K = 1 To 3",
            "        S = S & Made(K, K * 10).Total & \",\"",
            "    Next",
            "    Debug.Print S & Log",
            "End Sub",
            "Private Function Made(ByVal A As Long, ByVal B As Long) As Point",
            "    With New Point",
            "        .X = A",
            "        .Sum = B",
            "        Set Made = .Self",
            "    End With",
            "End Function"), "11,22,33,x=1;x=2;x=3;");

    // the value is evaluated before the object: a call that gives it is made, then the member of Nothing is error 91, each time - whether what is assigned
    // is what the call returned or a value compiled code evaluates. The recoveries are the 4 errors, each resumed past by the interpreter, and the 4 reads
    // of Err.Number; the 4 calls of Err.Clear are support calls.
    [TestMethod]
    public async Task AMemberOfNothing_IsError91AfterTheValueIsEvaluated()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Debug.Print Tried() & \";\" & Log",
            "End Sub",
            "Private Function Tried() As Long",
            "    Dim P As Point",
            "    Dim K As Long",
            "    On Error Resume Next",
            "    For K = 1 To 2",
            "        P.X = Noted(K)",
            "        Tried = Tried + Err.Number",
            "        Err.Clear",
            "        P.Sum = K",
            "        Tried = Tried + Err.Number",
            "        Err.Clear",
            "    Next",
            "End Function",
            "Private Function Noted(ByVal K As Long) As Long",
            "    Log = Log & \"rhs \" & K & \";\"",
            "    Noted = K",
            "End Function"), "364;rhs 1;rhs 2;", ("Tried", 8, 0), ("Noted", 2, 0));

    // a call that gives the value Sets the variable the object is read from to another object: the member assigned is the new object's, as the value is
    // evaluated before the object. The recoveries are Swapped's: the 2 assignments of Log; its Set is a support call.
    [TestMethod]
    public async Task ACallThatGivesTheValueAndSetsTheObjectVariable_AssignsTheNewObject()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Current As Point",
            "Public Sub Main()",
            "    Dim Old As Point",
            "    Set Current = New Point",
            "    Set Old = Current",
            "    Assigned",
            "    Debug.Print Log & \"old \" & Old.Sum & \" new \" & Current.Sum",
            "End Sub",
            "Private Sub Assigned()",
            "    Dim K As Long",
            "    For K = 1 To 2",
            "        Current.Sum = Swapped(K)",
            "    Next",
            "End Sub",
            "Private Function Swapped(ByVal K As Long) As Long",
            "    Log = Log & \"swapped \" & K & \";\"",
            "    If K = 1 Then Set Current = New Point",
            "    Swapped = K * 100",
            "End Function"), "swapped 1;swapped 2;old 0 new 200", ("Assigned", 0, 0), ("Swapped", 2, 0));

    // a jump out of a With block, and back into it, finds the block's object: the block holds it until its End With, as in MS-VBA. A GoTo
    // out of a block is not compiled code's, so the procedure is the interpreter's; the Property Let it calls is compiled.
    [TestMethod]
    public async Task AJumpOutOfAWithBlockAndBackIn_FindsItsObject()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Jumps",
            "    Debug.Print Log",
            "End Sub",
            "Private Sub Jumps()",
            "    Dim P As Point",
            "    Dim K As Long",
            "    Set P = New Point",
            "    With P",
            "        For K = 1 To 2",
            "            .X = K",
            "        Next",
            "        GoTo Outside",
            "Inside:",
            "        .X = 9",
            "    End With",
            "    Exit Sub",
            "Outside:",
            "    Log = Log & \"out;\"",
            "    GoTo Inside",
            "End Sub"), "x=1;x=2;out;x=9;", ("Let X", 3, 0));

    // a jump into a With block whose With statement did not run: the block has no object, and a member of it is error 91, as in MS-VBA.
    // The recoveries are the GoTo, the End With, and at each pass the error, the assignment of Log and Err.Clear (inside a With block, not a support
    // call); the Set is a support call.
    [TestMethod]
    public async Task AJumpIntoAWithBlock_FindsNoObject()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Into",
            "    Debug.Print Log",
            "End Sub",
            "Private Sub Into()",
            "    Dim P As Point",
            "    Dim K As Long",
            "    On Error Resume Next",
            "    Set P = New Point",
            "    GoTo Inside",
            "    With P",
            "Inside:",
            "        For K = 1 To 2",
            "            .X = K",
            "            Log = Log & Err.Number & \";\"",
            "            Err.Clear",
            "        Next",
            "    End With",
            "End Sub"), "91;91;", ("Into", 8, 0));

    // the object of a With block is Nothing: a member of it is error 91. The recoveries are the With and its End With, and at each pass the 2 errors, the 2
    // assignments of Log and the 2 calls of Err.Clear.
    [TestMethod]
    public async Task AWithBlockOfNothing_IsError91ForEachMember()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Nothings",
            "    Debug.Print Log",
            "End Sub",
            "Private Sub Nothings()",
            "    Dim P As Point",
            "    Dim K As Long",
            "    On Error Resume Next",
            "    With P",
            "        For K = 1 To 2",
            "            .X = K",
            "            Log = Log & Err.Number & \";\"",
            "            Err.Clear",
            "            .Add K",
            "            Log = Log & Err.Number & \";\"",
            "            Err.Clear",
            "        Next",
            "    End With",
            "End Sub"), "91;91;91;91;", ("Nothings", 14, 0));

    // the object of a With block in error, which On Error Resume Next goes past: the block has no object, and a member of it is error 91. The recoveries
    // are the With and its End With, and at each pass the error, the assignment of Log and Err.Clear.
    [TestMethod]
    public async Task AWithBlockWhoseObjectIsInError_FindsNoObject()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    PastTarget",
            "    Debug.Print Log",
            "End Sub",
            "Private Sub PastTarget()",
            "    Dim K As Long",
            "    On Error Resume Next",
            "    With Broken()",
            "        For K = 1 To 2",
            "            .X = K",
            "            Log = Log & Err.Number & \";\"",
            "            Err.Clear",
            "        Next",
            "    End With",
            "End Sub",
            "Private Function Broken() As Point",
            "    Err.Raise 5",
            "End Function"), "91;91;", ("PastTarget", 8, 0));

    // an Exit Sub inside a With block of an object made with New, which only the block holds: it is destroyed as the procedure is left, before its caller
    // goes on. The recovery is the With.
    [TestMethod]
    public async Task AnExitOutOfAWithBlock_LetsGoOfItsObject()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Log = \"a;\"",
            "    Leaves",
            "    Log = Log & \"b;\"",
            "    Debug.Print Log",
            "End Sub",
            "Private Sub Leaves()",
            "    Dim K As Long",
            "    With New Noisy",
            "        For K = 1 To 3",
            "            .Id = K",
            "            If K = 2 Then Exit Sub",
            "        Next",
            "    End With",
            "End Sub"), "a;gone 2;b;", ("Leaves", 1, 0));

    // a Resume to a label past the End With of a block an error left: the block still holds its object, which is let go of as the procedure is left. The
    // recoveries are the With, Err.Raise, the 2 assignments of Log and the Resume.
    [TestMethod]
    public async Task AResumePastTheEndOfAWithBlock_HoldsItsObjectUntilTheProcedureIsLeft()
        => await AssertRunAsync(Program(
            "Public Log As String",
            "Public Sub Main()",
            "    Log = \"a;\"",
            "    Resumes",
            "    Log = Log & \"b;\"",
            "    Debug.Print Log",
            "End Sub",
            "Private Sub Resumes()",
            "    Dim K As Long",
            "    On Error GoTo Handler",
            "    With New Noisy",
            "        For K = 1 To 2",
            "            .Id = K",
            "        Next",
            "        Err.Raise 5",
            "    End With",
            "After:",
            "    Log = Log & \"after;\"",
            "    Exit Sub",
            "Handler:",
            "    Log = Log & \"handler;\"",
            "    Resume After",
            "End Sub"), "a;handler;after;gone 2;b;", ("Resumes", 5, 0));
}
