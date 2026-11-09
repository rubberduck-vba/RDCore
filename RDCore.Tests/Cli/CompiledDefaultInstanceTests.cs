using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A member of the default instance of a class (MS-VBAL §5.2.4.1.2) called by compiled code: a <c>Sub</c> as a statement, a <c>Property Get</c> for the
/// whole value a variable is given or as an operand, a <c>Function</c> in a condition. Compiled code calls the member on the object the session holds for
/// the default instance, as the interpreter does; a default instance that holds no object yet is the interpreter's, which makes it (MS-VBAL §5.2.3.1.4),
/// and compiled code calls the members of the object it made. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledDefaultInstanceTests
{
    // a class with a default instance, which says when an object of it is made and destroyed.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Board", ModuleWorkspace.ClassModule("Board",
            "Attribute VB_PredeclaredId = True",
            "Option Explicit",
            "Private mSize As Byte",
            "Private Sub Class_Initialize()",
            "    mSize = 3",
            "    Debug.Print \"made\"",
            "End Sub",
            "Private Sub Class_Terminate()",
            "    Debug.Print \"gone\"",
            "End Sub",
            "Public Property Get Size() As Byte",
            "    Size = mSize",
            "End Property",
            "Public Sub Grow(ByVal By As Byte)",
            "    mSize = mSize + By",
            "End Sub",
            "Public Function Fits(ByVal X As Long) As Boolean",
            "    Fits = X < mSize",
            "End Function")),
    ];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // the program run both ways, as AssertAlikeAsync runs it, with as many statements handed to the interpreter as recoveries says; and each call compiled
    // code made, made through the member's typed entry.
    private static async Task AssertDefaultAsync(string program, string compiled, long direct, long recoveries, string expected)
    {
        var result = await AssertAlikeAsync(program, compiled, recoveries, classes: Classes, expected: expected);
        CompiledDirectCallTests.AssertCalls(direct, (result.Body!.DirectCalls, result.Body.IndirectCalls));
    }

    // a Sub of the default instance called as a statement, and a Property Get of it read as an operand.
    [TestMethod]
    public async Task AMemberOfTheDefaultInstance_AsAStatement_OrAsAnOperand_IsCompiled()
        => await AssertDefaultAsync(Program(
            "Public Sub Main()",
            "    Board.Grow 1",
            "    Debug.Print Area(5)",
            "End Sub",
            "Private Function Area(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Area = Area + Board.Size * I",
            "        Board.Grow 1",
            "    Next",
            "End Function"), "Area", direct: 10, recoveries: 0, expected: "100");

    // a Function of the default instance in a condition, and a Property Get of it for the whole value a variable is given.
    [TestMethod]
    public async Task AMemberOfTheDefaultInstance_InACondition_OrForAWholeValue_IsCompiled()
        => await AssertDefaultAsync(Program(
            "Public Sub Main()",
            "    Board.Grow 2",
            "    Debug.Print Counted(8)",
            "End Sub",
            "Private Function Counted(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    Dim S As Byte",
            "    For I = 1 To N",
            "        If Board.Fits(I) Then Counted = Counted + 1",
            "    Next",
            "    S = Board.Size",
            "    Counted = Counted * 10 + S",
            "End Function"), "Counted", direct: 9, recoveries: 0, expected: "45");

    // the default instance named first in compiled code: the interpreter makes its object, once, and compiled code calls the members of the object made.
    [TestMethod]
    public async Task ADefaultInstanceThatHoldsNoObjectYet_IsMadeByTheInterpreter_ThenCalledCompiled()
        => await AssertDefaultAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Area(3)",
            "End Sub",
            "Private Function Area(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Area = Area + Board.Size * I",
            "        Board.Grow 1",
            "    Next",
            "End Function"), "Area", direct: 5, recoveries: 1, expected: "made ; 26");

    // the default instance set to Nothing in the loop: the object is destroyed, and the next call is on another the interpreter makes.
    [TestMethod]
    public async Task ADefaultInstanceSetToNothing_IsMadeAgain_OnTheNextCall()
        => await AssertDefaultAsync(Program(
            "Public Sub Main()",
            "    Board.Grow 0",
            "    Debug.Print Reset(4)",
            "End Sub",
            "Private Function Reset(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Reset = Reset * 10 + Board.Size",
            "        Board.Grow 1",
            "        If I = 2 Then Set Board = Nothing",
            "    Next",
            "End Function"), "Reset", direct: 7, recoveries: 2, expected: "gone ; made ; 3434");
}
