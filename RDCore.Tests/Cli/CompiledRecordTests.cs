using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A field of a record parameter or variable, compiled (IR lowering S2.6a): the record held in the cell its frame holds it in, and each field read and
/// written where the record holds it - a field of a scalar type as a payload of its kind, a <c>Variant</c> field as a Variant variable is. What the
/// interpreter does of the record - a Let of the whole record, a statement it runs alone, the caller whose variable it is - it does to that very record. A
/// program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledRecordTests
{
    private static readonly string[] Types =
    [
        "Private Type TPoint",
        "    X As Long",
        "    Y As Double",
        "    Small As Integer",
        "    V As Variant",
        "End Type",
        "Private Type TPair",
        "    Inner As TPoint",
        "    Count As Long",
        "End Type",
    ];

    private static string Module(params string[] lines) => Program([.. Types, .. lines]);

    [TestMethod]
    public async Task TheFieldsOfARecordVariable_AreReadAndWrittenWhereTheRecordHoldsThem()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Debug.Print Walk(5); Walk(2)",
            "End Sub",
            "Private Function Walk(ByVal N As Long) As Double",
            "    Dim P As TPoint",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        P.X = P.X + I",
            "        P.Y = P.Y + P.X * 0.5",
            "    Next",
            "    Walk = P.X * 1000 + P.Y",
            "End Function"), "Walk", recoveries: 0);

    // a record parameter is its caller's variable (MS-VBAL §5.3.1.11): what compiled code writes to its fields, the caller reads, and what the caller wrote,
    // compiled code reads.
    [TestMethod]
    public async Task ARecordParameter_IsItsCallersVariable()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Dim P As TPoint",
            "    P.X = 3",
            "    Bump P, 4",
            "    Debug.Print P.X; P.Y",
            "    Bump P, 1",
            "    Debug.Print P.X; P.Y",
            "End Sub",
            "Private Sub Bump(ByRef P As TPoint, ByVal N As Long)",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        P.X = P.X * 2",
            "        P.Y = P.Y - P.X",
            "    Next",
            "End Sub"), "Bump", recoveries: 0);

    // a Variant field holds what it is assigned of its own subtype (MS-VBAL §5.5.1.2.12): Empty, then an Integer, widened to a Long when the sum overflows,
    // as a Variant variable is. Empty holds no payload: the first sum of each call is the interpreter's, and so are the sum that overflows the Integer and
    // each String statement.
    [TestMethod]
    public async Task AVariantField_IsReadAndWrittenAsAVariantVariableIs()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Debug.Print Summed(3); Summed(300)",
            "End Sub",
            "Private Function Summed(ByVal N As Integer) As String",
            "    Dim P As TPoint",
            "    Dim I As Integer",
            "    For I = 1 To N",
            "        P.V = P.V + I * 30",
            "    Next",
            "    Summed = TypeName(P.V) & \" \" & P.V",
            "End Function"), "Summed", recoveries: 5);

    // a record copied whole copies what its Variant field holds (VBUserDefinedTypeValue.DataCopy): each record's Variant is its own after it.
    [TestMethod]
    public async Task ARecordCopiedByValue_CopiesItsVariantField()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Copied 3",
            "End Sub",
            "Private Sub Copied(ByVal N As Long)",
            "    Dim A As TPoint, B As TPoint",
            "    Dim I As Long",
            "    A.V = 1",
            "    For I = 1 To N",
            "        B = A",
            "        B.V = B.V * 10 + I",
            "        A.V = A.V + B.V",
            "        Debug.Print A.V; B.V; TypeName(A.V)",
            "    Next",
            "End Sub"), "Copied", recoveries: 6);

    // a field of a record that is a record itself is written in place, through it.
    [TestMethod]
    public async Task AFieldOfARecordInARecord_IsWrittenInPlace()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Dim Q As TPair",
            "    Nest Q, 4",
            "    Debug.Print Q.Inner.X; Q.Inner.Y; Q.Count",
            "End Sub",
            "Private Sub Nest(ByRef Q As TPair, ByVal N As Long)",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Q.Inner.X = Q.Inner.X + I",
            "        Q.Count = Q.Count + 1",
            "        Q.Inner.Y = Q.Inner.X / Q.Count",
            "    Next",
            "End Sub"), "Nest", recoveries: 0);

    // a record is copied whole by a Let (MS-VBAL §5.4.3.8), which the interpreter runs: each record has fields of its own after it, which compiled code reads
    // and writes in the record its variable holds then.
    [TestMethod]
    public async Task ARecordCopiedByValue_HasFieldsOfItsOwn()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Copied 3",
            "End Sub",
            "Private Sub Copied(ByVal N As Long)",
            "    Dim A As TPoint, B As TPoint",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        A.X = A.X + I",
            "        B = A",
            "        B.X = B.X * 10",
            "        A.Y = A.Y + B.X",
            "        Debug.Print A.X; B.X; A.Y; B.Y",
            "    Next",
            "End Sub"), "Copied", recoveries: 6);

    private static (string Name, string Source)[] Noisy()
        => [("Noisy", ModuleWorkspace.ClassModule("Noisy", ["Option Explicit", "Public Name As String",
            "Private Sub Class_Terminate()", "    Debug.Print \"gone \" & Name", "End Sub"]))];

    // an object a field holds is let go of where the interpreter lets go of it: a copy of another record over the record, a Let of a number to a Variant
    // field that held it - which compiled code hands to the interpreter - and the end of the activation whose variable the record is.
    [TestMethod]
    public async Task AnObjectHeldInAField_TerminatesWhereTheInterpreterLetsGoOfIt()
        => await AssertAlikeAsync(Program(
            "Private Type THolder",
            "    O As Noisy",
            "    N As Long",
            "    V As Variant",
            "End Type",
            "Public Sub Main()",
            "    Keep 3",
            "    Debug.Print \"after\"",
            "End Sub",
            "Private Sub Keep(ByVal N As Long)",
            "    Dim H As THolder, E As THolder",
            "    Dim I As Long",
            "    Set H.O = New Noisy",
            "    H.O.Name = \"a\"",
            "    For I = 1 To N",
            "        H.N = H.N + I",
            "    Next",
            "    Debug.Print H.N",
            "    H = E",
            "    Debug.Print \"copied\"; H.N",
            "    Set H.O = New Noisy",
            "    H.O.Name = \"b\"",
            "    Set H.V = H.O",
            "    Set H.O = Nothing",
            "    Debug.Print \"dropped\"",
            "    H.V = N",
            "    Debug.Print \"numbered\"; H.V",
            "    Set H.O = New Noisy",
            "    H.O.Name = \"c\"",
            "    H.N = H.N + 1",
            "End Sub"), "Keep", recoveries: 14, classes: Noisy());

    // an Integer field that overflows is let-assigned nothing (MS-VBAL §5.4.3.1.1): the interpreter runs the assignment alone, and raises the error to the
    // handler; the field holds what it held before.
    [TestMethod]
    public async Task AFieldThatOverflows_IsRaisedByTheInterpreter_ToTheHandler()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Debug.Print Guarded()",
            "End Sub",
            "Private Function Guarded() As Long",
            "    Dim P As TPoint",
            "    Dim I As Long",
            "    On Error GoTo Overflowed",
            "    For I = 1 To 5",
            "        P.Small = P.Small + 10000",
            "    Next",
            "    Guarded = -1",
            "    Exit Function",
            "Overflowed:",
            "    Guarded = P.Small + I",
            "End Function"), "Guarded", recoveries: 1);

    // an error compiled code does not handle walks the activation back with the record as compiled code left it, half updated: the caller's handler reads
    // what was written before the error, and nothing of what was not - the Integer field that overflows is let-assigned nothing (MS-VBAL §5.4.3.1.1).
    [TestMethod]
    public async Task AWalkBackMidUpdate_LeavesTheRecordAsCompiledCodeLeftIt()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Dim P As TPoint",
            "    On Error Resume Next",
            "    Fill P, 1",
            "    Debug.Print Err.Number; P.X; P.Y; P.Small",
            "End Sub",
            "Private Sub Fill(ByRef P As TPoint, ByVal D As Long)",
            "    P.X = 7",
            "    P.Small = 32767 + D",
            "    P.Y = 1.5",
            "End Sub"), "Fill", recoveries: 0, walkBacks: 1);
}
