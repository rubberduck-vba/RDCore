using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// Member reads and calls compiled: a field of a record, and an element of an array, read inside an expression that calls (<strong>MS-VBAL §5.6.9</strong>,
/// <strong>§5.6.13</strong>), a field of a record assigned
/// what a call returns (<strong>MS-VBAL §5.4.3.8</strong>), a call that leaves out an optional parameter (<strong>MS-VBAL §5.3.1.11</strong>), and a member
/// called on the object a bare <c>Property Get</c> of the class returns. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledMembersLeftTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("IPoint", ModuleWorkspace.ClassModule("IPoint", "Option Explicit",
            "Public Property Get X() As Long",
            "End Property",
            "Public Property Get Y() As Long",
            "End Property")),
        ("Coord", ModuleWorkspace.ClassModule("Coord", "Option Explicit",
            "Implements IPoint",
            "Private Type TCoord",
            "    X As Long",
            "    Y As Long",
            "End Type",
            "Private this As TCoord",
            "Public Property Get X() As Long",
            "    X = this.X",
            "End Property",
            "Public Property Let X(ByVal Value As Long)",
            "    this.X = Value",
            "End Property",
            "Public Property Get Y() As Long",
            "    Y = this.Y",
            "End Property",
            "Public Property Let Y(ByVal Value As Long)",
            "    this.Y = Value",
            "End Property",
            "Private Property Get IPoint_X() As Long",
            "    IPoint_X = this.X",
            "End Property",
            "Private Property Get IPoint_Y() As Long",
            "    IPoint_Y = this.Y",
            "End Property",
            "Public Function Equals(ByVal Other As Coord) As Boolean",
            "    Equals = Other.X = this.X And Other.Y = this.Y",
            "End Function",
            "Public Function Adjacent(ByVal Other As IPoint) As Boolean",
            "    If Other.Y = this.Y Then",
            "        Adjacent = Other.X = this.X - 1 Or Other.X = this.X + 1",
            "    ElseIf Other.X = this.X Then",
            "        Adjacent = Other.Y = this.Y - 1 Or Other.Y = this.Y + 1",
            "    End If",
            "End Function",
            "Public Function Bump() As Long",
            "    this.X = this.X + 1",
            "    Bump = this.X",
            "End Function",
            "Public Function Order() As Long",
            "    Order = this.X * 100 + Bump * 10 + this.X",
            "End Function")),
        ("IRand", ModuleWorkspace.ClassModule("IRand", "Option Explicit",
            "Public Function Maybe(ByVal Likelihood As Long) As Boolean",
            "End Function")),
        ("Rand", ModuleWorkspace.ClassModule("Rand", "Option Explicit",
            "Implements IRand",
            "Private Function IRand_Maybe(ByVal Likelihood As Long) As Boolean",
            "    IRand_Maybe = Likelihood > 3",
            "End Function")),
        ("Grid", ModuleWorkspace.ClassModule("Grid",
            "Attribute VB_PredeclaredId = True",
            "Option Explicit",
            "Private Type TGrid",
            "    Random As IRand",
            "    Count As Byte",
            "    Total As Long",
            "End Type",
            "Private this As TGrid",
            "Public Property Get Random() As IRand",
            "    Set Random = this.Random",
            "End Property",
            "Public Property Set Random(ByVal Value As IRand)",
            "    Set this.Random = Value",
            "End Property",
            "Public Property Get PerGrid() As Byte",
            "    PerGrid = 5",
            "End Property",
            "Public Function Tries(ByVal N As Long) As Long",
            "    Do",
            "        N = N + 1",
            "    Loop Until N > 10 Or Random.Maybe(N)",
            "    Tries = N",
            "End Function",
            "Public Function Counted(ByVal Other As Grid) As Long",
            "    this.Count = PerGrid",
            "    this.Total = this.Count",
            "    this.Count = Other.PerGrid",
            "    this.Total = this.Total + this.Count",
            "    this.Count = Grid.PerGrid",
            "    Dim Local As TGrid",
            "    Local.Total = PerGrid",
            "    Counted = this.Total + this.Count + Local.Total",
            "End Function",
            "Public Function TryHit(ByVal Position As Coord, Optional ByRef Hit As Coord) As Long",
            "    TryHit = Position.X * 10",
            "    If Hit Is Nothing Then TryHit = TryHit + 1",
            "End Function",
            "Public Function Score(ByVal N As Long, Optional ByVal Bonus As Long = 7, Optional ByRef Extra As Integer) As Long",
            "    Score = N + Bonus + Extra",
            "    Extra = 5",
            "End Function",
            "Public Function Hits(ByVal Position As Coord) As Long",
            "    If TryHit(Position) <> 3 Then",
            "        Hits = TryHit(Position) + Score(1) + Score(1, 2)",
            "    End If",
            "    Hits = Hits * 1000 + Me.Score(2)",
            "End Function")),
        ("Board", ModuleWorkspace.ClassModule("Board", "Option Explicit",
            "Private Type TBoard",
            "    Cells(1 To 3, 1 To 3) As Long",
            "End Type",
            "Private this As TBoard",
            "Public Sub Fill()",
            "    Dim I As Long",
            "    Dim J As Long",
            "    For I = 1 To 3",
            "        For J = 1 To 3",
            "            this.Cells(I, J) = I * 10 + J",
            "        Next",
            "    Next",
            "End Sub",
            "Public Function Cell(ByVal Position As Coord) As Long",
            "    On Error Resume Next",
            "    Cell = 99",
            "    Cell = this.Cells(Position.X, Position.Y)",
            "End Function",
            "Public Function Picked(ByVal Position As Coord) As Long",
            "    Dim Values(1 To 3) As Long",
            "    Values(1) = 4: Values(2) = 5: Values(3) = 6",
            "    Picked = Values(Position.X) + Values(Position.Y) * 10",
            "End Function",
            "Public Function Poke() As Long",
            "    this.Cells(1, 1) = 9",
            "End Function",
            "Public Function Changed() As Long",
            "    Changed = this.Cells(1, 1) * 100 + Poke + this.Cells(1, 1)",
            "End Function")),
    ];

    // each procedure named was compiled, none of its statements was the interpreter's, and none of its instructions was handed to it after a call.
    private static void AssertCompiled(IReadOnlyDictionary<string, CompilationResult> compiled, params string[] names)
    {
        foreach (var name in names)
        {
            Assert.IsTrue(compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            Assert.IsEmpty(result.Stepped, $"{name} steps: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(0, result.Body.WalkBacks, $"{name}'s walk-backs");
            }
        }
    }

    // fields of a record read in an operation and in the condition of an If whose other operands are calls, given an object and an interface.
    [TestMethod]
    public async Task RecordFieldsBesideCalls_AreReadAsTheyAre()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As New Coord",
            "    Dim B As New Coord",
            "    Dim C As New Coord",
            "    A.X = 1: A.Y = 2: B.X = 1: B.Y = 2: C.X = 1: C.Y = 3",
            "    Debug.Print A.Equals(B); A.Equals(C); A.Adjacent(C); C.Adjacent(A); A.Adjacent(A)",
            "End Sub"), classes: Classes, expected: "TrueFalseTrueTrueFalse"), "Equals", "Adjacent");

    // a field read left of a call that changes it is what it was before the call, and one read right of it what the call made it.
    [TestMethod]
    public async Task ARecordFieldACallChanges_IsReadWhereItIsWritten()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As New Coord",
            "    A.X = 4",
            "    Debug.Print A.Order(); A.X",
            "End Sub"), classes: Classes, expected: " 455  5"), "Order", "Bump");

    // a field of a record assigned what a Property Get returns: of the class's own object named bare, of another object, and of the default instance; a
    // field of Me's record and of a record variable of the procedure.
    [TestMethod]
    public async Task ARecordFieldAssignedACall_HoldsWhatItReturned()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim G As New Grid",
            "    Debug.Print G.Counted(G)",
            "End Sub"), classes: Classes, expected: " 20"), "Counted");

    // a member called on the object a bare Property Get of the class returns, in the condition of a loop.
    [TestMethod]
    public async Task AMemberOfABarePropertyGet_IsCalledOnWhatItReturns()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim G As New Grid",
            "    Set G.Random = New Rand",
            "    Debug.Print G.Tries(0); G.Tries(7)",
            "End Sub"), classes: Classes, expected: " 4  8"), "Tries");

    // calls that leave out optional parameters: an object given Nothing, a scalar its declared default, and one passed by reference its type's default.
    [TestMethod]
    public async Task OptionalParametersLeftOut_AreGivenTheirDefaults()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim G As New Grid",
            "    Dim P As New Coord",
            "    P.X = 2",
            "    Debug.Print G.Hits(P)",
            "End Sub"), classes: Classes, expected: " 32009"), "Hits");

    // elements of an array, of a record's field and of a variable, at subscripts calls give; one outside the array's bounds raises error 9, which
    // On Error Resume Next goes past.
    [TestMethod]
    public async Task ElementsAtSubscriptsCallsGive_AreRead()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim B As New Board",
            "    Dim P As New Coord",
            "    B.Fill",
            "    Dim Q As New Coord",
            "    P.X = 2: P.Y = 3: Q.X = 7: Q.Y = 1",
            "    Debug.Print B.Cell(P); B.Picked(P); B.Cell(Q)",
            "End Sub"), classes: Classes, expected: " 23  65  99"), "Cell", "Picked");

    // an element read left of a call that changes it is what it was before the call, and one read right of it what the call made it.
    [TestMethod]
    public async Task AnElementACallChanges_IsReadWhereItIsWritten()
        => AssertCompiled(await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim B As New Board",
            "    B.Fill",
            "    Debug.Print B.Changed()",
            "End Sub"), classes: Classes, expected: " 1109"), "Changed");
}
