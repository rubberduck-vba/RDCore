using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A statement whose target is bound (IR lowering S0.11) - a <c>Set</c>, an <c>LSet</c> or an <c>RSet</c> of a variable through its slot, a member of an
/// object or a field of a record assigned through the member found once for each class, a <c>Mid</c> statement's operands let-coerced by their plans -
/// writes what MS-VBAL 5.4.3 says it writes, and raises what it says it raises.
/// </summary>
[TestClass]
public sealed class BoundStatementTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
    [
        ("Point", ClassModule("Point",
            "Public X As Long",
            "Public Y As Variant",
            "Public Other As Point")),
        ("INamed", ClassModule("INamed",
            "Public Name As String")),
        ("Labelled", ClassModule("Labelled",
            "Implements INamed",
            "Private mName As String",
            "Private Property Get INamed_Name() As String", "    INamed_Name = \"label:\" & mName", "End Property",
            "Private Property Let INamed_Name(ByVal Value As String)", "    mName = Value", "End Property")),
        ("Titled", ClassModule("Titled",
            "Implements INamed",
            "Private mName As String",
            "Private Property Get INamed_Name() As String", "    INamed_Name = \"title:\" & mName", "End Property",
            "Private Property Let INamed_Name(ByVal Value As String)", "    mName = UCase$(Value)", "End Property")),
    ];

    private static readonly IReadOnlyList<(string Name, string Source)> Modules =
    [
        ("Globals", StandardModule("Globals",
            "Public Kept As Point")),
    ];

    private static Task<string[]> RunMainAsync(params string[] body)
        => RunAsync(Classes, Modules, $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", body)}\r\n");

    private static Task<string[]> RunMainBodyAsync(params string[] body)
        => RunMainAsync(["Public Sub Main()", .. body, "End Sub"]);

    // MS-VBAL 5.4.3.9: Set assigns the reference - to a local, a variable of the module, and a variable named through its module - and the object the
    // variable held before loses one.
    [TestMethod]
    public async Task SetOfAVariable_AssignsTheReference_WhereverTheVariableIs()
        => CollectionAssert.AreEqual(new[] { "7", "7", "7", "True", "True" }, await RunMainBodyAsync(
            "Dim p As New Point, q As Point, I As Long",
            "p.X = 7",
            "For I = 1 To 3", "    Set q = p", "Next",
            "Debug.Print q.X",
            "Set Kept = q",
            "Debug.Print Kept.X",
            "Set Globals.Kept = Nothing", "Set Globals.Kept = p",
            "Debug.Print Globals.Kept.X",
            "Debug.Print Kept Is p",
            "Set q = Nothing", "Debug.Print q Is Nothing"));

    // MS-VBAL 5.3.1.11: a Variant reference parameter bound to a variable of another type is Set-assigned as that variable is declared: an object of
    // another class does not fit it.
    [TestMethod]
    public async Task SetOfAVariantReferenceParameter_IsSetAsTheVariableItIsBoundTo()
        => CollectionAssert.AreEqual(new[] { "3", "13" }, await RunMainAsync(
            "Private Sub Assign(ByRef Target As Variant, ByVal Value As Object)",
            "    Set Target = Value",
            "End Sub",
            "Public Sub Main()",
            "    Dim p As Point, q As New Point",
            "    q.X = 3",
            "    Assign p, q",
            "    Debug.Print p.X",
            "    On Error Resume Next",
            "    Assign p, New Collection",
            "    Debug.Print Err.Number",
            "End Sub"));

    [TestMethod]
    public async Task SetOfAFunctionsName_SetsItsResult()
        => CollectionAssert.AreEqual(new[] { "9" }, await RunMainAsync(
            "Private Function Made() As Point",
            "    Dim p As New Point",
            "    p.X = 9",
            "    Set Made = p",
            "End Function",
            "Public Sub Main()",
            "    Debug.Print Made().X",
            "End Sub"));

    // MS-VBAL 5.4.3.8: a public variable of an object is its storage, which the value is let-coerced to the declared type of - an overflow is error 6,
    // and a String that is a number is one.
    [TestMethod]
    public async Task LetOfAPublicVariableOfAnObject_CoercesToItsDeclaredType()
        => CollectionAssert.AreEqual(new[] { "4", "12", "2.5", "6", "12" }, await RunMainBodyAsync(
            "Dim p As New Point, I As Long",
            "For I = 1 To 4", "    p.X = I", "Next",
            "Debug.Print p.X",
            "p.X = \"12\"", "Debug.Print p.X",
            "p.Y = 2.5", "Debug.Print p.Y",
            "On Error Resume Next",
            "p.X = 3000000000#", "Debug.Print Err.Number",
            "Debug.Print p.X"));

    // MS-VBAL 5.3.1.9: through the interface, the member assigned is the Property Let the object's class implements it with - a different one for each
    // class, at the same statement.
    [TestMethod]
    public async Task LetOfAMemberOfAnInterface_IsThePropertyLetOfTheObjectsClass_WhicheverItIs()
        => CollectionAssert.AreEqual(new[] { "label:a", "title:B", "label:c", "title:D" }, await RunMainBodyAsync(
            "Dim names(1) As INamed, I As Long, Pass As Long, n As INamed",
            "Set names(0) = New Labelled", "Set names(1) = New Titled",
            "For Pass = 0 To 1",
            "    For I = 0 To 1",
            "        Set n = names(I)",
            "        n.Name = Chr$(97 + 2 * Pass + I)",
            "        Debug.Print n.Name",
            "    Next",
            "Next"));

    // MS-VBAL 5.6.15: two members of the target of the same With block are two members.
    [TestMethod]
    public async Task LetOfMembersOfTheTargetOfAWithBlock_AssignsEachMember()
        => CollectionAssert.AreEqual(new[] { "1", "two", "3" }, await RunMainBodyAsync(
            "Dim p As New Point",
            "With p",
            "    .X = 1", "    .Y = \"two\"",
            "    Set .Other = New Point", "    .Other.X = 3",
            "End With",
            "Debug.Print p.X", "Debug.Print p.Y", "Debug.Print p.Other.X"));

    [TestMethod]
    public async Task LetOfAMemberOfNothing_IsError91()
        => CollectionAssert.AreEqual(new[] { "91" }, await RunMainBodyAsync(
            "Dim p As Point",
            "On Error Resume Next",
            "p.X = 1",
            "Debug.Print Err.Number"));

    // MS-VBAL 5.4.3.8: a field of a record is let-coerced to its declared type.
    [TestMethod]
    public async Task LetOfAFieldOfARecord_CoercesToItsDeclaredType()
        => CollectionAssert.AreEqual(new[] { "3", "12", "x", "6", "12" }, await RunMainAsync(
            "Private Type Pair",
            "    X As Long",
            "    Y As Variant",
            "End Type",
            "Public Sub Main()",
            "    Dim r As Pair, I As Long",
            "    For I = 1 To 3", "        r.X = I", "    Next",
            "    Debug.Print r.X",
            "    r.X = \"12\"", "    Debug.Print r.X",
            "    r.Y = \"x\"", "    Debug.Print r.Y",
            "    On Error Resume Next",
            "    r.X = 1E+20", "    Debug.Print Err.Number",
            "    Debug.Print r.X",
            "End Sub"));

    // MS-VBAL 5.4.3.5: the start and the length are let-coerced to Long - a Double rounds to even - and the value to String, whatever they are.
    [TestMethod]
    public async Task Mid_CoercesItsOperands()
        => CollectionAssert.AreEqual(new[] { "R5-VBA", "RD-7BA", "RD-VB9", "RDxxBA", "5" }, await RunMainBodyAsync(
            "Dim s As String, v As Variant",
            "s = \"RD-VBA\"",
            "Mid$(s, 2, 1) = 5", "Debug.Print s",
            "s = \"RD-VBA\"",
            "Mid$(s, 4.5, 1) = \"7\"", "Debug.Print s",
            "s = \"RD-VBA\"", "v = 9",
            "Mid(s, \"6\") = v", "Debug.Print s",
            "s = \"RD-VBA\"",
            "Mid(s, 3, 2) = \"xxxx\"", "Debug.Print s",
            "On Error Resume Next",
            "Mid(s, 0) = \"x\"", "Debug.Print Err.Number"));

    // MS-VBAL 5.4.3.6-7: LSet and RSet fit the value into the current width of the variable - a local, or the variable a parameter is bound to.
    [TestMethod]
    public async Task LSetAndRSet_FitTheValueIntoTheVariable()
        => CollectionAssert.AreEqual(new[] { "[ab  ]", "[  ab]", "[abcd]", "[xy   ]" }, await RunMainAsync(
            "Private Sub Fit(ByRef Target As String)",
            "    LSet Target = \"xy\"",
            "End Sub",
            "Public Sub Main()",
            "    Dim s As String, t As String",
            "    s = \"1234\"", "    LSet s = \"ab\"", "    Debug.Print \"[\" & s & \"]\"",
            "    s = \"1234\"", "    RSet s = \"ab\"", "    Debug.Print \"[\" & s & \"]\"",
            "    s = \"1234\"", "    LSet s = \"abcdef\"", "    Debug.Print \"[\" & s & \"]\"",
            "    t = \"12345\"", "    Fit t", "    Debug.Print \"[\" & t & \"]\"",
            "End Sub"));
}
