using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A field of <c>Me</c> of a scalar type, compiled: held in the cell the object holds it in, found where the interpreter finds it, and
/// read and written there by compiled code as a variable held in a cell is. What the interpreter does of the field (a statement it runs alone, a member
/// called on the object, a caller outside the class) it does in that cell too. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledFieldTests
{
    private static (string Name, string Source)[] Counter(params string[] members)
        => [("Counter", ModuleWorkspace.ClassModule("Counter", ["Option Explicit", "Public Total As Long", "Private mSum As Double", "Private mSmall As Integer", .. members]))];

    [TestMethod]
    public async Task AFieldOfMe_IsReadAndWrittenInItsCell()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As Counter",
            "    Set C = New Counter",
            "    Debug.Print C.AddUpTo(3)",
            "    Debug.Print C.AddUpTo(4)",
            "End Sub"), "AddUpTo", recoveries: 0, classes: Counter(
                "Public Function AddUpTo(ByVal N As Long) As Long",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        Total = Total + I",
                "    Next",
                "    AddUpTo = Total",
                "End Function"));

    // each object holds its own fields: the cell found is the one of the object the member is called on.
    [TestMethod]
    public async Task TheFieldsOfTwoObjects_AreEachTheirOwn()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As Counter, B As Counter",
            "    Set A = New Counter",
            "    Set B = New Counter",
            "    Debug.Print A.AddUpTo(3); B.AddUpTo(4); A.AddUpTo(1); B.AddUpTo(0)",
            "End Sub"), "AddUpTo", recoveries: 0, classes: Counter(
                "Public Function AddUpTo(ByVal N As Long) As Long",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        Total = Total + I",
                "    Next",
                "    AddUpTo = Total",
                "End Function"));

    [TestMethod]
    public async Task AFieldOfAFloatingPointType_IsReadAndWrittenInItsCell()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As New Counter",
            "    Debug.Print C.Halves(5)",
            "    Debug.Print C.Halves(2)",
            "End Sub"), "Halves", recoveries: 0, classes: Counter(
                "Public Function Halves(ByVal N As Long) As Double",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        mSum = mSum + I * 0.5",
                "    Next",
                "    Halves = mSum",
                "End Function"));

    // what a caller outside the class assigns the field - the interpreter, through the object - is what compiled code reads.
    [TestMethod]
    public async Task AFieldAssignedFromOutsideTheClass_IsReadByCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As New Counter",
            "    C.Total = 10",
            "    Debug.Print C.AddUpTo(3)",
            "    Debug.Print C.Total",
            "End Sub"), "AddUpTo", recoveries: 0, classes: Counter(
                "Public Function AddUpTo(ByVal N As Long) As Long",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        Total = Total + I",
                "    Next",
                "    AddUpTo = Total",
                "End Function"));

    // a field passed by reference is passed as its cell: what the callee assigns its parameter, it assigns the field, which the object holds after the
    // call.
    [TestMethod]
    public async Task AFieldPassedByReference_IsAssignedByTheCallee()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As New Counter",
            "    Debug.Print C.Doubled(4)",
            "    Debug.Print C.Total",
            "End Sub",
            "Public Sub Twice(ByRef X As Long)",
            "    X = X * 2",
            "End Sub"), "Doubled", recoveries: 0, classes: Counter(
                "Public Function Doubled(ByVal N As Long) As Long",
                "    Dim I As Long",
                "    Total = 1",
                "    For I = 1 To N",
                "        Twice Total",
                "    Next",
                "    Doubled = Total",
                "End Function"));

    // what the field holds, read from the object by the interpreter at an instruction it runs alone, is what compiled code wrote.
    [TestMethod]
    public async Task AStatementTheInterpreterRuns_ReadsWhatCompiledCodeWroteIntoTheField()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As New Counter",
            "    C.Shown 3",
            "End Sub"), "Shown", recoveries: 3, classes: Counter(
                "Public Sub Shown(ByVal N As Long)",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        Total = Total + I",
                "        Debug.Print Total",
                "    Next",
                "End Sub"));

    // an Integer field that overflows is let-assigned nothing (MS-VBAL §5.4.3.1.1): the interpreter runs the assignment alone, and raises the error to the
    // handler; the field holds what it held before.
    [TestMethod]
    public async Task AFieldThatOverflows_IsRaisedByTheInterpreter_ToTheHandler()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As New Counter",
            "    Debug.Print C.Guarded()",
            "End Sub"), "Guarded", recoveries: 1, classes: Counter(
                "Public Function Guarded() As Long",
                "    Dim I As Long",
                "    On Error GoTo Overflowed",
                "    For I = 1 To 5",
                "        mSmall = mSmall + 10000",
                "    Next",
                "    Guarded = -1",
                "    Exit Function",
                "Overflowed:",
                "    Guarded = mSmall + I",
                "End Function"));
}
