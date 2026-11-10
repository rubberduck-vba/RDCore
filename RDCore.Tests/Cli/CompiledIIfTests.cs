using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>IIf</c>, compiled: a call of the function of the standard library, through its binding, given both its parts evaluated - the true part first - as
/// MS-VBA gives them (MS-VBAL §6.1.2.8.1.11), and returning a <c>Variant</c>, whose arithmetic is the Variant's: an operation that overflows its subtype
/// is done in a wider one. In a statement that calls inside an expression, <c>IIf</c> and the <c>Variant</c> it returns are computed of what was read and
/// called before. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledIIfTests
{
    // a part chosen each time round a loop, added to a Long: none of it is the interpreter's.
    [TestMethod]
    public async Task APartChosen_IsAddedAsTheVariantItIs()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed(10)",
            "End Sub",
            "Private Function Summed(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Summed = Summed + IIf(I Mod 2 = 0, I, -I)",
            "    Next",
            "End Function"), [], "| 5 |", ("Summed", 0));

    // the Integer IIf returns, added to an Integer past the range of one, is a Long, as a Variant's sum is (MS-VBAL §5.6.9.3.2): no overflow.
    [TestMethod]
    public async Task TheVariantReturned_IsWidenedWhenItsSumOverflows()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Promoted(True); Promoted(False)",
            "End Sub",
            "Private Function Promoted(ByVal C As Boolean) As Long",
            "    Promoted = IIf(C, 32767, 1) + 1",
            "End Function"), [], "| 32768  2 |", ("Promoted", 1));

    // both parts are evaluated, the true part first, whichever is returned: each call is made, once.
    [TestMethod]
    public async Task BothParts_AreEvaluated_TheTruePartFirst()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Dim A As Long, B As Long",
            "    A = Picked(True)",
            "    B = Picked(False)",
            "    Debug.Print A; B",
            "End Sub",
            "Private Function Noted(ByVal N As Long) As Long",
            "    Debug.Print \"n\" & N",
            "    Noted = N * 10",
            "End Function",
            "Private Function Picked(ByVal C As Boolean) As Long",
            "    Picked = IIf(C, Noted(1), Noted(2)) + 1",
            "End Function"), [], "| n1 ; n2 ; n1 ; n2 ; 11  21 |", ("Picked", 0));

    // a condition that is Null takes the false part; one that is no Boolean raises error 13, which the handler is given.
    [TestMethod]
    public async Task ACondition_IsLetCoercedToABoolean()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Safe(True); Safe(0); Safe(\"x\"); Safe(Null)",
            "End Sub",
            "Private Function Safe(ByVal V As Variant) As Long",
            "    On Error GoTo Bad",
            "    Safe = IIf(V, 1, 2)",
            "    Exit Function",
            "Bad:",
            "    Safe = 100 + Err.Number",
            "End Function"), [], "| 1  2  113  2 |", ("Safe", 2));

    private static (string Name, string Source)[] Boards()
        =>
        [
            ("Coord", ModuleWorkspace.ClassModule("Coord", ["Option Explicit",
                "Private mX As Long, mY As Long",
                "Public Sub Init(ByVal X As Long, ByVal Y As Long)", "    mX = X", "    mY = Y", "End Sub",
                "Public Property Get X() As Long", "    X = mX", "End Property",
                "Public Property Get Y() As Long", "    Y = mY", "End Property"])),
            ("Board", ModuleWorkspace.ClassModule("Board", ["Option Explicit",
                "Private Type TBoard", "    Cells(1 To 5, 1 To 4) As Long", "End Type",
                "Private this As TBoard",
                "Public Function Fits(ByVal P As Coord, ByVal Across As Boolean, ByVal Size As Byte) As Boolean",
                "    Fits = (P.X + IIf(Across, Size - 1, 0) <= UBound(this.Cells, 1)) And (P.Y + IIf(Across, 0, Size - 1) <= UBound(this.Cells, 2))",
                "End Function",
                "Public Sub Place(ByVal P As Coord, ByVal Across As Boolean, ByVal Size As Byte)",
                "    Dim X As Long, Y As Long",
                "    For X = P.X To P.X + IIf(Across, Size - 1, 0)",
                "        For Y = P.Y To P.Y + IIf(Across, 0, Size - 1)",
                "            this.Cells(X, Y) = this.Cells(X, Y) + 1",
                "        Next",
                "    Next",
                "End Sub",
                "Public Function Covered() As Long",
                "    Dim X As Long, Y As Long",
                "    For X = 1 To 5",
                "        For Y = 1 To 4",
                "            Covered = Covered + this.Cells(X, Y)",
                "        Next",
                "    Next",
                "End Function"])),
        ];

    // IIf in a statement that calls inside an expression - a Property Get of the position - beside the bounds of the grid, and in the bounds of a For: what
    // each call returned and each bound read is what IIf and the Variant arithmetic are computed of.
    [TestMethod]
    public async Task IIfBesideACall_IsComputedOfWhatTheCallReturned()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Dim B As Board, P As Coord",
            "    Set B = New Board",
            "    Set P = New Coord",
            "    P.Init 4, 1",
            "    Debug.Print B.Fits(P, True, 2); B.Fits(P, True, 3); B.Fits(P, False, 4); B.Fits(P, False, 5)",
            "    B.Place P, True, 2",
            "    P.Init 1, 2",
            "    B.Place P, False, 3",
            "    Debug.Print B.Covered()",
            "End Sub"), Boards(), "| TrueFalseTrueFalse ; 5 |",
            ("Fits", 0), ("Place", 0), ("Covered", 0));
}
