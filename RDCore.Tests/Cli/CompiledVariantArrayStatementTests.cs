using static RDCore.Tests.Cli.CompiledArrayFieldTests;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// What a macro does with the values of a range, compiled: a <c>ReDim</c> and an <c>Erase</c> of an array of <c>Variant</c>s or of objects
/// (MS-VBAL §5.4.3.3-4), and the Let of an array to a <c>Variant</c> (MS-VBAL §5.4.3.8), are the runtime's statements, run from compiled code; what they
/// let go of is terminated where the interpreter terminates it. <c>IsEmpty</c> of a Variant (MS-VBAL §6.1.2.7.1.4) is read in its cell. An element that a
/// Let writes and its value reads at the same subscripts is found once; an element outside its dimension still raises error 9 at its statement. A program
/// runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledVariantArrayStatementTests
{
    private static readonly (string Name, string Source)[] Probes =
        [("Probe", ModuleWorkspace.ClassModule("Probe", "Option Explicit", "Private Sub Class_Terminate()", "    Debug.Print \"T\";", "End Sub"))];

    // the block pass over a range's values: an array made, copied to a Variant, and each element that is not Empty doubled where it is. The copy is the
    // Variant's own: the array it was made of keeps its values.
    [TestMethod]
    public async Task TheBlockPass_IsCompiledWhole()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Passed()",
            "End Sub",
            "Private Function Passed() As Double",
            "    Dim Values() As Variant, V As Variant, R As Long, C As Long, S As Double, N As Long",
            "    ReDim Values(1 To 3, 1 To 2)",
            "    Values(1, 1) = 1",
            "    Values(2, 2) = 2.5",
            "    Values(3, 1) = 4",
            "    V = Values",
            "    For R = 1 To 3",
            "        For C = 1 To 2",
            "            If Not IsEmpty(V(R, C)) Then V(R, C) = V(R, C) * 2",
            "        Next",
            "    Next",
            "    For R = 1 To 3",
            "        For C = 1 To 2",
            "            If IsEmpty(V(R, C)) Then N = N + 1 Else S = S + V(R, C)",
            "        Next",
            "    Next",
            "    Passed = S * 1000 + N * 100 + Values(2, 2)",
            "End Function"), [], "| 15302.5 |", ("Passed", 0));

    // IsEmpty of an element or of a Variant variable, read in its cell: a String, Null and a number are not Empty.
    [TestMethod]
    public async Task IsEmptyOfAVariant_IsReadInItsCell()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Dim A(1 To 4) As Variant",
            "    A(1) = \"text\"",
            "    A(2) = Null",
            "    A(4) = 7",
            "    Debug.Print Empties(A)",
            "End Sub",
            "Private Function Empties(ByRef A() As Variant) As Long",
            "    Dim I As Long, V As Variant",
            "    For I = LBound(A) To UBound(A)",
            "        If IsEmpty(A(I)) Then Empties = Empties + 1",
            "    Next",
            "    If IsEmpty(V) Then Empties = Empties + 10",
            "    V = 1",
            "    If IsEmpty(V) Then Empties = Empties + 100",
            "End Function"), [], "| 11 |", ("Empties", 0));

    // IsEmpty of an element that holds an array is the standard library's: the interpreter runs the If, and compiled code goes on after it.
    [TestMethod]
    public async Task IsEmptyOfAnElementHoldingAnArray_IsTheStandardLibrarys()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Dim A(1 To 2) As Variant",
            "    A(1) = Array(1, 2)",
            "    Debug.Print Counted(A)",
            "End Sub",
            "Private Function Counted(ByRef A() As Variant) As Long",
            "    Dim I As Long",
            "    For I = 1 To 2",
            "        If IsEmpty(A(I)) Then Counted = Counted + 1 Else Counted = Counted + 10",
            "    Next",
            "End Function"), [], "| 11 |", ("Counted", 1));

    // a ReDim of an array of Variants and of objects, with Preserve and without, and an Erase: what each lets go of is terminated in the statement.
    [TestMethod]
    public async Task AReDimAndAnErase_TerminateWhatTheyLetGoOf()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Resized()",
            "End Sub",
            "Private Function Resized() As Long",
            "    Dim A() As Variant, O() As Probe",
            "    ReDim A(1 To 2)",
            "    Set A(1) = New Probe",
            "    Debug.Print \"a\";",
            "    ReDim Preserve A(1 To 3)",
            "    Debug.Print \"b\";",
            "    ReDim A(1 To 1)",
            "    Debug.Print \"c\";",
            "    ReDim O(1 To 2)",
            "    Set O(2) = New Probe",
            "    Erase O",
            "    Debug.Print \"d\";",
            "    Resized = UBound(A) * 10 + LBound(A)",
            "End Function"), Probes, "abTcTd 11", ("Resized", 0));

    // the Let of an array to a Variant gives it a copy (MS-VBAL §5.5.1.2.12), and lets go of the object the Variant held, which is terminated there.
    [TestMethod]
    public async Task TheLetOfAnArrayToAVariant_CopiesItAndLetsGoOfWhatTheVariantHeld()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Copied()",
            "End Sub",
            "Private Function Copied() As Long",
            "    Dim A() As Variant, V As Variant",
            "    ReDim A(1 To 2)",
            "    A(1) = 5",
            "    Set V = New Probe",
            "    Debug.Print \"a\";",
            "    V = A",
            "    Debug.Print \"b\";",
            "    V(1) = 6",
            "    Copied = A(1) * 10 + V(1)",
            "End Function"), Probes, "aTb 56", ("Copied", 0));

    // an element a Let reads at other subscripts than the ones it writes is found at its own: each is the sum of the ones before it.
    [TestMethod]
    public async Task AnElementReadAtOtherSubscripts_IsFoundAtItsOwn()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed()",
            "End Sub",
            "Private Function Summed() As Long",
            "    Dim A(1 To 4) As Variant, L(1 To 4) As Long, R As Long",
            "    For R = 1 To 4",
            "        A(R) = R",
            "        L(R) = R",
            "    Next",
            "    For R = 2 To 4",
            "        A(R) = A(R - 1) + A(R)",
            "        L(R) = L(R - 1) + L(R)",
            "    Next",
            "    Summed = A(4) * 100 + L(4)",
            "End Function"), [], "| 1010 |", ("Summed", 0));

    // the array an element is found in is kept from one access to the next, until something gives its variable another: the Let of another array, a
    // ReDim Preserve, a callee given it by reference. The next element is found in the array the variable holds then.
    [TestMethod]
    public async Task AnArrayGivenAnotherInALoop_IsFoundAgain()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Swapped(); Grown(); Given()",
            "End Sub",
            "Private Function Swapped() As Long",
            "    Dim A() As Variant, B() As Variant, V As Variant, I As Long",
            "    ReDim A(1 To 3)",
            "    ReDim B(1 To 3)",
            "    For I = 1 To 3",
            "        A(I) = I",
            "        B(I) = I * 10",
            "    Next",
            "    V = A",
            "    For I = 1 To 3",
            "        Swapped = Swapped + V(I)",
            "        If I = 1 Then V = B",
            "    Next",
            "End Function",
            "Private Function Grown() As Long",
            "    Dim C() As Long, J As Long",
            "    ReDim C(1 To 1)",
            "    For J = 1 To 3",
            "        ReDim Preserve C(1 To J)",
            "        C(J) = J * J",
            "        Grown = Grown + C(J)",
            "    Next",
            "End Function",
            "Private Function Given() As Long",
            "    Dim D() As Variant, J As Long",
            "    ReDim D(1 To 1)",
            "    D(1) = 1",
            "    For J = 1 To 2",
            "        Grow D",
            "        Given = Given + D(UBound(D))",
            "    Next",
            "End Function",
            "Private Sub Grow(ByRef A() As Variant)",
            "    ReDim Preserve A(1 To UBound(A) + 1)",
            "    A(UBound(A)) = UBound(A) * 100",
            "End Sub"), [], "| 51  14  500 |", ("Swapped", 0), ("Grown", 0), ("Given", 0), ("Grow", 0));

    // MS-VBAL §5.6.13: an element outside its dimension raises error 9 at the Let that writes it, which writes nothing; the handler is given it.
    [TestMethod]
    public async Task AnElementOutsideItsDimension_RaisesError9AtItsLet()
        => await AssertCompiledAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outside()",
            "End Sub",
            "Private Function Outside() As Long",
            "    Dim A() As Variant, V As Variant, R As Long",
            "    ReDim A(1 To 2)",
            "    A(1) = 1",
            "    A(2) = 2",
            "    V = A",
            "    On Error GoTo Bad",
            "    For R = 1 To 3",
            "        V(R) = V(R) * 2",
            "        Outside = Outside + 1",
            "    Next",
            "    Exit Function",
            "Bad:",
            "    Outside = Outside * 100 + Err.Number * 10 + V(2)",
            "End Function"), [], "| 294 |", ("Outside", 1));
}
