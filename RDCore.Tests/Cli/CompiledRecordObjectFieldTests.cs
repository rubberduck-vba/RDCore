using RDCore.Runtime.Compiled;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using static RDCore.Tests.Cli.CompiledErrorHandlingTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// An object field of a record, compiled: the object held in the field's own cell, which compiled code finds through the record's holder each time, and
/// reads, compares and Sets there as it does an object variable: counted where the field holds it, and let go of where the interpreter lets go of it. A
/// program runs the same either way, <c>Class_Terminate</c> included.
/// </summary>
[TestClass]
public sealed class CompiledRecordObjectFieldTests
{
    private static readonly string[] Types =
    [
        "Private Type THolder",
        "    O As Noisy",
        "    P As Noisy",
        "    N As Long",
        "    Small As Integer",
        "End Type",
    ];

    private static string Module(params string[] lines) => Program([.. Types, .. lines]);

    // a class whose objects say when they terminate, and one of another class.
    private static (string Name, string Source)[] Noisy()
        =>
        [
            ("Noisy", ModuleWorkspace.ClassModule("Noisy", ["Option Explicit", "Public Name As String",
                "Private Sub Class_Terminate()", "    Debug.Print \"gone \" & Name", "End Sub"])),
            ("Quiet", ModuleWorkspace.ClassModule("Quiet", ["Option Explicit", "Public Name As String"])),
        ];

