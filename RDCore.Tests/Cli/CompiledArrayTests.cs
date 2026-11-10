using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// An array parameter or variable, compiled: held in the cell its frame holds it in, an element of it found in the array that cell holds
/// each time it is read or written (MS-VBAL §5.6.13), as the interpreter's evaluation on payloads finds it; <c>LBound</c> and <c>UBound</c> read where the
/// interpreter reads them (MS-VBAL §3.3.5.2). A subscript outside its dimension is the interpreter's, which raises error 9; a <c>ReDim</c> and an
/// <c>Erase</c> are the interpreter's statements, run as support calls. An element of an array of <c>Variant</c>s is read and written in its Variant's cell, as
/// a Variant variable is; an element of an array of objects is compared and tested, its <c>Set</c> the interpreter's. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledArrayTests
{
    private static readonly (string Name, string Source)[] Noisy =
        [("Noisy", ModuleWorkspace.ClassModule("Noisy", "Public Id As Long", "Private Sub Class_Terminate()", "    Debug.Print \"gone \" & Id", "End Sub"))];

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
    // recovered once at each end of the array; Err.Number is compiled code's to read, and Err.Clear is a support call.
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
            "End Function"), "Ranged", recoveries: 4);

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

    // Option Base 1 gives a dimension declared by its upper bound alone a lower bound of 1: LBound reads it, and an element at 0 is error 9, which Err.Number
    // reads.
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
            "End Function"), "Based", recoveries: 1);

    // LBound and UBound of a dimension the array doesn't have, 0 and 3: error 9, the interpreter's; Err.Number is compiled code's to read, and Err.Clear
    // is a support call.
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
            "End Function"), "Bounded", recoveries: 2);

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
    // erased: the interpreter's errors, given to the handler, which Err.Number reads. The two Err.Clear are support calls.
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
            "End Function"), "Emptied", recoveries: 3);

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

    // an array an assignment copies whole - another array, a function's result, an array of Variants, a String's bytes - holds its elements as the array it
    // is assigned to would: compiled code reads and writes them, and a write to the copy is not one to the array it was copied from. The 4 assignments are
    // the interpreter's statements; no element is.
    [TestMethod]
    public async Task AnArrayCopiedByAnAssignment_IsReadAndWrittenInCompiledCode()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Copied()",
            "End Sub",
            "Private Function Copied() As Double",
            "    Dim A(1 To 4) As Long",
            "    Dim B() As Long",
            "    Dim C() As Long",
            "    Dim V(1 To 3) As Variant",
            "    Dim W() As Variant",
            "    Dim Bytes() As Byte",
            "    Dim I As Long",
            "    For I = 1 To 4",
            "        A(I) = I * 5",
            "    Next",
            "    For I = 1 To 3",
            "        V(I) = I / 2",
            "    Next",
            "    B = A",
            "    C = Made(3)",
            "    W = V",
            "    Bytes = \"AB\"",
            "    For I = LBound(B) To UBound(B)",
            "        B(I) = B(I) + 1",
            "        Copied = Copied * 2 + B(I) + A(I)",
            "    Next",
            "    For I = 1 To 3",
            "        W(I) = W(I) * 4",
            "        Copied = Copied + C(I) + W(I) + V(I)",
            "    Next",
            "    For I = LBound(Bytes) To UBound(Bytes)",
            "        Copied = Copied * 3 + Bytes(I)",
            "    Next",
            "End Function",
            "Private Function Made(ByVal N As Long) As Long()",
            "    Dim R() As Long",
            "    Dim I As Long",
            "    ReDim R(1 To N)",
            "    For I = 1 To N",
            "        R(I) = I * 11",
            "    Next",
            "    Made = R",
            "End Function"), "Copied", recoveries: 4, expected: "30789");

    // an array of records an assignment copies whole: each element is Let-assigned, a copy of the record with its nested record and its array field, so a
    // write to a field of either array is not one to the other's.
    [TestMethod]
    public async Task AnArrayOfRecordsCopiedByAnAssignment_SharesNoFieldWithTheArrayItWasCopiedFrom()
        => await AssertAlikeAsync(Program(
            "Private Type Pair",
            "    X As Long",
            "    S As String",
            "End Type",
            "Private Type Holder",
            "    Inner As Pair",
            "    N(1 To 2) As Long",
            "End Type",
            "Public Sub Main()",
            "    Debug.Print Copied()",
            "End Sub",
            "Private Function Copied() As String",
            "    Dim A(1 To 2) As Holder",
            "    Dim B() As Holder",
            "    A(1).Inner.X = 1",
            "    A(1).Inner.S = \"a\"",
            "    A(1).N(1) = 3",
            "    B = A",
            "    B(1).Inner.X = 2",
            "    B(1).Inner.S = \"b\"",
            "    B(1).N(1) = 4",
            "    A(2).Inner.X = 9",
            "    Copied = A(1).Inner.X & A(1).Inner.S & A(1).N(1) & \"|\" & B(1).Inner.X & B(1).Inner.S & B(1).N(1) & \"|\" & B(2).Inner.X",
            "End Function"), expected: "1a3|2b4|0");

    // an array of Variants an assignment copies whole, one of them holding an array: that element is Let-assigned a copy of the array it holds, so a write
    // to an element of either held array is not one to the other's. The numbers assigned are compiled code's; the 7 statements the interpreter runs are the Strings
    // assigned and joined, Array, the copy and the writes to an element of an element.
    [TestMethod]
    public async Task AnArrayOfVariantsCopiedByAnAssignment_SharesNoArrayAnElementHolds()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Copied()",
            "End Sub",
            "Private Function Copied() As String",
            "    Dim V(0 To 2) As Variant",
            "    Dim W() As Variant",
            "    V(0) = 1",
            "    V(1) = Array(10, 20)",
            "    V(2) = \"s\"",
            "    W = V",
            "    W(0) = 5",
            "    W(1)(0) = 99",
            "    W(2) = \"t\"",
            "    V(1)(1) = 77",
            "    Copied = V(0) & \",\" & V(1)(0) & \",\" & V(1)(1) & \",\" & V(2) & \"|\" & W(0) & \",\" & W(1)(0) & \",\" & W(1)(1) & \",\" & W(2)",
            "End Function"), "Copied", recoveries: 7, expected: "1,10,77,s|5,99,20,t");

    // a Variant assigned to an element of an array of a scalar type, holding a String: compiled code declines it, as it does a Variant's Let to a variable,
    // and the interpreter runs the Let (let-coercing "5"); with no handler, the procedure goes on in compiled code after it, each time round. A Variant
    // holding a number is compiled code's own.
    [TestMethod]
    public async Task AVariantAssignedToAnElement_ThatCompiledCodeDeclines_IsTheInterpretersLet()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Assigned()",
            "End Sub",
            "Private Function Assigned() As Double",
            "    Dim A(1 To 3) As Long",
            "    Dim V As Variant",
            "    Dim I As Long",
            "    V = \"5\"",
            "    For I = 1 To 3",
            "        A(I) = V",
            "        A(I) = A(I) + I",
            "    Next",
            "    V = 20",
            "    A(2) = V",
            "    For I = 1 To 3",
            "        Assigned = Assigned * 10 + A(I)",
            "    Next",
            "End Function"), "Assigned", recoveries: 1 + 3);

    // an element of an array of Variants holds what it is assigned, of its own type: a payload compiled code reads and writes; a String, Empty, and an object
    // the interpreter's to read, and a Let over the object, which lets go of it there, before the next line prints: recovered, as are the 3 statements
    // the interpreter runs (the String, Len, the member). The prints are compiled code's. The Set of an element to a New object is a support call the
    // interpreter runs, no recovery.
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
            "End Function"), "Mixed", recoveries: 3 + 1, classes: Noisy);

    // an element of an array of objects compared with Nothing and with another, and tested with TypeOf, in compiled code: an element no object was ever Set
    // to holds Nothing. Each Set of an element, and the Set of O to one, is a support call the interpreter runs, no recovery; the Set of O to Nothing, which
    // lets go of no last reference, is compiled. The object two elements held is let go of at the second Set of an element to Nothing, which prints there:
    // the one statement the interpreter runs is the member, none of the loop, and the prints are compiled code's.
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
            "End Function"), "Held", recoveries: 1, classes: Noisy);
}
