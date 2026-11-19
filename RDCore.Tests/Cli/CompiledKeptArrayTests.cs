using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledModuleFieldTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// An element of an array compiled code keeps, found from the shape it keeps with it: each subscript within the bounds of its dimension, the first varying
/// fastest (MS-VBAL §5.6.13), and <c>LBound</c> and <c>UBound</c> read from the same bounds (MS-VBAL §5.2.3.1.3). A subscript outside its dimension is error
/// 9, as the interpreter raises it; a <c>ReDim</c> gives the variable another array, whose shape is found again.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.13 Index Expressions")]
public sealed class CompiledKeptArrayTests
{
    // an array of three dimensions with negative lower bounds, filled and summed with each element weighed by its subscripts, so that one out of its place
    // changes the sum.
    [TestMethod]
    public async Task AnArrayOfThreeDimensions_HasEachElementWhereItsSubscriptsSay()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Weighed()",
            "End Sub",
            "Private Function Weighed() As Double",
            "    Dim A() As Long, I As Long, J As Long, K As Long",
            "    ReDim A(-2 To 1, 0 To 2, -1 To 0)",
            "    For I = LBound(A, 1) To UBound(A, 1)",
            "        For J = LBound(A, 2) To UBound(A, 2)",
            "            For K = LBound(A, 3) To UBound(A, 3)",
            "                A(I, J, K) = I * 100 + J * 10 + K",
            "            Next",
            "        Next",
            "    Next",
            "    For K = -1 To 0",
            "        For J = 0 To 2",
            "            For I = -2 To 1",
            "                Weighed = Weighed * 1.01 + A(I, J, K) * (I + 3)",
            "            Next",
            "        Next",
            "    Next",
            "End Function"),
            [("Weighed", 0)]);

    // MS-VBAL §5.6.13: a subscript outside its dimension, of any of the three, below or above it, is error 9, which the interpreter raises, once each.
    [TestMethod]
    public async Task ASubscriptOutsideItsDimension_IsSubscriptOutOfRange()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outside()",
            "End Sub",
            "Private Function Outside() As Long",
            "    Dim A() As Long, N As Long",
            "    ReDim A(1 To 2, -1 To 1, 3)",
            "    On Error Resume Next",
            "    N = A(0, 0, 0)",
            "    Outside = Outside * 10 + Err.Number Mod 10",
            "    Err.Clear",
            "    N = A(3, 0, 0)",
            "    Outside = Outside * 10 + Err.Number Mod 10",
            "    Err.Clear",
            "    N = A(1, -2, 0)",
            "    Outside = Outside * 10 + Err.Number Mod 10",
            "    Err.Clear",
            "    A(1, 2, 0) = 5",
            "    Outside = Outside * 10 + Err.Number Mod 10",
            "    Err.Clear",
            "    N = A(2, 1, 4)",
            "    Outside = Outside * 10 + Err.Number Mod 10",
            "    Err.Clear",
            "    A(2, 1, 3) = 7",
            "    Outside = Outside * 10 + A(2, 1, 3) + Err.Number",
            "End Function"),
            [("Outside", 5)]);

    // a ReDim in the loop gives the variable another array: what is read after it is found in that array, its bounds and its elements.
    [TestMethod]
    public async Task AReDimInsideTheLoop_GivesTheVariableAnotherArray_WhoseBoundsAndElementsAreRead()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Grown()",
            "End Sub",
            "Private Function Grown() As Long",
            "    Dim A() As Long, I As Long, Round As Long",
            "    ReDim A(1 To 2)",
            "    For Round = 1 To 3",
            "        For I = LBound(A) To UBound(A)",
            "            A(I) = A(I) + I * Round",
            "        Next",
            "        Grown = Grown * 100 + UBound(A) * 10 + A(UBound(A))",
            "        ReDim Preserve A(1 To Round + 2)",
            "    Next",
            "End Function"),
            [("Grown", 0)]);

    // the bounds of an array a Variant holds, one of no elements (Array()) among them, whose upper bound is the one below its lower; and an array of an
    // enumeration, whose elements are Longs.
    [TestMethod]
    public async Task TheBoundsOfAnArrayAVariantHolds_AndTheElementsOfAnArrayOfAnEnumeration_AreRead()
        => await AssertAlikeAsync(Program(
            "Public Enum Shade",
            "    Light = 1",
            "    Dark = 2",
            "End Enum",
            "Public Sub Main()",
            "    Debug.Print Bounds()",
            "    Debug.Print Shades()",
            "End Sub",
            "Private Function Held(ByVal N As Long) As Variant",
            "    If N = 1 Then Held = Array() Else Held = Array(1, 2, 3)",
            "End Function",
            "Private Function Bounds() As Long",
            "    Dim V As Variant, I As Long",
            "    For I = 1 To 2",
            "        V = Held(I)",
            "        Bounds = Bounds * 100 + (UBound(V) + 1) * 10 + LBound(V) + 1",
            "    Next",
            "End Function",
            "Private Function Shades() As Long",
            "    Dim S(1 To 4) As Shade, I As Long",
            "    For I = 1 To 4",
            "        S(I) = Dark",
            "        If I Mod 2 = 0 Then S(I) = Light",
            "    Next",
            "    For I = 1 To 4",
            "        Shades = Shades * 10 + S(I)",
            "    Next",
            "End Function"),
            [("Bounds", 0), ("Shades", 0)]);

    // a two-dimensional array of Variants numbered from 1, as the values of a block of cells are: each number doubled where the element is not Empty.
    [TestMethod]
    public async Task ATwoDimensionalArrayOfVariants_IsDoubledWhereItHoldsANumber()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim V(1 To 4, 1 To 3) As Variant, R As Long, C As Long",
            "    For R = 1 To 4",
            "        For C = 1 To 3",
            "            If (R + C) Mod 3 <> 0 Then V(R, C) = R * 10 + C + 0.5",
            "        Next",
            "    Next",
            "    Doubled V",
            "    Debug.Print Summed(V)",
            "End Sub",
            "Private Sub Doubled(ByRef V() As Variant)",
            "    Dim R As Long, C As Long",
            "    For R = 1 To UBound(V, 1)",
            "        For C = 1 To UBound(V, 2)",
            "            If Not IsEmpty(V(R, C)) Then V(R, C) = V(R, C) * 2",
            "        Next",
            "    Next",
            "End Sub",
            "Private Function Summed(ByRef V() As Variant) As Double",
            "    Dim R As Long, C As Long",
            "    For C = 1 To 3",
            "        For R = 1 To 4",
            "            If IsEmpty(V(R, C)) Then Summed = Summed + 1000 Else Summed = Summed * 1.5 + V(R, C)",
            "        Next",
            "    Next",
            "End Function"),
            [("Doubled", 0), ("Summed", 0)]);
}