    // a field Set to an object variable, to another field and to Nothing, and each compared with Is and tested with TypeOf, where the record holds it: the
    // object is held by the caller's variable throughout, so no Set lets go of the last reference to it, and compiled code runs each one.
    [TestMethod]
    public async Task AnObjectField_IsSetAndComparedWhereTheRecordHoldsIt()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Dim A As Noisy",
            "    Set A = New Noisy",
            "    A.Name = \"a\"",
            "    Debug.Print Hold(A, 3)",
            "    Debug.Print \"after\"",
            "End Sub",
            "Private Function Hold(ByVal A As Noisy, ByVal N As Long) As Long",
            "    Dim H As THolder",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        If H.O Is Nothing Then H.N = H.N + 1",
            "        Set H.O = A",
            "        If H.O Is A Then H.N = H.N + 10",
            "        Set H.P = H.O",
            "        If H.P Is H.O Then H.N = H.N + 100",
            "        If TypeOf H.P Is Noisy Then H.N = H.N + 1000",
            "        Set H.O = Nothing",
            "        If H.O Is H.P Then H.N = H.N + 10000",
            "    Next",
            "    Hold = H.N",
            "End Function"), "Hold", recoveries: 0, classes: Noisy(), expected: "3333");

    // an object field of a record that is itself a field of a record is Set and compared in place, in the inner record the outer one holds.
    [TestMethod]
    public async Task AnObjectFieldOfARecordInARecord_IsSetInPlace()
        => await AssertAlikeAsync(Program([.. Types,
            "Private Type TPair",
            "    Inner As THolder",
            "    Other As THolder",
            "End Type",
            "Public Sub Main()",
            "    Dim A As Noisy",
            "    Set A = New Noisy",
            "    A.Name = \"a\"",
            "    Debug.Print Nest(A)",
            "    Set A = Nothing",
            "    Debug.Print \"after\"",
            "End Sub",
            "Private Function Nest(ByVal A As Noisy) As Long",
            "    Dim P As TPair",
            "    Set P.Inner.O = A",
            "    If P.Other.O Is Nothing Then Nest = 1",
            "    If P.Inner.O Is A Then Nest = Nest + 10",
            "    Set P.Other.P = P.Inner.O",
            "    If P.Other.P Is P.Inner.O Then Nest = Nest + 100",
            "    If P.Inner.P Is Nothing Then Nest = Nest + 1000",
            "End Function"]), "Nest", recoveries: 0, classes: Noisy(), expected: "1111 ; gone a ; after");

    // a Set that lets go of the last reference to the object the field held destroys it: the interpreter runs that Set, and its Class_Terminate runs where
    // MS-VBA runs it, before the statement after the Set. A Set that lets go of an object something else still holds is compiled code's.
    [TestMethod]
    public async Task AnObjectAFieldLetsGoOfLast_TerminatesAtTheSet()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Debug.Print Swap(3)",
            "    Debug.Print \"after\"",
            "End Sub",
            "Private Function Swap(ByVal N As Long) As Long",
            "    Dim H As THolder",
            "    Dim B As Noisy",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Set B = New Noisy",
            "        B.Name = \"n\" & I",
            "        Set H.O = B",
            "        Debug.Print \"set\"; I",
            "        Set B = Nothing",
            "    Next",
            "    Debug.Print \"loop done\"",
            "    Set H.O = Nothing",
            "    Debug.Print \"cleared\"",
            "    Swap = N",
            "End Function"), "Swap", recoveries: 9, classes: Noisy(), expected: "set 1 ; gone n1 ; set 2 ; gone n2 ; set 3 ; loop done ; gone n3 ; cleared");

    // a variable Set to the object a field holds holds a counted reference of its own: the field can let go of it and the object lives on, and the Set that
    // lets go of the last reference to what the variable held before is the interpreter's, which runs its Class_Terminate there.
    [TestMethod]
    public async Task AVariableSetToAField_HoldsTheObjectOfItsOwn()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Debug.Print Take(3)",
            "End Sub",
            "Private Function Take(ByVal N As Long) As Long",
            "    Dim H As THolder",
            "    Dim X As Noisy",
            "    Dim I As Long",
            "    Set X = New Noisy",
            "    X.Name = \"first\"",
            "    Set H.O = X",
            "    For I = 1 To N",
            "        Set X = H.O",
            "        If X Is H.O Then Take = Take + 1",
            "    Next",
            "    Set H.P = New Noisy",
            "    H.P.Name = \"second\"",
            "    Set H.O = Nothing",
            "    Set X = H.P",
            "    Debug.Print \"taken\"",
            "    Set H.P = Nothing",
            "    Debug.Print \"kept\"; X.Name",
            "End Function"), "Take", recoveries: 3, classes: Noisy(), expected: "gone first ; taken ; keptsecond ; gone second ; 3");

    // a Set of a field to an object of another class than it is declared as does not Set-coerce (MS-VBAL §5.5.2.2): the interpreter runs the Set, and raises
    // the error to the handler, and the field holds what it held before.
    [TestMethod]
    public async Task AFieldSetToAnObjectOfAnotherClass_IsRaisedByTheInterpreter_ToTheHandler()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Debug.Print Guarded()",
            "End Sub",
            "Private Function Guarded() As Long",
            "    Dim H As THolder",
            "    Dim A As Noisy",
            "    Dim Q As Object",
            "    Set A = New Noisy",
            "    A.Name = \"kept\"",
            "    Set Q = New Quiet",
            "    Set H.O = A",
            "    On Error GoTo Mismatch",
            "    Set H.O = Q",
            "    Guarded = -1",
            "    Exit Function",
            "Mismatch:",
            "    If H.O Is A Then Guarded = Err.Number",
            "End Function"), "Guarded", recoveries: 2, classes: Noisy(), expected: "13");

    // an error compiled code does not handle walks the activation back with the record as compiled code left it: the object a field was Set to is the
    // caller's record's, counted there, and the caller's handler reads it.
    [TestMethod]
    public async Task AWalkBackAfterAFieldWasSet_LeavesTheObjectInTheField()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Dim H As THolder",
            "    Dim X As Noisy",
            "    Set X = New Noisy",
            "    X.Name = \"w\"",
            "    On Error Resume Next",
            "    Fill H, X, 1",
            "    Debug.Print Err.Number; H.O Is X; H.N",
            "    Set X = Nothing",
            "    Debug.Print \"x dropped\"",
            "    Set H.O = Nothing",
            "    Debug.Print \"h dropped\"",
            "End Sub",
            "Private Sub Fill(ByRef H As THolder, ByVal X As Noisy, ByVal D As Long)",
            "    Set H.O = X",
            "    H.N = 7",
            "    H.Small = 32767 + D",
            "    H.N = 8",
            "End Sub"), "Fill", recoveries: 0, walkBacks: 1, classes: Noisy(), expected: "6 True 7 ; x dropped ; gone w ; h dropped");

    // LSet of a record over another (MS-VBAL §5.4.3.6) is the interpreter's, and gives each object field a counted copy of the other's reference: the object
    // is then held by both fields, and a Set of either to Nothing lets go of it only when the other no longer holds it.
    [TestMethod]
    public async Task ARecordLSetOverAnother_HoldsItsObjectCounted()
        => await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Debug.Print Copy()",
            "    Debug.Print \"after\"",
            "End Sub",
            "Private Function Copy() As Long",
            "    Dim A As THolder, B As THolder",
            "    Dim X As Noisy",
            "    Set X = New Noisy",
            "    X.Name = \"x\"",
            "    Set A.O = X",
            "    Set X = Nothing",
            "    LSet B = A",
            "    If B.O Is A.O Then Copy = 1",
            "    Set A.O = Nothing",
            "    Debug.Print \"a cleared\"; B.O Is Nothing",
            "    Set B.O = Nothing",
            "    Debug.Print \"b cleared\"",
            "End Function"), "Copy", recoveries: 4, classes: Noisy(), expected: "a clearedFalse ; gone x ; b cleared ; 1");

    // a call that passes a field is the interpreter's, run as a support call, which binds a parameter by reference to the field's cell: what the callee Sets the parameter to is in
    // the field, counted there, and read by compiled code after the call; a parameter by value is given a counted copy of the reference.
    [TestMethod]
    public async Task AFieldPassedToAProcedure_IsTheFieldTheCalleeSets()
    {
        var result = await AssertAlikeAsync(Module(
            "Public Sub Main()",
            "    Debug.Print Passes()",
            "    Debug.Print \"after\"",
            "End Sub",
            "Private Sub Renew(ByRef O As Noisy, ByVal Name As String)",
            "    Set O = New Noisy",
            "    O.Name = Name",
            "End Sub",
            "Private Sub Look(ByVal O As Noisy)",
            "    Debug.Print \"looked at \"; O.Name",
            "End Sub",
            "Private Function Passes() As Long",
            "    Dim H As THolder",
            "    Renew H.O, \"first\"",
            "    Set H.P = H.O",
            "    Renew H.O, \"second\"",
            "    Look H.O",
            "    If Not H.P Is H.O Then Passes = 1",
            "    Set H.P = Nothing",
            "    Debug.Print \"p cleared\"",
            "End Function"), "Passes", recoveries: 1, classes: Noisy(), expected: "p cleared");
        Assert.IsFalse(result.Stepped.Any(reason => reason is { Kind: IneligibilityKind.Call, Detail: "Renew" }),
            $"a call given a field is a support call, which steps nothing: {string.Join("; ", result.Stepped.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
    }

    // a class whose state is one record of Me's, with an object field: `this.Owner`.
    private static (string Name, string Source)[] Nodes()
        =>
        [
            ("Node", ModuleWorkspace.ClassModule("Node", ["Option Explicit",
                "Private Type TState", "    Owner As Node", "    Name As String", "    Links As Long", "End Type",
                "Private this As TState",
                "Public Property Let Name(ByVal Value As String)", "    this.Name = Value", "End Property",
                "Private Sub Class_Terminate()", "    Debug.Print \"gone \" & this.Name", "End Sub",
                "Public Sub Attach(ByVal Parent As Node)",
                "    If this.Owner Is Parent Then Exit Sub",
                "    Set this.Owner = Parent",
                "    this.Links = this.Links + 1",
                "End Sub",
                "Public Function IsChildOf(ByVal Parent As Node) As Boolean",
                "    IsChildOf = this.Owner Is Parent",
                "End Function",
                "Public Sub Detach()",
                "    Set this.Owner = Nothing",
                "End Sub",
                "Public Property Get Links() As Long", "    Links = this.Links", "End Property",
                "Public Property Get Owner() As Node", "    Set Owner = this.Owner", "End Property"])),
        ];

    // `this.Owner` is each object's own, Set and compared where Me holds the record: an owner held only by a child's field lives as long as the child holds it,
    // and goes when the child lets go of it.
    [TestMethod]
    public async Task TheOwnerOfThis_IsEachObjectsOwn()
    {
        var program = Program(
            "Public Sub Main()",
            "    Dim P As Node, C As Node, D As Node",
            "    Set P = New Node",
            "    P.Name = \"p\"",
            "    Set C = New Node",
            "    C.Name = \"c\"",
            "    Set D = New Node",
            "    D.Name = \"d\"",
            "    C.Attach P",
            "    C.Attach P",
            "    D.Attach C",
            "    Debug.Print C.IsChildOf(P); C.IsChildOf(D); D.IsChildOf(C); C.Links; D.Links",
            "    Set P = Nothing",
            "    Debug.Print \"p dropped\"",
            "    C.Attach D",
            "    Debug.Print \"c moved\"; C.IsChildOf(D); C.Links; C.Owner Is D; P Is Nothing",
            "    D.Detach",
            "    Debug.Print \"d detached\"; D.IsChildOf(Nothing)",
            "    Set D = Nothing",
            "    Debug.Print \"d dropped\"",
            "End Sub");
        var compiled = await AssertAlikeAsync(program, Nodes(), expected: "TrueFalseTrue 1  1 ; p dropped ; gone p ; c movedTrue 2 TrueTrue ; d detachedTrue ; d dropped ; gone c ; gone d");
        foreach (var name in new[] { "Attach", "IsChildOf", "Detach", "Owner" })
        {
            Assert.IsTrue(compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
        }
    }

    // a record of a standard module holds its object field in the session: what one procedure Sets it to, another reads, and the object lives until the
    // field lets go of it.
    [TestMethod]
    public async Task AnObjectFieldOfAModulesRecord_IsHeldInTheSession()
        => await AssertAlikeAsync(Program([.. Types,
            "Private State As THolder",
            "Public Sub Main()",
            "    Dim X As Noisy",
            "    Set X = New Noisy",
            "    X.Name = \"m\"",
            "    Keep X, 4",
            "    Set X = Nothing",
            "    Debug.Print \"kept\"; State.O Is Nothing; State.N",
            "    Set State.O = Nothing",
            "    Debug.Print \"released\"",
            "End Sub",
            "Private Sub Keep(ByVal X As Noisy, ByVal N As Long)",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Set State.O = X",
            "        If State.O Is X Then State.N = State.N + 1",
            "    Next",
            "End Sub"]), "Keep", recoveries: 0, classes: Noisy(), expected: "keptFalse 4 ; gone m ; released");
}
