using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledModuleFieldTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>LBound</c> and <c>UBound</c> of a fixed-size array, in compiled code: of a dimension a constant says, or the first, the bound its declaration gives
/// that dimension (MS-VBAL §3.3.5.2, §5.2.3.1.3), which no <c>ReDim</c> changes and <c>Erase</c> keeps, an omitted lower bound being the
/// <c>Option Base</c> of the module the array is declared in. Each is a constant to compiled code, whichever module declares the array; a dimension the
/// array has not is error 9, as the interpreter raises it.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.1.3 Array Dimensions and Bounds")]
public sealed class CompiledArrayBoundTests
{
    // the bounds of arrays of one and two dimensions, of one declared with a constant expression, and of one declared by its upper bound alone under Option
    // Base 1; and after an Erase, which keeps them.
    [TestMethod]
    public async Task TheBoundsOfAFixedSizeArray_AreTheBoundsItsDeclarationGives()
        => await AssertAlikeAsync(Program(
            "Option Base 1",
            "Private Const N As Long = 3",
            "Public Sub Main()",
            "    Debug.Print Bounds()",
            "End Sub",
            "Private Function Bounds() As String",
            "    Dim A(1 To 10) As Long, M(1 To 3, -2 To 4) As Double, C(N * 2) As Integer, I As Long, Total As Long",
            "    For I = 1 To 3",
            "        Total = Total * 10 + UBound(A) + LBound(A) + UBound(M, 2) - LBound(M, 2) + UBound(M, 1) + UBound(C) + LBound(C)",
            "    Next",
            "    A(2) = 7",
            "    Erase A",
            "    Bounds = Total & \" \" & LBound(M, 2) & \" \" & LBound(C) & \" \" & UBound(C) & \" \" & UBound(A) & \" \" & A(2)",
            "End Function"),
            [("Bounds", 0)]);

    // the bounds of a fixed-size array another module declares are those its declaration gives under that module's Option Base, not the caller's.
    [TestMethod]
    public async Task TheBoundsOfAFixedSizeArrayOfAnotherModule_AreThoseOfItsOwnOptionBase()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Bounds()",
            "End Sub",
            "Private Function Bounds() As Long",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        Bounds = Bounds * 100 + LBound(Cells) * 1000 + UBound(Cells) * 100 + LBound(Cells, 2) * 10 + UBound(Cells, 2)",
            "    Next",
            "End Function"),
            [("Bounds", 0)],
            [("Grid", ModuleWorkspace.StandardModule("Grid", ["Option Base 1", "Public Cells(4, 2 To 5) As Long"]))]);

    // a dimension given by a variable is read when the statement runs, as the bounds of an array that is not of a fixed size are.
    [TestMethod]
    public async Task ADimensionAVariableGives_IsTheBoundOfThatDimension()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Bounds()",
            "End Sub",
            "Private Function Bounds() As Long",
            "    Dim M(1 To 3, -2 To 4, 5) As Byte, D As Long",
            "    For D = 1 To 3",
            "        Bounds = Bounds * 100 + UBound(M, D) * 10 + LBound(M, D)",
            "    Next",
            "End Function"),
            [("Bounds", 0)]);

    // MS-VBAL §3.3.5.2: a dimension the array has not, of a constant or of a variable, is error 9, as the interpreter raises it; the statement that raises it
    // is the interpreter's, once each.
    [TestMethod]
    public async Task ADimensionAFixedSizeArrayHasNot_IsSubscriptOutOfRange()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Bounds()",
            "End Sub",
            "Private Function Bounds() As Long",
            "    Dim M(1 To 3, 4) As Long, D As Long, N As Long",
            "    On Error Resume Next",
            "    N = UBound(M, 3)",
            "    Bounds = Err.Number * 100",
            "    Err.Clear",
            "    D = 0",
            "    N = LBound(M, D)",
            "    Bounds = Bounds + Err.Number * 10 + N",
            "End Function"),
            [("Bounds", 2)]);
}
