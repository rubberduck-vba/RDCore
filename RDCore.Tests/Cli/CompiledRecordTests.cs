using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A field of a record parameter or variable, compiled: the record held in the cell its frame holds it in, and each field read and
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

    // a class whose state is one record of Me's - `Private this As TState` - read and written by its members.
    private static (string Name, string Source)[] Grid(params string[] members)
        => [("Grid", ModuleWorkspace.ClassModule("Grid", ["Option Explicit",
            "Private Type TState", "    X As Long", "    Y As Double", "    V As Variant", "    Inner As TCount", "End Type",
            "Private Type TCount", "    N As Integer", "End Type",
            "Private this As TState",
            "Private Saved As TState",
            "Public Property Get X() As Long", "    X = this.X", "End Property",
            "Public Property Let X(ByVal Value As Long)", "    this.X = Value", "End Property",
            .. members]))];

    // `this.X` is read and written where Me holds the record: each object its own, what a Property Let assigns it read by compiled code, and what compiled
    // code writes read by a Property Get.
    [TestMethod]
    public async Task TheFieldsOfThis_AreEachObjectsOwn()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As New Grid, B As New Grid",
            "    A.X = 10",
            "    Debug.Print A.Walk(3); B.Walk(4); A.Walk(1); B.X; A.X",
            "End Sub"), "Walk", recoveries: 0, classes: Grid(
                "Public Function Walk(ByVal N As Long) As Double",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        this.X = this.X + I",
                "        this.Y = this.Y + this.X / 4",
                "        this.Inner.N = this.Inner.N + 1",
                "    Next",
                "    Walk = this.X * 1000 + this.Y + this.Inner.N / 100",
                "End Function"));

    // a Let of the whole record (MS-VBAL §5.4.3.8) is the interpreter's, and copies another record over Me's: what compiled code reads after it is the copy,
    // and what it writes is in Me's record, not in the one copied.
    [TestMethod]
    public async Task ThisAssignedAWholeRecord_IsReadAndWrittenAfterwards()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As New Grid",
            "    Debug.Print A.Walk(3);",
            "    A.Reset 7",
            "    Debug.Print A.Walk(2);",
            "    A.Reset 1",
            "    Debug.Print A.Walk(1); A.X",
            "End Sub"), "Walk", recoveries: 0, classes: Grid(
                "Public Sub Reset(ByVal N As Long)",
                "    Saved.X = N",
                "    this = Saved",
                "    Saved.X = -1",
                "End Sub",
                "Public Function Walk(ByVal N As Long) As Double",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        this.X = this.X + I",
                "        this.Y = this.Y + 0.5",
                "    Next",
                "    Walk = this.X + this.Y",
                "End Function"));

    // a Variant field of this holds what it is assigned of its own subtype, as a Variant field of a local record does; Empty holds no payload, so the first
    // sum of an object is the interpreter's.
    [TestMethod]
    public async Task AVariantFieldOfThis_IsReadAndWrittenAsAVariantVariableIs()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim A As New Grid",
            "    Debug.Print A.Summed(3); A.Summed(2)",
            "End Sub"), "Summed", recoveries: 1, classes: Grid(
                "Public Function Summed(ByVal N As Long) As Long",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        this.V = this.V + I",
                "    Next",
                "    Summed = this.V",
                "End Function"));

    // a record of a standard module is held in the session: what one procedure's compiled code writes in it, another's reads, and the interpreter too.
    [TestMethod]
    public async Task ARecordOfAModule_IsHeldInTheSession()
        => await AssertAlikeAsync(Module(
            "Private Kept As TPair",
            "Public Sub Main()",
            "    Fill 4",
            "    Debug.Print Kept.Count; Kept.Inner.X; Read()",
            "    Kept.Count = 100",
            "    Fill 1",
            "    Debug.Print Read()",
            "End Sub",
            "Private Sub Fill(ByVal N As Long)",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Kept.Count = Kept.Count + 1",
            "        Kept.Inner.X = Kept.Inner.X + I",
            "    Next",
            "End Sub",
            "Private Function Read() As Long",
            "    Read = Kept.Count * 1000 + Kept.Inner.X",
            "End Function"), "Fill", recoveries: 0);
}
