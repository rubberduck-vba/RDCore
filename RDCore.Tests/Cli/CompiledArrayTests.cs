using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// An array parameter or variable, compiled: held in the cell its frame holds it in, an element of it found in the array that cell holds
/// each time it is read or written (MS-VBAL §5.6.13.1), as the interpreter's evaluation on payloads finds it; <c>LBound</c> and <c>UBound</c> read where the
/// interpreter reads them (MS-VBAL §3.3.5.2). A subscript outside its dimension is the interpreter's, which raises error 9; a <c>ReDim</c> and an
/// <c>Erase</c> are the interpreter's statements, run as support calls. An element of an array of <c>Variant</c>s is read and written in its Variant's cell, as
/// a Variant variable is; an element of an array of objects is compared and tested, its <c>Set</c> the interpreter's. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledArrayTests
{
    private static readonly (string Name, string Source)[] Noisy =
        [("Noisy", ModuleWorkspace.ClassModule("Noisy", "Public Id As Long", "Private Sub Class_Terminate()", "    Debug.Print \"gone \" & Id", "End Sub"))];

    // an element of an array of Variants holds what it is assigned, of its own type: a payload compiled code reads and writes; a String, Empty, and an object
    // the interpreter's to read - and a Let over the object, which lets go of it there, before the next line prints: recovered, as are the 6 statements
    // the interpreter runs (the String, Len, the Set, the member, the prints).
    [TestMethod]
    public async Task AnElementOfAVariantArray_IsReadAndWrittenInItsVariantsCell()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Mixed()",
            "End Sub",
            "Private Function Mixed() As Double",
            "    Dim V(1 To 6) As Variant",
            "    Dim I As Long",
            "    For I = 1 To 5",
            "        V(I) = I * 1.5",
            "    Next",
            "    For I = 1 To 5",
            "        V(I) = V(I) * 2 + I",
            "    Next",
            "    V(3) = \"x\"",
            "    For I = 1 To 5",
            "        If I <> 3 Then Mixed = Mixed * 1.5 + V(I)",
            "    Next",
            "    Mixed = Mixed + Len(V(3)) + IsEmpty(V(6))",
            "    Set V(2) = New Noisy",
            "    V(2).Id = 7",
            "    Debug.Print \"held\"",
            "    V(2) = 4",
            "    Debug.Print \"let go\"",
            "    Mixed = Mixed + V(2)",
            "End Function"), "Mixed", recoveries: 6 + 1, classes: Noisy);

    // an element of an array of objects compared with Nothing and with another, and tested with TypeOf, in compiled code: an element no object was ever Set
    // to holds Nothing. Each Set is the interpreter's, and the object two elements held is let go of at the second Set of Nothing, which prints there: the
    // 10 statements the interpreter runs are the Sets, the member and the prints, none of the loop.
    [TestMethod]
    public async Task AnElementOfAnObjectArray_IsComparedInCompiledCode_AndLetGoOfWhereTheInterpreterSetsIt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Held()",
            "End Sub",
            "Private Function Held() As Long",
            "    Dim A(1 To 3) As Noisy",
            "    Dim O As Object",
            "    Dim I As Long",
            "    Set A(1) = New Noisy",
            "    A(1).Id = 1",
            "    Set A(2) = A(1)",
            "    Set O = A(1)",
            "    For I = 1 To 3",
            "        If A(I) Is Nothing Then Held = Held * 2 Else Held = Held * 2 + 1",
            "        If A(I) Is A(1) Then Held = Held + 100",
            "        If Not A(I) Is Nothing Then",
            "            If TypeOf A(I) Is Noisy Then Held = Held + 1000",
            "        End If",
            "        If O Is A(I) Then Held = Held + 10000",
            "    Next",
            "    Set O = Nothing",
            "    Debug.Print \"before\"",
            "    Set A(1) = Nothing",
            "    Debug.Print \"middle\"",
            "    Held = Held + (A(1) Is Nothing) + (A(2) Is Nothing)",
            "    Set A(2) = Nothing",
            "    Debug.Print \"after\"",
            "End Function"), "Held", recoveries: 10, classes: Noisy);
    // an array filled, then summed, element by element: none of it is the interpreter's.
    [TestMethod]
    public async Task AnArrayFilledThenSummed_IsReadAndWrittenInCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed()",
            "End Sub",
            "Private Function Summed() As Double",
            "    Dim Values(1 To 50) As Long",
            "    Dim I As Long",
            "    For I = 1 To 50",
            "        Values(I) = I * 3",
            "    Next",
            "    For I = 1 To 50",
            "        Summed = Summed + Values(I)",
            "    Next",
            "End Function"), "Summed", recoveries: 0);

    // an element of each scalar kind, each written a value let-coerced to it - a Double rounded into an Integer, a number into a Boolean - and read back.
    [TestMethod]
    public async Task AnElementOfEachScalarKind_IsLetCoercedToIt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Kinds()",
            "End Sub",
            "Private Function Kinds() As Double",
            "    Dim B(2) As Byte",
            "    Dim N(2) As Integer",
            "    Dim L(2) As Long",
            "    Dim D(2) As Double",
            "    Dim S(2) As Single",
            "    Dim C(2) As Currency",
            "    Dim F(2) As Boolean",
            "    Dim T(2) As Date",
            "    Dim I As Long",
            "    For I = 0 To 2",
            "        B(I) = I * 2.5",
            "        N(I) = I * 1.5",
            "        L(I) = I * 7",
            "        D(I) = I / 4",
            "        S(I) = I / 8",
            "        C(I) = I * 1.25",
            "        F(I) = I",
            "        T(I) = I + 0.5",
            "    Next",
            "    For I = 0 To 2",
            "        Kinds = Kinds * 3 + B(I) + N(I) + L(I) + D(I) + S(I) + C(I) + T(I)",
            "        If F(I) Then Kinds = Kinds + 1000",
            "    Next",
            "End Function"), "Kinds", recoveries: 0);

    // an array of two and of three dimensions, column-major as VBArrayValue holds it: each element at its own subscripts.
    [TestMethod]
    public async Task AnElementOfSeveralDimensions_IsFoundAtItsSubscripts()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Grid()",
            "End Sub",
            "Private Function Grid() As Double",
            "    Dim G(1 To 3, 0 To 4) As Long",
            "    Dim K(2, 1, 3) As Double",
            "    Dim I As Long, J As Long, M As Long",
            "    For I = 1 To 3",
            "        For J = 0 To 4",
            "            G(I, J) = I * 10 + J",
            "        Next",
            "    Next",
            "    For I = 0 To 2",
            "        For J = 0 To 1",
            "            For M = 0 To 3",
            "                K(I, J, M) = G(I + 1, J + M) / 2",
            "            Next",
            "        Next",
            "    Next",
            "    For I = 0 To 2",
            "        For J = 0 To 1",
            "            For M = 0 To 3",
            "                Grid = Grid * 1.01 + K(I, J, M) * (I + 1) * (J + 2) * (M + 3)",
            "            Next",
            "        Next",
            "    Next",
            "End Function"), "Grid", recoveries: 0);

    // a subscript outside its dimension is error 9, raised by the interpreter at the statement, which Resume Next goes past: a read and a write each
    // recovered once at each end of the array - and Err.Number and Err.Clear, the interpreter's, at each of the 7 times round.
    [TestMethod]
    public async Task ASubscriptOutOfRange_UnderResumeNext_IsGonePast()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Ranged()",
            "End Sub",
            "Private Function Ranged() As Long",
            "    On Error Resume Next",
            "    Dim V(1 To 5) As Long",
            "    Dim I As Long",
            "    For I = 0 To 6",
            "        V(I) = I * I",
            "        Ranged = Ranged * 2 + V(I)",
            "        Ranged = Ranged + Err.Number",
            "        Err.Clear",
            "    Next",
            "End Function"), "Ranged", recoveries: 4 + 7 * 2);

    // a subscript outside its dimension with no handler: error 9, at the statement, with the stack the interpreter gives it - in the middle of the loop,
    // after the 6 Debug.Print the interpreter ran.
    [TestMethod]
    public async Task ASubscriptOutOfRange_MidLoop_WalksBackWithTheInterpretersError()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Walked(9)",
            "End Sub",
            "Private Function Walked(ByVal Count As Long) As Long",
            "    Dim V(1 To 6) As Long",
            "    Dim I As Long",
            "    For I = 1 To Count",
            "        V(I) = I",
            "        Debug.Print V(I)",
            "    Next",
            "End Function"), "Walked", recoveries: 6, walkBacks: 1);

    // Option Base 1 gives a dimension declared by its upper bound alone a lower bound of 1: LBound reads it, and an element at 0 is error 9 (and Err.Number
    // the interpreter's).
    [TestMethod]
    public async Task OptionBase1_GivesTheLowerBound_ThatLBoundReads()
        => await AssertAlikeAsync("Option Base 1\r\n" + Program(
            "Public Sub Main()",
            "    Debug.Print Based()",
            "End Sub",
            "Private Function Based() As Long",
            "    On Error Resume Next",
            "    Dim V(4) As Long",
            "    Dim G(3, 2 To 5) As Integer",
            "    Dim I As Long",
            "    For I = LBound(V) To UBound(V)",
            "        V(I) = I",
            "    Next",
            "    Based = LBound(V) * 1000 + UBound(V) * 100 + LBound(G, 1) * 10 + LBound(G, 2) + UBound(G, 2) * 10000",
            "    V(0) = 7",
            "    Based = Based + Err.Number * 100000",
            "End Function"), "Based", recoveries: 1 + 1);

    // LBound and UBound of a dimension the array doesn't have, 0 and 3: error 9, the interpreter's - as Err.Number and Err.Clear are, 4 times round.
    [TestMethod]
    public async Task ABoundOfADimensionTheArrayHasNot_IsTheInterpretersError()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Bounded()",
            "End Sub",
            "Private Function Bounded() As Long",
            "    On Error Resume Next",
            "    Dim V(2 To 3, 5) As Long",
            "    Dim I As Long",
            "    For I = 0 To 3",
            "        Bounded = Bounded * 10 + UBound(V, I) + LBound(V, I)",
            "        Bounded = Bounded + Err.Number * 1000",
            "        Err.Clear",
            "    Next",
            "End Function"), "Bounded", recoveries: 2 + 4 * 2);

    // a ReDim Preserve inside a loop over the array grows it as the loop goes: compiled code finds each element in the array the cell holds after it.
    [TestMethod]
    public async Task AReDimPreserve_InsideALoopOverTheArray_GrowsWhatTheLoopReads()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Grown()",
            "End Sub",
            "Private Function Grown() As Double",
            "    Dim V() As Long",
            "    Dim I As Long",
            "    ReDim V(1 To 1)",
            "    For I = 1 To 12",
            "        ReDim Preserve V(1 To I)",
            "        V(I) = I * I",
            "        Grown = Grown + V(I) + V(1) + UBound(V)",
            "    Next",
            "    For I = LBound(V) To UBound(V)",
            "        If I = 4 Then ReDim Preserve V(1 To 6)",
            "        If I <= UBound(V) Then Grown = Grown * 1.5 + V(I)",
            "    Next",
            "    ReDim V(0 To 2)",
            "    Grown = Grown + V(2) + LBound(V)",
            "    Erase V",
            "    Grown = Grown + 0.5",
            "End Function"), "Grown", recoveries: 0);

    // an element of an array Erase emptied, and a ReDim to bounds the program can't have, and UBound of the array it left
    // erased: the interpreter's errors, given to the handler - and the two Err.Number and two Err.Clear.
    [TestMethod]
    public async Task AnErasedArray_AndAReDimThatFails_AreTheInterpretersErrors()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Emptied()",
            "End Sub",
            "Private Function Emptied() As Long",
            "    On Error Resume Next",
            "    Dim V() As Long",
            "    Dim I As Long",
            "    ReDim V(3)",
            "    V(2) = 5",
            "    Erase V",
            "    Emptied = V(2)",
            "    Emptied = Emptied + Err.Number",
            "    Err.Clear",
            "    I = -4",
            "    ReDim V(I To -5)",
            "    Emptied = Emptied * 100 + Err.Number",
            "    Err.Clear",
            "    Emptied = Emptied * 100 + UBound(V)",
            "End Function"), "Emptied", recoveries: 3 + 4);

    // an array passed by reference whole - to a callee compiled code runs, which writes its elements, and to one the interpreter runs, whose loop counts
    // with a parameter passed by reference - and an element passed by reference, which the interpreter passes: what each callee writes is in the caller's
    // array after it.
    [TestMethod]
    public async Task AnArrayOrAnElementPassedByReference_IsWrittenByTheCallee()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Passed()",
            "End Sub",
            "Private Function Passed() As Double",
            "    Dim V(1 To 8) As Long",
            "    Dim I As Long, J As Long",
            "    Fill V",
            "    For I = 1 To 8",
            "        Passed = Passed * 2 + V(I)",
            "    Next",
            "    FillInterpreted V, J",
            "    Passed = Passed + J",
            "    For I = 1 To 8",
            "        Bump V(I)",
            "        Passed = Passed * 2 + V(I)",
            "    Next",
            "End Function",
            "Private Sub Fill(A() As Long)",
            "    Dim I As Long",
            "    For I = LBound(A) To UBound(A)",
            "        A(I) = A(I) + I * 3",
            "    Next",
            "End Sub",
            "Private Sub FillInterpreted(A() As Long, I As Long)",
            "    For I = LBound(A) To UBound(A)",
            "        A(I) = A(I) * 2 + 1",
            "    Next",
            "End Sub",
            "Private Sub Bump(ByRef X As Long)",
            "    X = X + 100",
            "End Sub"));
        Assert.IsNotNull(compiled["Passed"].Body, "Passed");
        Assert.IsNotNull(compiled["Fill"].Body, "Fill");
        Assert.IsNull(compiled["FillInterpreted"].Body, "FillInterpreted");
        Assert.IsNotNull(compiled["Bump"].Body, "Bump");
        Assert.AreEqual(0, compiled["Fill"].Body!.Recoveries, "Fill's recoveries");
    }
}
