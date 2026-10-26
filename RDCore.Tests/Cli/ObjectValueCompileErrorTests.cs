using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.5.1.1</strong> Where a value is expected, an object stands for its default member's value (<strong>§5.5.1.2.13</strong>), and what
/// cannot have one is a compile error: the <c>Nothing</c> literal (VBC09304), an object whose default member takes an argument (VBC09339), and, where a
/// value of a declared type is expected, an object whose default member returns an object (VBC09310).
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA reported compiling the same statement, alone in a module of the same project, in Excel (64-bit, en-US): its compile
/// error, or none. An object of a class that has no default member, which MS-VBAL §5.5.1.1 makes statically invalid, compiles in MS-VBA, to raise error 438
/// when it runs: RD-VBA does the same (RD-VBAL §5.5.1.1), and reports RDC11015.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.1 Let-coercion")]
public sealed class ObjectValueCompileErrorTests
{
    private const string InvalidUseOfObject = "Invalid use of object";
    private const string ArgumentNotOptional = "Argument not optional";
    private const string TypeMismatch = "Type mismatch";
    private const string Compiles = "";

    private static (string, string) Cls(string name, params string[] body) => (name, ClassModule(name, body));

    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
    [
        Cls("DfltNum", "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "    Value = 42", "End Property"),
        Cls("DfltChain", "Public Property Get Value() As Object", "Attribute Value.VB_UserMemId = 0", "    Set Value = New DfltNum", "End Property"),
        Cls("DfltVar", "Public Property Get Value() As Variant", "Attribute Value.VB_UserMemId = 0", "    Value = 1", "End Property"),
        Cls("NoDflt", "Public Sub DoIt()", "End Sub"),
        Cls("DfltArg", "Public Function Value(ByVal i As Long) As Long", "Attribute Value.VB_UserMemId = 0", "    Value = i", "End Function"),
    ];

    private static async Task AssertCompiles(string statement, string expected)
    {
        var errors = await LoadErrorsAsync(Classes,
            "Attribute VB_Name = \"Program\"\r\n"
            + "Private Sub ByVar(ByVal x As Variant)\r\nEnd Sub\r\n"
            + "Private Sub ByLng(ByVal x As Long)\r\nEnd Sub\r\n"
            + "Private Sub ByRefLng(x As Long)\r\nEnd Sub\r\n"
            + "Private Function FnDbl(ByVal x As Double) As Double\r\nEnd Function\r\n"
            + "Public Sub Main()\r\n"
            + "    Dim v As Variant, l As Long, s As String, d As Double, c As Collection, b As Boolean\r\n"
            + $"    {statement}\r\nEnd Sub\r\n");

        if (expected.Length == 0)
        {
            Assert.IsEmpty(errors, string.Join(" | ", errors));
            return;
        }

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        Assert.StartsWith(expected, errors[0]);
    }

    [TestMethod]
    [DataRow("l = Nothing", InvalidUseOfObject)]
    [DataRow("l = New Collection", ArgumentNotOptional)]
    [DataRow("l = c", ArgumentNotOptional)]
    [DataRow("l = New DfltArg", ArgumentNotOptional)]
    [DataRow("l = New DfltChain", TypeMismatch)]
    [DataRow("l = New DfltVar", Compiles)]
    [DataRow("l = New DfltNum", Compiles)]
    // MS-VBAL 5.5.1.1 makes a class with no default member invalid; MS-VBA compiles it, and raises error 438 when it runs.
    [DataRow("l = New NoDflt", Compiles)]
    [DataRow("v = New NoDflt", Compiles)]
    // a Variant takes the object's value, whatever it is, when it runs.
    [DataRow("v = Nothing", Compiles)]
    [DataRow("v = New Collection", Compiles)]
    [DataRow("v = New DfltChain", Compiles)]
    public Task AnAssignment(string statement, string expected) => AssertCompiles(statement, expected);

    [TestMethod]
    [DataRow("ByLng Nothing", InvalidUseOfObject)]
    [DataRow("ByLng New Collection", ArgumentNotOptional)]
    [DataRow("ByLng New DfltChain", TypeMismatch)]
    [DataRow("ByLng New NoDflt", Compiles)]
    [DataRow("ByVar New NoDflt", Compiles)]
    [DataRow("ByRefLng Nothing", InvalidUseOfObject)]
    [DataRow("ByRefLng New DfltArg", ArgumentNotOptional)]
    [DataRow("ByRefLng New DfltChain", TypeMismatch)]
    [DataRow("d = FnDbl(Nothing)", InvalidUseOfObject)]
    [DataRow("d = FnDbl(New DfltArg)", ArgumentNotOptional)]
    [DataRow("d = FnDbl(New DfltChain)", TypeMismatch)]
    [DataRow("ByVar Nothing", Compiles)]
    [DataRow("ByVar New Collection", Compiles)]
    [DataRow("ByVar New DfltChain", Compiles)]
    public Task AnArgumentOfAProcedure(string statement, string expected) => AssertCompiles(statement, expected);

    [TestMethod]
    [DataRow("s = \"x\" & Nothing", InvalidUseOfObject)]
    [DataRow("l = 1 + Nothing", InvalidUseOfObject)]
    [DataRow("l = -Nothing", InvalidUseOfObject)]
    [DataRow("b = (Nothing = 1)", InvalidUseOfObject)]
    [DataRow("s = \"x\" & New Collection", ArgumentNotOptional)]
    [DataRow("l = Not New DfltArg", ArgumentNotOptional)]
    [DataRow("b = (New DfltArg = 1)", ArgumentNotOptional)]
    [DataRow("l = 1 + New DfltChain", TypeMismatch)]
    [DataRow("b = (New DfltChain = 1)", TypeMismatch)]
    [DataRow("s = \"x\" & New DfltChain", TypeMismatch)]
    // an object whose default member gives a value is that value; one with no default member has none (MS-VBA: error 438 when it runs).
    [DataRow("s = \"x\" & New DfltVar", Compiles)]
    [DataRow("l = 1 + New DfltVar", Compiles)]
    [DataRow("s = \"x\" & New NoDflt", Compiles)]
    [DataRow("l = 1 + New NoDflt", Compiles)]
    public Task AnOperand(string statement, string expected) => AssertCompiles(statement, expected);

    [TestMethod]
    // a conversion, and the functions of the library whose parameter MS-VBA types: a value of a declared type.
    [DataRow("v = CInt(Nothing)", InvalidUseOfObject)]
    [DataRow("v = CStr(New Collection)", ArgumentNotOptional)]
    [DataRow("v = CInt(New DfltChain)", TypeMismatch)]
    [DataRow("v = CDbl(c)", ArgumentNotOptional)]
    [DataRow("v = CVErr(New DfltChain)", TypeMismatch)]
    [DataRow("v = Sgn(New DfltChain)", TypeMismatch)]
    [DataRow("v = Left$(Nothing, 1)", InvalidUseOfObject)]
    [DataRow("v = Trim$(New DfltChain)", TypeMismatch)]
    [DataRow("v = Mid$(New DfltArg, 1)", ArgumentNotOptional)]
    [DataRow("v = Sqr(Nothing)", InvalidUseOfObject)]
    [DataRow("v = Val(New DfltChain)", TypeMismatch)]
    [DataRow("v = Chr(New DfltArg)", ArgumentNotOptional)]
    [DataRow("v = CInt(New NoDflt)", Compiles)]
    [DataRow("v = CInt(New DfltVar)", Compiles)]
    // a value of any type: an object whose default member returns an object is read when it runs.
    [DataRow("v = Abs(Nothing)", InvalidUseOfObject)]
    [DataRow("v = Int(New Collection)", ArgumentNotOptional)]
    [DataRow("v = Fix(New DfltArg)", ArgumentNotOptional)]
    [DataRow("v = Len(Nothing)", InvalidUseOfObject)]
    [DataRow("v = Abs(New DfltChain)", Compiles)]
    [DataRow("v = Int(New DfltChain)", Compiles)]
    [DataRow("v = Abs(New NoDflt)", Compiles)]
    // a Variant the function takes as it is.
    [DataRow("v = CVar(Nothing)", Compiles)]
    [DataRow("v = CDec(New Collection)", Compiles)]
    [DataRow("v = Round(Nothing)", Compiles)]
    [DataRow("v = Hex(New Collection)", Compiles)]
    [DataRow("v = Left(Nothing, 1)", Compiles)]
    [DataRow("v = Trim(New DfltChain)", Compiles)]
    [DataRow("v = IsNumeric(Nothing)", Compiles)]
    [DataRow("v = TypeName(New Collection)", Compiles)]
    [DataRow("v = IIf(True, Nothing, 2)", Compiles)]
    public Task AnArgumentOfTheLibrary(string statement, string expected) => AssertCompiles(statement, expected);

    // MS-VBA, Excel-measured (oracle s46 F16a-c): 438 when the object is one, 91 when the variable holds Nothing.
    [TestMethod]
    [DataRow("l = New NoDflt", "438")]
    [DataRow("v = New NoDflt", "438")]
    [DataRow("ByLng New NoDflt", "438")]
    [DataRow("l = 1 + New NoDflt", "438")]
    [DataRow("Set o = New NoDflt ;; c = o", "438")]
    [DataRow("Set o = New NoDflt ;; c = Split(\"a,b,c\", \",\", o)", "438")]
    [DataRow("c = CInt(o)", "91")]
    public async Task AClassWithNoDefaultMember_Compiles_AndRaisesWhenItRuns(string statements, string expected)
    {
        var output = await RunAsync(Classes,
            "Attribute VB_Name = \"Program\"\r\nPrivate Sub ByLng(ByVal x As Long)\r\nEnd Sub\r\nPublic Sub Main()\r\n"
            + "    Dim v As Variant, l As Long, o As NoDflt, c As Variant\r\n    On Error Resume Next\r\n"
            + $"    {string.Join("\r\n    ", statements.Split(" ;; "))}\r\n    Debug.Print Err.Number\r\nEnd Sub\r\n");

        Assert.AreEqual(expected, string.Join("/", output));
    }

    [TestMethod]
    [DataRow("v = Len(New DfltChain)")]
    [DataRow("v = LenB(New DfltChain)")]
    public Task LenOfAnObjectWhoseDefaultMemberReturnsAnObject_IsVariableRequired(string statement)
        => AssertCompiles(statement, "Variable required");

    // MS-VBAL §6.1.2.11.1.22: "any valid string expression, or any valid variable name" - an object stands for its default member's value, which is
    // neither unless it is a String or a Variant.
    [TestMethod]
    [DataRow("v = Len(New DfltNum)", "Variable required")]
    [DataRow("v = Len(New DfltVar)", Compiles)]
    [DataRow("v = Len(New NoDflt)", Compiles)]
    [DataRow("v = Len(New DfltArg)", ArgumentNotOptional)]
    [DataRow("v = Len(c)", ArgumentNotOptional)]
    [DataRow("v = Len(New Collection)", ArgumentNotOptional)]
    [DataRow("v = Len(Nothing)", InvalidUseOfObject)]
    public Task LenOfAnObject(string statement, string expected) => AssertCompiles(statement, expected);
}
