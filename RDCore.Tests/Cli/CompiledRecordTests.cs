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
            "End Sub"), "Keep", recoveries: 8, classes: Noisy());

    // an Integer field whose sum overflows (MS-VBAL §5.6.9.3) is let-assigned nothing (MS-VBAL §5.4.3.8): the interpreter runs the assignment alone, and
    // raises the error to the handler; the field holds what it held before.
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
    // what was written before the error, and nothing of what was not - the Integer field whose sum overflows (MS-VBAL §5.6.9.3) is let-assigned nothing
    // (MS-VBAL §5.4.3.8).
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

    // the program run both ways alike, with the procedure named compiled and recovering and walking back as many times as given (none named: whatever is
    // compiled), then what it printed: what MS-VBA prints.
    private static Task AssertPrintsAsync(string program, string? compiled, long recoveries, long walkBacks, params string[] printed)
        => AssertPrintsAsync([], program, compiled, recoveries, walkBacks, printed);

    // the same, in a workspace of the classes given.
    private static async Task AssertPrintsAsync(IReadOnlyList<(string Name, string Source)> classes, string program, string? compiled, long recoveries,
        long walkBacks, params string[] printed)
    {
        if (compiled is null)
        {
            _ = await AssertAlikeAsync(program, classes);
        }
        else
        {
            _ = await AssertAlikeAsync(program, compiled, recoveries, walkBacks, classes);
        }

        CollectionAssert.AreEqual(printed, await ModuleWorkspace.RunAsync(classes, program, RDCore.SDK.Runtime.Abstract.Execution.VBExecutionEngine.Interpreter));
    }

    // a field given by reference is the field (MS-VBAL §5.3.1.11): a Let of the field through its record is what the parameter reads after, and what the
    // parameter is assigned, the record's field holds. A field of a scalar type is written in its cell, compiled or not; a Variant field given by reference
    // is bound to the record and the field's position.
    [TestMethod]
    [DataRow("X", "Long", 0, 1)]
    [DataRow("Y", "Double", 0, 1)]
    [DataRow("Small", "Integer", 0, 1)]
    [DataRow("V", "Variant", 2, 0)]
    public async Task AFieldGivenByReference_IsTheField_WhenItIsLetThroughItsRecord(string field, string type, long recoveries, long walkBacks)
        => await AssertPrintsAsync(Module(
            "Public Sub Main()",
            "    Dim Q As TPoint",
            $"    Q.{field} = 1",
            "    Debug.Print Aliased(Q)",
            "End Sub",
            "Private Function Aliased(ByRef Q As TPoint) As Long",
            $"    Inner Q, Q.{field}",
            $"    Aliased = Q.{field}",
            "End Function",
            $"Private Sub Inner(ByRef Q As TPoint, ByRef N As {type})",
            $"    Q.{field} = Q.{field} + 3",
            "    Debug.Print N",
            "    N = N + 100",
            "End Sub"), "Inner", recoveries, walkBacks, "4", "104");

    // the same, of a record of the module that another procedure lets the field of: Touch's Let is compiled code's, with nothing handed to the
    // interpreter. Inner, the same run's other compiled procedure, walks back once: the counters count.
    [TestMethod]
    public async Task AFieldOfAModuleRecordGivenByReference_IsTheField_WhenAnotherProcedureLetsIt()
    {
        var program = Module(
            "Private M As TPoint",
            "Public Sub Main()",
            "    M.X = 1",
            "    Inner M.X",
            "    Debug.Print M.X",
            "End Sub",
            "Private Sub Inner(ByRef N As Long)",
            "    Touch",
            "    Debug.Print N",
            "    N = N + 100",
            "End Sub",
            "Private Sub Touch()",
            "    M.X = M.X + 3",
            "End Sub");
        await AssertPrintsAsync(program, "Touch", 0, 0, "4", "104");
        _ = await AssertAlikeAsync(program, "Inner", recoveries: 0, walkBacks: 1);
    }

    // the same, of a String field, which the interpreter lets in either engine.
    [TestMethod]
    public async Task AStringFieldGivenByReference_IsTheField_WhenItIsLetThroughItsRecord()
        => await AssertPrintsAsync(Module(
            "Private Type TText",
            "    S As String",
            "End Type",
            "Public Sub Main()",
            "    Dim Q As TText",
            "    Q.S = \"a\"",
            "    Inner Q, Q.S",
            "    Debug.Print Q.S",
            "End Sub",
            "Private Sub Inner(ByRef Q As TText, ByRef N As String)",
            "    Q.S = Q.S & \"b\"",
            "    Debug.Print N",
            "    N = N & \"!\"",
            "End Sub"), null, 0, 0, "ab", "ab!");

    // the same, of a Let of the whole record: the record is written where it is, so its field given by reference holds what the other record's field held,
    // and the field holds what the parameter is assigned after it. The Let of a record is the interpreter's in either engine.
    [TestMethod]
    public async Task AFieldGivenByReference_IsTheField_WhenItsWholeRecordIsLet()
        => await AssertPrintsAsync(Module(
            "Public Sub Main()",
            "    Dim Q As TPoint",
            "    Dim O As TPoint",
            "    Q.X = 1",
            "    O.X = 7",
            "    O.V = \"v\"",
            "    Inner Q, O, Q.X",
            "    Debug.Print Q.X; Q.V; O.X",
            "End Sub",
            "Private Sub Inner(ByRef Q As TPoint, ByRef O As TPoint, ByRef N As Long)",
            "    Q = O",
            "    Debug.Print N",
            "    N = N + 100",
            "End Sub"), null, 0, 0, "7", "107 v 7");

    // the same, of a field of a record field let a record, and of a field of an element of an array of records let one.
    [TestMethod]
    [DataRow("Q.Inner = O", "Q.Inner.X", "Private Q As TPair")]
    [DataRow("Q(1) = O", "Q(1).X", "Private Q(1 To 2) As TPoint")]
    public async Task AFieldGivenByReference_IsTheField_WhenARecordHoldingItIsLet(string let, string field, string declared)
        => await AssertPrintsAsync(Module(
            declared,
            "Public Sub Main()",
            "    Dim O As TPoint",
            $"    {field} = 1",
            "    O.X = 7",
            $"    Inner {field}, O",
            $"    Debug.Print {field}; O.X",
            "End Sub",
            "Private Sub Inner(ByRef N As Long, ByRef O As TPoint)",
            $"    {let}",
            "    Debug.Print N",
            "    N = N + 100",
            "End Sub"), null, 0, 0, "7", "107  7");

    // a record let where it is lets go of the object its field held as the Let is made, when nothing else holds it, and holds the one the other record's
    // field holds until the record's own variable ends: of a record variable, of an element of an array of records, and of a record field.
    [TestMethod]
    [DataRow("Dim Q As THolder", "Q", "ByRef Q As THolder", "Q")]
    [DataRow("Dim Q(1 To 1) As THolder", "Q(1)", "Q() As THolder", "Q()")]
    [DataRow("Dim Q As TOuter", "Q.Inner", "ByRef Q As TOuter", "Q")]
    public async Task AnObjectInARecordLetWhereItIs_TerminatesWhenTheLetLetsGoOfIt(string declared, string record, string parameter, string given)
        => await AssertPrintsAsync(Noisy(), Program(
            "Private Type THolder",
            "    Item As Noisy",
            "    Count As Long",
            "End Type",
            "Private Type TOuter",
            "    Inner As THolder",
            "End Type",
            "Public Sub Main()",
            "    Run",
            "    Debug.Print \"ended\"",
            "End Sub",
            "Private Sub Run()",
            $"    {declared}",
            "    Dim O As THolder",
            $"    Set {record}.Item = New Noisy",
            $"    {record}.Item.Name = \"a\"",
            $"    {record}.Count = 1",
            "    Set O.Item = New Noisy",
            "    O.Item.Name = \"b\"",
            $"    Inner {given}, O, {record}.Count",
            $"    Debug.Print \"after\"; {record}.Count",
            "    Set O.Item = Nothing",
            "    Debug.Print \"cleared\"",
            "End Sub",
            $"Private Sub Inner({parameter}, ByRef O As THolder, ByRef N As Long)",
            "    Debug.Print \"let\"",
            $"    {record} = O",
            "    Debug.Print \"done\"; N",
            "    N = N + 100",
            "End Sub"), null, 0, 0, "let", "gone a", "done 0", "after 100", "cleared", "gone b", "ended");

    private static readonly string[] Texts =
    [
        "Private Type TText",
        "    S As String",
        "    N As Long",
        "End Type",
        "Private Type TWrap",
        "    Inn As TText",
        "    K As Long",
        "End Type",
    ];

    // a record copied by LSet (MS-VBAL §5.4.3.6) holds its String and record fields in cells of its own: what is let to the copy after is not the
    // original's, and the other way around.
    [TestMethod]
    public async Task ARecordCopiedByLSet_SharesNoCellWithTheRecordItCopied()
        => await AssertPrintsAsync(Module([.. Texts,
            "Public Sub Main()",
            "    Dim A As TText, B As TText",
            "    Dim C As TWrap, D As TWrap",
            "    A.S = \"a\"",
            "    A.N = 1",
            "    LSet B = A",
            "    B.S = \"b\"",
            "    B.N = 2",
            "    Debug.Print A.S; A.N; B.S; B.N",
            "    C.Inn.S = \"c\"",
            "    C.Inn.N = 3",
            "    LSet D = C",
            "    D.Inn.S = \"d\"",
            "    D.Inn.N = 5",
            "    Debug.Print C.Inn.S; C.Inn.N; D.Inn.S; D.Inn.N",
            "End Sub"]), null, 0, 0, "a 1 b 2", "c 3 d 5");

    private static (string Name, string Source)[] Box()
        => [("Box", ModuleWorkspace.ClassModule("Box", ["Option Explicit", "Public Name As String"]))];

    // a field LSet leaves at its declared type's default, of a class, Object or Variant type, holds the default in a cell of its own: an object Set to it
    // after is the field's, and a variable of the type declared after holds the default. The default value of a type is one value every variable shares.
    [TestMethod]
    public async Task AFieldLSetLeavesAtItsDefault_HoldsItInACellOfItsOwn()
        => await AssertPrintsAsync(Box(), Program(
            "Private Type TSmall",
            "    N As Long",
            "End Type",
            "Private Type TBig",
            "    N As Long",
            "    O As Object",
            "    V As Variant",
            "    C As Box",
            "End Type",
            "Public Sub Main()",
            "    Dim A As TSmall, B As TBig",
            "    LSet B = A",
            "    Set B.O = New Box",
            "    B.O.Name = \"b\"",
            "    Set B.C = New Box",
            "    B.C.Name = \"c\"",
            "    Debug.Print B.O.Name; B.C.Name; B.O Is B.C",
            "    Dim Z As Box, W As Object, E As Variant",
            "    Debug.Print Z Is Nothing; W Is Nothing; IsEmpty(E)",
            "End Sub"), null, 0, 0, "bcFalse", "TrueTrueTrue");

    // a field of a class type is an object reference as a field of type Object is, with no bytes of its own: LSet gives the other record's field the object
    // the field holds, as it gives it one of type Object.
    [TestMethod]
    [DataRow("Box")]
    [DataRow("Object")]
    public async Task AnObjectFieldCopiedByLSet_HoldsTheObject_OfAClassTypeAsOfTypeObject(string type)
        => await AssertPrintsAsync(Box(), Program(
            "Private Type THold",
            $"    O As {type}",
            "    N As Long",
            "End Type",
            "Public Sub Main()",
            "    Dim A As THold, B As THold",
            "    Set A.O = New Box",
            "    A.O.Name = \"a\"",
            "    A.N = 3",
            "    LSet B = A",
            "    Debug.Print B.O Is Nothing; B.O.Name; B.N",
            "End Sub"), null, 0, 0, "Falsea 3");

    // an object Set to a field LSet left at its default is held by the field, and let go of when the record is: in either engine.
    [TestMethod]
    public async Task AnObjectSetToAFieldLSetLeftAtItsDefault_TerminatesWithTheRecord()
        => await AssertPrintsAsync(Noisy(), Program(
            "Private Type TSmall",
            "    N As Long",
            "End Type",
            "Private Type TBig",
            "    N As Long",
            "    O As Object",
            "    V As Variant",
            "    C As Noisy",
            "End Type",
            "Public Sub Main()",
            "    Run",
            "    Debug.Print \"ended\"",
            "End Sub",
            "Private Sub Run()",
            "    Dim S As TSmall, B As TBig",
            "    LSet B = S",
            "    Set B.O = New Noisy",
            "    B.O.Name = \"o\"",
            "    Set B.C = New Noisy",
            "    B.C.Name = \"c\"",
            "    B.V = 5",
            "    Debug.Print B.O.Name; B.C.Name; B.V",
            "End Sub"), null, 0, 0, "oc 5", "gone o", "gone c", "ended");

    // a field LSet leaves at its default keeps its bounds when it is a fixed-size array, and its own fields when it is a record: each element and field is
    // reset where it is.
    [TestMethod]
    public async Task AFixedSizeArrayFieldLSetLeavesAtItsDefault_KeepsItsBounds()
        => await AssertPrintsAsync(Module(
            "Private Type TSmall",
            "    N As Long",
            "End Type",
            "Private Type TBig",
            "    N As Long",
            "    S As String",
            "    A(1 To 2) As Long",
            "    Inner As TPair",
            "End Type",
            "Public Sub Main()",
            "    Dim S As TSmall, B As TBig",
            "    B.A(2) = 4",
            "    B.Inner.Inner.X = 3",
            "    LSet B = S",
            "    Debug.Print LBound(B.A); UBound(B.A); B.A(2); B.Inner.Inner.X",
            "    B.A(2) = 6",
            "    Debug.Print B.A(2)",
            "End Sub"), null, 0, 0, "1  2  0  0", "6");

    // a Variant or an object element of a fixed-size array field given by reference is the element when the record is let (MS-VBAL §5.3.1.11): it is
    // written where it is, as an element of a scalar type is. The object the element held is let go of as the Let is made.
    [TestMethod]
    [DataRow("Q = O")]
    [DataRow("LSet Q = O")]
    public async Task AVariantElementOfAFixedSizeArrayFieldGivenByReference_IsTheElement_WhenItsRecordIsLet(string let)
        => await AssertPrintsAsync(Module(
            "Private Type TVar",
            "    V(1 To 2) As Variant",
            "End Type",
            "Private Q As TVar, O As TVar",
            "Public Sub Main()",
            "    Q.V(1) = 1",
            "    O.V(1) = 7",
            "    Inner Q.V(1)",
            "    Debug.Print Q.V(1)",
            "End Sub",
            "Private Sub Inner(ByRef N As Variant)",
            $"    {let}",
            "    Debug.Print N",
            "    N = N + 100",
            "End Sub"), null, 0, 0, "7", "107");

    // the same, of an object element: the parameter is the element, which holds the other record's object after the Let, and the object it held is let go of.
    [TestMethod]
    [DataRow("Q = O")]
    [DataRow("LSet Q = O")]
    public async Task AnObjectElementOfAFixedSizeArrayFieldGivenByReference_IsTheElement_WhenItsRecordIsLet(string let)
        => await AssertPrintsAsync(Noisy(), Program(
            "Private Type TObj",
            "    O(1 To 1) As Noisy",
            "End Type",
            "Public Sub Main()",
            "    Run",
            "    Debug.Print \"ended\"",
            "End Sub",
            "Private Sub Run()",
            "    Dim Q As TObj, O As TObj",
            "    Set Q.O(1) = New Noisy",
            "    Q.O(1).Name = \"q\"",
            "    Set O.O(1) = New Noisy",
            "    O.O(1).Name = \"o\"",
            "    Inner Q, O, Q.O(1)",
            "    Debug.Print \"after\"",
            "End Sub",
            "Private Sub Inner(ByRef Q As TObj, ByRef O As TObj, ByRef X As Noisy)",
            $"    {let}",
            "    Debug.Print X.Name; Q.O(1).Name; X Is Q.O(1)",
            "End Sub"), null, 0, 0, "gone q", "ooTrue", "after", "gone o", "ended");

    private static readonly string[] Holders =
    [
        "Private Type THold",
        "    O As Noisy",
        "    N As Long",
        "End Type",
        "Private Type TPtr",
        "    P As LongPtr",
        "    N As Long",
        "End Type",
    ];

    // a program of the holder types whose Main runs Run, then prints "ended": what Run's locals hold is let go of before that.
    private static string Holding(params string[] run)
        => Program([.. Holders, "Public Sub Main()", "    Run", "    Debug.Print \"ended\"", "End Sub", "Private Sub Run()", .. run, "End Sub"]);

    // LSet of a record (MS-VBAL §5.4.3.6, implementation-defined) gives an object field the object the other record's field holds at the same offset, as
    // `Set B.O = A.O` would: a reference of its own, counted, in a cell of its own.
    [TestMethod]
    public async Task AnObjectFieldCopiedByLSet_IsAReferenceOfItsOwn()
        => await AssertPrintsAsync(Noisy(), Holding(
            "    Dim A As THold, B As THold",
            "    Set A.O = New Noisy",
            "    A.O.Name = \"a\"",
            "    LSet B = A",
            "    Debug.Print B.O Is A.O; B.O.Name",
            "    Set A.O = Nothing",
            "    Debug.Print \"cleared a\"",
            "    Set B.O = Nothing",
            "    Debug.Print \"cleared b\""), null, 0, 0, "Truea", "cleared a", "gone a", "cleared b", "ended");

    // an object Set to the field LSet copied is the field's: the other record keeps its own.
    [TestMethod]
    public async Task AnObjectSetToAFieldLSetCopied_LeavesTheOtherRecordAlone()
        => await AssertPrintsAsync(Noisy(), Holding(
            "    Dim A As THold, B As THold",
            "    Set A.O = New Noisy",
            "    A.O.Name = \"a\"",
            "    LSet B = A",
            "    Set B.O = New Noisy",
            "    B.O.Name = \"b\"",
            "    Debug.Print A.O.Name; B.O.Name",
            "    Set A.O = Nothing",
            "    Debug.Print \"cleared a\"",
            "    Set B.O = Nothing",
            "    Debug.Print \"cleared b\""), null, 0, 0, "ab", "gone a", "cleared a", "gone b", "cleared b", "ended");

    // the object the field held before LSet is let go of as the LSet is made.
    [TestMethod]
    public async Task AnObjectFieldLSetOver_LetsGoOfTheObjectItHeld()
        => await AssertPrintsAsync(Noisy(), Holding(
            "    Dim A As THold, B As THold",
            "    Set B.O = New Noisy",
            "    B.O.Name = \"old\"",
            "    Set A.O = New Noisy",
            "    A.O.Name = \"a\"",
            "    LSet B = A",
            "    Debug.Print \"lset\"",
            "    Set A.O = Nothing",
            "    Set B.O = Nothing",
            "    Debug.Print \"cleared\""), null, 0, 0, "gone old", "lset", "gone a", "cleared", "ended");

    // a record LSet copied into a local lets go of its own reference when the procedure ends: the record it copied keeps the object.
    [TestMethod]
    public async Task ALocalRecordLSetCopiedInto_LetsGoOfItsOwnReferenceWhenItsProcedureEnds()
        => await AssertPrintsAsync(Noisy(), Program([.. Holders,
            "Public Sub Main()",
            "    Run",
            "    Debug.Print \"ended\"",
            "End Sub",
            "Private Sub Run()",
            "    Dim A As THold",
            "    Set A.O = New Noisy",
            "    A.O.Name = \"a\"",
            "    Copy A",
            "    Debug.Print \"after \"; A.O.Name",
            "    Set A.O = Nothing",
            "    Debug.Print \"cleared\"",
            "End Sub",
            "Private Sub Copy(ByRef A As THold)",
            "    Dim B As THold",
            "    LSet B = A",
            "    Debug.Print \"copied \"; B.O.Name",
            "End Sub"]), null, 0, 0, "copied a", "after a", "gone a", "cleared", "ended");

    // the bytes of an object field LSet gives a field of another type are the object's address, what ObjPtr says it is.
    [TestMethod]
    public async Task AnObjectFieldLSetOntoALongPtr_IsTheObjectsAddress()
        => await AssertPrintsAsync(Noisy(), Holding(
            "    Dim A As THold, W As TPtr",
            "    Set A.O = New Noisy",
            "    A.O.Name = \"a\"",
            "    A.N = 7",
            "    LSet W = A",
            "    Debug.Print W.P = ObjPtr(A.O); W.P <> 0; W.N"), null, 0, 0, "TrueTrue 7", "gone a", "ended");

    // the address of an object LSet gives an object field is that object, counted: the field holds it after the variable it came from lets go of it.
    [TestMethod]
    public async Task AnObjectsAddressLSetOntoAnObjectField_IsTheObject()
        => await AssertPrintsAsync(Noisy(), Holding(
            "    Dim A As THold, W As TPtr, R As THold",
            "    Set A.O = New Noisy",
            "    A.O.Name = \"a\"",
            "    W.P = ObjPtr(A.O)",
            "    LSet R = W",
            "    Debug.Print R.O Is A.O; R.O.Name",
            "    Set A.O = Nothing",
            "    Debug.Print \"cleared a\"",
            "    Set R.O = Nothing",
            "    Debug.Print \"cleared r\""), null, 0, 0, "Truea", "cleared a", "gone a", "cleared r", "ended");

    // 0 LSet onto an object field is Nothing: the object the field held is let go of.
    [TestMethod]
    public async Task ZeroLSetOntoAnObjectField_IsNothing()
        => await AssertPrintsAsync(Noisy(), Holding(
            "    Dim W As TPtr, R As THold",
            "    Set R.O = New Noisy",
            "    R.O.Name = \"r\"",
            "    LSet R = W",
            "    Debug.Print \"lset\"; R.O Is Nothing"), null, 0, 0, "gone r", "lsetTrue", "ended");

    // a number that is no object's address LSet onto an object field is no object: error 424, and nothing of the record is written.
    [TestMethod]
    public async Task ANumberThatIsNoObjectsAddressLSetOntoAnObjectField_IsAnError()
        => await AssertPrintsAsync(Noisy(), Holding(
            "    On Error Resume Next",
            "    Dim W As TPtr, R As THold",
            "    Set R.O = New Noisy",
            "    R.O.Name = \"r\"",
            "    W.P = 12345",
            "    W.N = 7",
            "    LSet R = W",
            "    Debug.Print Err.Number; R.O.Name; R.N"), null, 0, 0, "424 r 0", "gone r", "ended");

    // the objects of a fixed-size array field LSet copies are each element's own reference, counted; the ones the elements held are let go of.
    [TestMethod]
    public async Task AnObjectElementOfAFixedSizeArrayFieldCopiedByLSet_IsAReferenceOfItsOwn()
        => await AssertPrintsAsync(Noisy(), Program(
            "Private Type TArr",
            "    O(1 To 2) As Noisy",
            "End Type",
            "Public Sub Main()",
            "    Run",
            "    Debug.Print \"ended\"",
            "End Sub",
            "Private Sub Run()",
            "    Dim A As TArr, B As TArr",
            "    Set A.O(1) = New Noisy",
            "    A.O(1).Name = \"a\"",
            "    Set B.O(1) = New Noisy",
            "    B.O(1).Name = \"old\"",
            "    LSet B = A",
            "    Debug.Print B.O(1) Is A.O(1)",
            "    Set A.O(1) = Nothing",
            "    Debug.Print \"cleared a\"",
            "    Set B.O(1) = Nothing",
            "    Debug.Print \"cleared b\"",
            "End Sub"), null, 0, 0, "gone old", "True", "cleared a", "gone a", "cleared b", "ended");

    // an object set to a Variant field given by reference is the field's: it is let go of when the field is cleared, set to Nothing, or ends with its record.
    [TestMethod]
    public async Task AnObjectSetToAVariantFieldGivenByReference_IsLetGoOfWithTheField()
        => await AssertPrintsAsync(Noisy(), Program(
            "Private Type TVar",
            "    V As Variant",
            "    N As Long",
            "End Type",
            "Public Sub Main()",
            "    Run",
            "    Debug.Print \"ended\"",
            "End Sub",
            "Private Sub Run()",
            "    Dim A As TVar",
            "    Hold A.V, \"a\"",
            "    A.V = Empty",
            "    Debug.Print \"cleared\"",
            "    Hold A.V, \"b\"",
            "    Set A.V = Nothing",
            "    Debug.Print \"nothing\"",
            "    Hold A.V, \"c\"",
            "    Debug.Print \"left\"",
            "End Sub",
            "Private Sub Hold(ByRef V As Variant, ByVal Name As String)",
            "    Set V = New Noisy",
            "    V.Name = Name",
            "End Sub"), null, 0, 0, "gone a", "cleared", "gone b", "nothing", "left", "gone c", "ended");

    // an object a Variant field holds is the Variant field's own reference once LSet copied it, counted, as an object field's is.
    [TestMethod]
    public async Task AnObjectInAVariantFieldCopiedByLSet_IsAReferenceOfItsOwn()
        => await AssertPrintsAsync(Noisy(), Program(
            "Private Type TVar",
            "    V As Variant",
            "    N As Long",
            "End Type",
            "Public Sub Main()",
            "    Run",
            "    Debug.Print \"ended\"",
            "End Sub",
            "Private Sub Run()",
            "    Dim A As TVar, B As TVar",
            "    Hold A.V, \"v\"",
            "    LSet B = A",
            "    Debug.Print B.V Is A.V; B.V.Name",
            "    A.V = Empty",
            "    Debug.Print \"cleared a\"",
            "    B.V = Empty",
            "    Debug.Print \"cleared b\"",
            "End Sub",
            "Private Sub Hold(ByRef V As Variant, ByVal Name As String)",
            "    Set V = New Noisy",
            "    V.Name = Name",
            "End Sub"), null, 0, 0, "Truev", "cleared a", "gone v", "cleared b", "ended");

    // an array a Variant field holds is copied by LSet, as a Let of the Variant would copy it.
    [TestMethod]
    public async Task AnArrayInAVariantFieldCopiedByLSet_IsACopy()
        => await AssertPrintsAsync(Module(
            "Public Sub Main()",
            "    Dim A As TPoint, B As TPoint",
            "    A.V = Array(1, 2)",
            "    LSet B = A",
            "    B.V(0) = 9",
            "    Debug.Print A.V(0); B.V(0)",
            "End Sub"), null, 0, 0, "1  9");

    private static (string Name, string Source)[] Nodes()
        => [("Node", ModuleWorkspace.ClassModule("Node", ["Option Explicit", "Public Name As String", "Public Parent As Node", "Public Child As Node",
            "Private Sub Class_Terminate()", "    Debug.Print \"gone \" & Name", "End Sub"]))];

    // an RD-VBA divergence: the address of an object LSet onto an object field is a reference counted as any other, where MS-VBA copies the pointer
    // and counts nothing. A child given its parent that way - the weak reference MS-VBA programs make so - holds it, and a parent and a child that hold each
    // other are never let go of: neither Class_Terminate runs.
    [TestMethod]
    public async Task AWeakReferenceMadeByLSet_IsCounted_SoAParentAndAChildThatHoldEachOtherAreNeverLetGoOf()
        => await AssertPrintsAsync(Nodes(), Program(
            "Private Type TPtr",
            "    P As LongPtr",
            "End Type",
            "Private Type TRef",
            "    O As Node",
            "End Type",
            "Public Sub Main()",
            "    Run",
            "    Debug.Print \"ended\"",
            "End Sub",
            "Private Sub Run()",
            "    Dim Mom As Node, Kid As Node, W As TPtr, R As TRef",
            "    Set Mom = New Node",
            "    Mom.Name = \"mom\"",
            "    Set Kid = New Node",
            "    Kid.Name = \"kid\"",
            "    Set Mom.Child = Kid",
            "    W.P = ObjPtr(Mom)",
            "    LSet R = W",
            "    Set Kid.Parent = R.O",
            "    Debug.Print Kid.Parent.Name",
            "End Sub"), null, 0, 0, "mom", "ended");

    // a field given by reference is the field when its record is written by LSet, by Get, or let a record whose fixed-size array field holds the element
    // given: each writes the record where it is.
    [TestMethod]
    [DataRow("Private Q As TPoint", "Q.X", "LSet Q = O", "O.X = 7")]
    [DataRow("Private Q As TFixed", "Q.A(1)", "Q = O", "O.A(1) = 7")]
    [DataRow("Private Q As TPoint", "Q.X", "Get #1, 1, Q", "O.X = 7")]
    public async Task AFieldGivenByReference_IsTheField_WhenItsRecordIsWrittenWhereItIs(string declared, string field, string write, string given)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rdcore-record-alias-{Guid.NewGuid():N}.bin");
        try
        {
            await AssertPrintsAsync(Module(
                "Private Type TFixed",
                "    A(1 To 2) As Long",
                "End Type",
                declared,
                $"Private O As {(declared.EndsWith("TFixed", StringComparison.Ordinal) ? "TFixed" : "TPoint")}",
                "Public Sub Main()",
                $"    {field} = 1",
                $"    {given}",
                $"    Open \"{path}\" For Binary As #1",
                "    Put #1, 1, O",
                $"    Inner {field}",
                "    Close #1",
                $"    Debug.Print {field}",
                "End Sub",
                "Private Sub Inner(ByRef N As Long)",
                $"    {write}",
                "    Debug.Print N",
                "    N = N + 100",
                "End Sub"), null, 0, 0, "7", "107");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // an element of a resizable array field given by reference does not lock the field against a write of its whole record, as it locks it against ReDim and
    // Erase: the field takes the other record's array, bounds and all, and the parameter is still the element it was given, which the field no longer holds.
    [TestMethod]
    [DataRow("2", "Q = O", "main 7  2")]
    [DataRow("2", "LSet Q = O", "main 7  2")]
    [DataRow("3", "Q = O", "main 7  3")]
    public async Task AnElementOfAResizableArrayFieldGivenByReference_IsTheElementItWas_WhenItsRecordIsWritten(string upper, string write, string printed)
        => await AssertPrintsAsync(Module(
            "Private Type TDyn",
            "    D() As Long",
            "End Type",
            "Private Q As TDyn, O As TDyn",
            "Public Sub Main()",
            $"    ReDim Q.D(1 To 2): ReDim O.D(1 To {upper})",
            "    Q.D(1) = 1: O.D(1) = 7",
            "    Inner Q.D(1)",
            "    Debug.Print \"main\"; Q.D(1); UBound(Q.D)",
            "End Sub",
            "Private Sub Inner(ByRef N As Long)",
            $"    {write}",
            "    Debug.Print \"N\"; N",
            "    N = N + 100",
            "End Sub"), null, 0, 0, "N 1", printed);

    // a nested record is laid out for the host's pointers as the record it is in is: its LongPtr is as wide as the outer record's, whatever is beside it.
    [TestMethod]
    [DataRow("    P As LongPtr", "A.Inner.P = 5", "B.Inner.P", "5")]
    [DataRow("    L As Long|    P As LongPtr", "A.Inner.L = 3: A.Inner.P = 5", "B.Inner.L; B.Inner.P", "3  5")]
    public async Task ANestedRecordWithALongPtrField_IsCopiedByLSet(string fields, string set, string print, string printed)
        => await AssertPrintsAsync(Module([
            "Private Type TIn",
            .. fields.Split('|'),
            "End Type",
            "Private Type TOut",
            "    Inner As TIn",
            "End Type",
            "Public Sub Main()",
            "    Dim A As TOut, B As TOut",
            $"    {set}",
            "    LSet B = A",
            $"    Debug.Print {print}",
            "End Sub"]), null, 0, 0, printed);

    // an object field given by reference is the field when its record is written by LSet or let another record: the parameter holds the object the field
    // holds after the write, and the object the field held before is let go of.
    [TestMethod]
    [DataRow("Noisy", "LSet Q = O")]
    [DataRow("Noisy", "Q = O")]
    [DataRow("Object", "LSet Q = O")]
    [DataRow("Object", "Q = O")]
    public async Task AnObjectFieldGivenByReference_IsTheField_WhenItsRecordIsWritten(string type, string write)
        => await AssertPrintsAsync(Noisy(), Program(
            "Private Type TObj",
            $"    O As {type}",
            "    N As Long",
            "End Type",
            "Public Sub Main()",
            "    Run",
            "    Debug.Print \"ended\"",
            "End Sub",
            "Private Sub Run()",
            "    Dim Q As TObj, O As TObj",
            "    Set Q.O = New Noisy",
            "    Q.O.Name = \"q\"",
            "    Set O.O = New Noisy",
            "    O.O.Name = \"o\"",
            "    Inner Q, O, Q.O",
            "    Debug.Print \"after\"",
            "End Sub",
            $"Private Sub Inner(ByRef Q As TObj, ByRef O As TObj, ByRef X As {type})",
            $"    {write}",
            "    Debug.Print TypeName(X); X Is Q.O",
            "    Debug.Print X.Name",
            "End Sub"), null, 0, 0, "gone q", "NoisyTrue", "o", "after", "gone o", "ended");

    // the address of an object LSet onto an object field in a nested record, or onto an object element of a fixed-size array field, is that object, as it
    // is onto an object field of the record itself.
    [TestMethod]
    [DataRow("    Inner As THold", "R.Inner.O", "R.Inner.N")]
    [DataRow("    O(1 To 1) As Noisy|    N As Long", "R.O(1)", "R.N")]
    public async Task AnObjectsAddressLSetOntoAnObjectFieldInsideTheRecord_IsTheObject(string fields, string held, string number)
        => await AssertPrintsAsync(Noisy(), Program([.. Holders,
            "Private Type TDeep",
            .. fields.Split('|'),
            "End Type",
            "Public Sub Main()",
            "    Run",
            "    Debug.Print \"ended\"",
            "End Sub",
            "Private Sub Run()",
            "    Dim W As TPtr, R As TDeep, N As Noisy",
            "    Set N = New Noisy",
            "    N.Name = \"n\"",
            "    W.P = ObjPtr(N)",
            "    W.N = 7",
            "    LSet R = W",
            $"    Debug.Print {held} Is N; {held}.Name; {number}",
            "    Set N = Nothing",
            "    Debug.Print \"cleared n\"",
            "End Sub"]), null, 0, 0, "Truen 7", "cleared n", "gone n", "ended");

    // a number that is no object's address LSet onto an object field inside the record is an error as it is onto one of the record itself: error 424, and
    // nothing of the record is written.
    [TestMethod]
    [DataRow("    Inner As THold", "R.Inner.O", "R.Inner.N")]
    [DataRow("    O(1 To 1) As Noisy|    N As Long", "R.O(1)", "R.N")]
    public async Task ANumberThatIsNoObjectsAddressLSetOntoAnObjectFieldInsideTheRecord_IsAnError(string fields, string held, string number)
        => await AssertPrintsAsync(Noisy(), Program([.. Holders,
            "Private Type TDeep",
            .. fields.Split('|'),
            "End Type",
            "Public Sub Main()",
            "    Run",
            "    Debug.Print \"ended\"",
            "End Sub",
            "Private Sub Run()",
            "    On Error Resume Next",
            "    Dim W As TPtr, R As TDeep",
            $"    Set {held} = New Noisy",
            $"    {held}.Name = \"r\"",
            "    W.P = 12345",
            "    W.N = 7",
            "    LSet R = W",
            $"    Debug.Print Err.Number; {held}.Name; {number}",
            "End Sub"]), null, 0, 0, "424 r 0", "gone r", "ended");

    private static (string Name, string Source)[] Implementers()
        => [.. Noisy(),
            ("IFoo", ModuleWorkspace.ClassModule("IFoo", ["Option Explicit", "Public Sub Hello()", "End Sub"])),
            ("Impl", ModuleWorkspace.ClassModule("Impl", ["Option Explicit", "Implements IFoo", "Public Name As String", "Private Sub IFoo_Hello()", "End Sub",
                "Private Sub Class_Terminate()", "    Debug.Print \"gone \" & Name", "End Sub"]))];

    private static readonly string[] Classed =
    [
        "Private Type TObj",
        "    O As Object",
        "    N As Long",
        "End Type",
        "Private Type TImpl",
        "    O As Impl",
        "    N As Long",
        "End Type",
        "Private Type TFoo",
        "    O As IFoo",
        "    N As Long",
        "End Type",
        "Private Type TPtr",
        "    P As LongPtr",
        "    N As Long",
        "End Type",
    ];

    // LSet gives a class-typed field an object as Set would: an object of another class, whether a field of the other record holds it or the bytes are its
    // address, is a type mismatch (error 13), and nothing of the record is written.
    [TestMethod]
    [DataRow("A")]
    [DataRow("W")]
    public async Task AnObjectOfAnotherClassLSetOntoAClassTypedField_IsATypeMismatch(string source)
        => await AssertPrintsAsync(Implementers(), Program([.. Classed,
            "Public Sub Main()",
            "    Run",
            "    Debug.Print \"ended\"",
            "End Sub",
            "Private Sub Run()",
            "    On Error Resume Next",
            "    Dim A As TObj, W As TPtr, R As TImpl, N As Noisy",
            "    Set N = New Noisy",
            "    N.Name = \"n\"",
            "    Set R.O = New Impl",
            "    R.O.Name = \"r\"",
            "    R.N = 3",
            "    Set A.O = N",
            "    A.N = 9",
            "    W.P = ObjPtr(N)",
            "    W.N = 9",
            $"    LSet R = {source}",
            "    Debug.Print Err.Number; R.O.Name; R.N",
            "    Set N = Nothing",
            "    Set A.O = Nothing",
            "    Debug.Print \"cleared n\"",
            "End Sub"]), null, 0, 0, "13 r 3", "gone n", "cleared n", "gone r", "ended");

    // an object of a class that implements the interface a field is declared as is of the field's class: LSet gives it to the field, from the other
    // record's field or from the address of either of its interfaces.
    [TestMethod]
    [DataRow("A", "I")]
    [DataRow("W", "I")]
    [DataRow("W", "M")]
    public async Task AnObjectThatImplementsAFieldsInterface_IsCopiedByLSet(string source, string addressOf)
        => await AssertPrintsAsync(Implementers(), Program([.. Classed,
            "Public Sub Main()",
            "    Run",
            "    Debug.Print \"ended\"",
            "End Sub",
            "Private Sub Run()",
            "    On Error Resume Next",
            "    Dim A As TObj, W As TPtr, R As TFoo, I As IFoo, M As Impl",
            "    Set M = New Impl",
            "    M.Name = \"m\"",
            "    Set I = M",
            "    Set A.O = M",
            $"    W.P = ObjPtr({addressOf})",
            $"    LSet R = {source}",
            "    Debug.Print Err.Number; R.O Is I",
            "    Set I = Nothing",
            "    Set M = Nothing",
            "    Set A.O = Nothing",
            "    Debug.Print \"cleared\"",
            "End Sub"]), null, 0, 0, "0 True", "cleared", "gone m", "ended");

    // a fixed-size Byte array field LSet lays over the bytes of numbers holds those bytes: how a VBA program reads the bytes of a Currency, a Double or a
    // Long (little-endian, as the host lays them out).
    [TestMethod]
    [DataRow("    C As Currency", "A.C = 1", "B.B(0); B.B(1)", "16  39")]
    [DataRow("    D As Double", "A.D = 1", "B.B(6); B.B(7)", "240  63")]
    [DataRow("    L As Long|    M As Long", "A.L = 258: A.M = 1", "B.B(0); B.B(1); B.B(4)", "2  1  1")]
    public async Task AByteArrayFieldLSetOverNumbers_HoldsTheirBytes(string fields, string set, string print, string printed)
        => await AssertPrintsAsync(Module([
            "Private Type TNumbers",
            .. fields.Split('|'),
            "End Type",
            "Private Type TBytes",
            "    B(0 To 7) As Byte",
            "End Type",
            "Public Sub Main()",
            "    Dim A As TNumbers, B As TBytes",
            $"    {set}",
            "    LSet B = A",
            $"    Debug.Print {print}",
            "End Sub"]), null, 0, 0, printed);
}
