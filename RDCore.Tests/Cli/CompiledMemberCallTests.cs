using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A call of a member of an object variable declared as a class, compiled (IR lowering S2.5e3): compiled code finds the member the object's class invokes for
/// it, as the interpreter finds it (MS-VBAL §5.3.1.9), and calls it as the interpreter does, the object given as <c>Me</c>. A call on <c>Nothing</c> is the
/// interpreter's, which raises error 91. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledMemberCallTests
{
    // each row folds what each call gives into the number it prints, in order: a call made on the wrong object, of the wrong member, or with the wrong
    // argument is another number.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Tally", ModuleWorkspace.ClassModule("Tally", "Option Explicit",
            "Private mTotal As Long",
            "Public Sub Add(ByVal N As Long)",
            "    mTotal = mTotal * 3 + N",
            "End Sub",
            "Public Function Total() As Long",
            "    Total = mTotal",
            "End Function",
            "Public Function Twice(ByVal N As Long) As Long",
            "    Twice = N * 2 + mTotal",
            "End Function",
            "Public Sub Bump(ByRef N As Long)",
            "    N = N * 2 + 1",
            "End Sub")),
        ("ICounter", ModuleWorkspace.ClassModule("ICounter", "Option Explicit",
            "Public Sub Increment(ByVal By As Long)",
            "End Sub",
            "Public Function Value() As Long",
            "End Function")),
        ("ByOnes", ModuleWorkspace.ClassModule("ByOnes", "Option Explicit",
            "Implements ICounter",
            "Private mValue As Long",
            "Private Sub ICounter_Increment(ByVal By As Long)",
            "    mValue = mValue * 2 + By",
            "End Sub",
            "Private Function ICounter_Value() As Long",
            "    ICounter_Value = mValue",
            "End Function")),
        ("ByTens", ModuleWorkspace.ClassModule("ByTens", "Option Explicit",
            "Implements ICounter",
            "Private mValue As Long",
            "Private Sub ICounter_Increment(ByVal By As Long)",
            "    mValue = mValue * 3 + By * 10",
            "End Sub",
            "Private Function ICounter_Value() As Long",
            "    ICounter_Value = mValue + 7",
            "End Function")),
        ("Noisy", ModuleWorkspace.ClassModule("Noisy", "Option Explicit",
            "Public Id As Long",
            "Public Sub Touch(ByVal N As Long)",
            "    Id = Id * 10 + N",
            "End Sub",
            "Private Sub Class_Terminate()",
            "    Debug.Print \"gone\"; Id",
            "End Sub")),
    ];

    // the program run both ways, as AssertAlikeAsync runs it, and each call of a member compiled code made, made through the member's typed entry (IR
    // lowering S2.5e3b): none through its binding.
    private static async Task AssertCalledAsync(string program, string compiled, long recoveries, long direct)
    {
        var result = await AssertAlikeAsync(program, compiled, recoveries, classes: Classes);
        if (!RDCore.Runtime.Compiled.CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual((direct, 0L), (result.Body!.DirectCalls, result.Body.IndirectCalls), $"{compiled}'s calls made directly, and through a binding");
        }
    }

    // a Sub called in a loop, given an argument; a Function whose result is assigned whole. Only the Set is the interpreter's.
    [TestMethod]
    public async Task AMemberOfAnObject_IsCalledByCompiledCode()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Called()",
            "End Sub",
            "Private Function Called() As Long",
            "    Dim T As Tally",
            "    Dim I As Long",
            "    Set T = New Tally",
            "    For I = 1 To 5",
            "        T.Add I",
            "    Next",
            "    Called = T.Total()",
            "End Function"), "Called", recoveries: 1, direct: 6);

    // a Function given an argument, its result assigned to a variable, then folded.
    [TestMethod]
    public async Task AFunctionMember_ReturnsWhatItsVariableIsAssigned()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Folded()",
            "End Sub",
            "Private Function Folded() As Long",
            "    Dim T As Tally",
            "    Dim I As Long",
            "    Dim V As Long",
            "    Set T = New Tally",
            "    For I = 1 To 4",
            "        T.Add I",
            "        V = T.Twice(I)",
            "        Folded = Folded * 7 + V",
            "    Next",
            "End Function"), "Folded", recoveries: 1, direct: 8);

    // a variable passed by reference to a member is the member's parameter: what it assigns it, it assigns the variable.
    [TestMethod]
    public async Task AVariablePassedByReferenceToAMember_IsAssignedThroughIt()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Bumped()",
            "End Sub",
            "Private Function Bumped() As Long",
            "    Dim T As Tally",
            "    Dim I As Long",
            "    Dim N As Long",
            "    Set T = New Tally",
            "    For I = 1 To 5",
            "        T.Bump N",
            "        N = N + I",
            "    Next",
            "    Bumped = N",
            "End Function"), "Bumped", recoveries: 1, direct: 5);

    // two objects of one class: each call is made on the object its variable refers to, which is Set again in the loop.
    [TestMethod]
    public async Task EachCall_IsMadeOnTheObjectItsVariableRefersTo()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Twice()",
            "End Sub",
            "Private Function Twice() As Long",
            "    Dim A As Tally",
            "    Dim B As Tally",
            "    Dim T As Tally",
            "    Dim I As Long",
            "    Dim V As Long",
            "    Set A = New Tally",
            "    Set B = New Tally",
            "    For I = 1 To 6",
            "        If I Mod 2 = 0 Then Set T = A Else Set T = B",
            "        T.Add I",
            "        V = A.Total()",
            "        Twice = Twice * 5 + V",
            "    Next",
            "    V = B.Total()",
            "    Twice = Twice + V",
            "End Function"), "Twice", recoveries: 8, direct: 13);

    // a variable declared as an interface: the member called is the one the object's class implements it with, of one class then of another.
    [TestMethod]
    public async Task AMemberOfAnInterface_IsTheOneTheObjectsClassImplementsItWith()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted()",
            "End Sub",
            "Private Function Counted() As Long",
            "    Dim C As ICounter",
            "    Dim I As Long",
            "    Dim V As Long",
            "    For I = 1 To 6",
            "        If I = 1 Then Set C = New ByOnes",
            "        If I = 4 Then Set C = New ByTens",
            "        C.Increment I",
            "        V = C.Value()",
            "        Counted = Counted * 3 + V",
            "    Next",
            "End Function"), "Counted", recoveries: 2, direct: 12);

    // a call on Nothing raises error 91: the interpreter makes it, and the handler is given the error.
    [TestMethod]
    public async Task ACallOnNothing_IsMadeByTheInterpreter_WhichRaisesTheError()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Raised()",
            "End Sub",
            "Private Function Raised() As Long",
            "    On Error Resume Next",
            "    Dim T As Tally",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        T.Add I",
            "        Raised = Raised * 100 + Err.Number",
            "        Err.Clear",
            "        If I = 2 Then Set T = New Tally",
            "    Next",
            "End Function"), "Raised", recoveries: 9, direct: 1);

    // the object a member is called on lives at least as long as the call: Set to another, the one before is let go of after the call, and terminates.
    [TestMethod]
    public async Task TheObjectAMemberIsCalledOn_TerminatesWhenItsLastReferenceGoes()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Touched()",
            "End Sub",
            "Private Function Touched() As Long",
            "    Dim T As Noisy",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        Set T = New Noisy",
            "        T.Touch I",
            "        T.Touch I + 1",
            "    Next",
            "    Touched = I",
            "End Function"), "Touched", recoveries: 3, direct: 6);

    // a member of a variable declared Object is late-bound: the interpreter's.
    [TestMethod]
    public async Task AMemberOfAnObjectDeclaredObject_IsCalledByTheInterpreter()
        => await AssertCalledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Late()",
            "End Sub",
            "Private Function Late() As Long",
            "    Dim O As Object",
            "    Dim I As Long",
            "    Set O = New Tally",
            "    For I = 1 To 3",
            "        O.Add I",
            "    Next",
            "    Late = 1",
            "End Function"), "Late", recoveries: 4, direct: 0);
}
