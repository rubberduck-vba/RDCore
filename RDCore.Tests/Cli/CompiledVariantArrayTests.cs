using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The arrays compiled code finds where the interpreter keeps them other than in an array variable: a <c>ParamArray</c>, a <c>Variant</c> array whose
/// elements are the caller's arguments (MS-VBAL §5.3.1.5), and the array a <c>Variant</c> holds, found in the Variant's cell each time it is read. And
/// <c>TypeOf</c> of a <c>Variant</c> that holds an object (MS-VBAL §5.6.7). A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledVariantArrayTests
{
    // the bounds of a ParamArray, which is 0-based and has no elements when it collects none (UBound is -1), and each element read as the Variant it is.
    [TestMethod]
    public async Task AParamArray_ItsBoundsAndElements_AreReadInCompiledCode()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Total(1, 2, 3); Total(); Total(10)",
            "End Sub",
            "Private Function Total(ParamArray Values() As Variant) As Long",
            "    Dim I As Long",
            "    For I = LBound(Values) To UBound(Values)",
            "        Total = Total + Values(I)",
            "    Next",
            "End Function"), [], "| 6  0  10 |", ("Total", 0));

    // an element of a ParamArray given a Variant variable is that variable, which a Let of the element writes; one given a variable of another type refers
    // to it through a Variant compiled code does not read, and is the interpreter's.
    [TestMethod]
    public async Task AParamArrayElement_IsTheCallersVariable()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Dim A As Variant, B As Variant, C As Long",
            "    A = 1",
            "    B = 2",
            "    C = 3",
            "    Bump A, B",
            "    Bump C",
            "    Debug.Print A; B; C",
            "End Sub",
            "Private Sub Bump(ParamArray Values() As Variant)",
            "    Dim I As Long",
            "    For I = 0 To UBound(Values)",
            "        Values(I) = Values(I) + 10",
            "    Next",
            "End Sub"), [], "| 11  12  13 |", ("Bump", 1));

    // the array a Variant holds, its bounds and its elements found in the Variant's cell: after the Variant is given another array, in that one.
    [TestMethod]
    public async Task AVariantHoldingAnArray_ItsBoundsAndElements_AreReadInCompiledCode()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed()",
            "End Sub",
            "Private Function Digits(ByVal N As Long) As Variant",
            "    If N = 3 Then Digits = Array(5, 6, 7) Else Digits = Array(1, 2)",
            "End Function",
            "Private Function Summed() As Long",
            "    Dim V As Variant, I As Long",
            "    V = Digits(3)",
            "    For I = LBound(V) To UBound(V)",
            "        Summed = Summed * 10 + V(I)",
            "    Next",
            "    V = Digits(2)",
            "    For I = LBound(V) To UBound(V)",
            "        Summed = Summed * 10 + V(I)",
            "    Next",
            "End Function"), [], "| 56712 |", ("Summed", 0));

    // the bounds of an array of Longs a Variant holds are read in compiled code; an element of it, held in a Long's cell, is no Variant's, and is the
    // interpreter's to read. So is the assignment of the whole array.
    [TestMethod]
    public async Task AVariantHoldingAnArrayOfLongs_ItsBoundsAreReadInCompiledCode()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Spread()",
            "End Sub",
            "Private Function Spread() As Long",
            "    Dim A() As Long, V As Variant",
            "    ReDim A(2 To 5)",
            "    V = A",
            "    Spread = UBound(V) * 10 + LBound(V, 1)",
            "End Function"), [], "| 52 |", ["ForeignVariable A"], ("Spread", 1, 0));

    // LBound of a Variant that holds no array is error 13, the interpreter's (MS-VBAL §3.3.5.2).
    [TestMethod]
    public async Task TheBoundOfAVariantThatHoldsNoArray_RaisesError13()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Bounded(5)",
            "End Sub",
            "Private Function Bounded(ByVal V As Variant) As Long",
            "    On Error GoTo Bad",
            "    Bounded = LBound(V)",
            "    Exit Function",
            "Bad:",
            "    Bounded = 100 + Err.Number",
            "End Function"), [], "| 113 |", ("Bounded", 1));

    private static (string Name, string Source)[] Pins()
        =>
        [
            ("Pin", ModuleWorkspace.ClassModule("Pin", ["Option Explicit", "Public X As Long"])),
            ("Peg", ModuleWorkspace.ClassModule("Peg", ["Option Explicit", "Public Y As Long"])),
        ];

    // TypeOf of a Variant that holds an object tests the object; of one that holds no object (a number, Empty) it is False. Of one that holds Nothing,
    // it is error 91 (MS-VBAL §5.6.7), which the interpreter raises; so is the Let of a number over the object the Variant holds, which lets go of it.
    [TestMethod]
    public async Task TypeOfAVariant_TestsTheObjectItHolds()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Kinds()",
            "End Sub",
            "Private Function Kinds() As Long",
            "    Dim V As Variant, Unset As Variant, N As Pin",
            "    On Error GoTo Bad",
            "    Set V = New Pin",
            "    If TypeOf V Is Pin Then Kinds = 1",
            "    Set V = New Peg",
            "    If TypeOf V Is Pin Then Kinds = Kinds + 10",
            "    V = 5",
            "    If TypeOf V Is Pin Then Kinds = Kinds + 100",
            "    If TypeOf Unset Is Pin Then Kinds = Kinds + 1000",
            "    Set V = N",
            "    If TypeOf V Is Pin Then Kinds = Kinds + 10000",
            "    Exit Function",
            "Bad:",
            "    Kinds = Kinds + 100 * Err.Number",
            "End Function"), Pins(), "| 9101 |", ("Kinds", 2));

    // a ParamArray's elements each copied to a Variant and tested with TypeOf, as a test randomizer's Setup is given its sequence.
    [TestMethod]
    public async Task AParamArraysElements_AreCopiedAndTested()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Tally(1, 2.5, 3)",
            "End Sub",
            "Private Function Tally(ParamArray Items() As Variant) As Double",
            "    Dim I As Long",
            "    For I = LBound(Items) To UBound(Items)",
            "        Dim Item As Variant",
            "        Item = Items(I)",
            "        If TypeOf Item Is Pin Then",
            "            Tally = Tally + 100",
            "        Else",
            "            Tally = Tally + Items(I)",
            "        End If",
            "    Next",
            "End Function"), Pins(), "| 6.5 |", ("Tally", 0));
}
