using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.3.1.11: an array parameter is given an array of its element type, and MS-VBA refuses anything else at compile time - an array of another
/// element type, a <c>Variant</c>, whatever it holds, a scalar, and an array in parentheses.
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA reported, or printed, for the same code in Excel (64-bit, en-US).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Procedure invocation argument processing")]
public sealed class ArrayParameterArgumentTests
{
    private static string Program(string parameter, string body, params string[] procedures) => StandardModule("Program",
        [
            "Option Explicit",
            "Private Type TPair", "    X As Long", "    Y As Long", "End Type",
            "Private Type TArr", "    A(2) As Long", "End Type",
            "Private Enum MyEnum", "    mA", "End Enum",
            "Private Enum EnumB", "    bA", "End Enum",
            $"Private Function Count(ByRef a(){parameter}) As Long",
            "    Count = UBound(a) - LBound(a) + 1",
            "End Function",
            .. procedures,
            "Public Sub Main()",
            "    " + string.Join("\r\n    ", body.Split(" ;; ")),
            "End Sub",
        ]);

    [TestMethod]
    [DataRow(" As Collection", "Dim c(2) As Collection ;; Dim v As Variant ;; v = c ;; Debug.Print Count(v)", DisplayName = "A05 a Variant to a Collection()")]
    [DataRow(" As Long", "Dim c(2) As Long ;; Dim v As Variant ;; v = c ;; Debug.Print Count(v)", DisplayName = "G01 a Variant to a Long()")]
    [DataRow(" As Variant", "Dim c(2) As Variant ;; Dim v As Variant ;; v = c ;; Debug.Print Count(v)", DisplayName = "G02 a Variant to a Variant()")]
    [DataRow(" As TPair", "Dim c(2) As TPair ;; Dim v As Variant ;; Debug.Print Count(v)", DisplayName = "G03 a Variant to a TPair()")]
    [DataRow("", "Dim c(2) ;; Dim v As Variant ;; v = c ;; Debug.Print Count(v)", DisplayName = "G04 a Variant to an array of no declared type")]
    [DataRow(" As Long", "Dim w() As Variant ;; ReDim w(2) ;; Debug.Print Count(w)", DisplayName = "G05 a Variant() to a Long()")]
    [DataRow(" As Long", "Dim c(2) As Integer ;; Debug.Print Count(c)", DisplayName = "G06 an Integer() to a Long()")]
    [DataRow(" As Object", "Dim c(2) As Collection ;; Debug.Print Count(c)", DisplayName = "G07 a Collection() to an Object()")]
    [DataRow(" As Long", "Dim c(2) As Long ;; Dim v As Variant ;; v = c ;; Debug.Print Count((v))", DisplayName = "G08 a Variant in parentheses")]
    [DataRow(" As Variant", "Debug.Print Count(Array(1, 2))", DisplayName = "G09 what Array returns")]
    [DataRow(" As Variant", "Dim vs(1) As Variant ;; vs(0) = Array(1, 2) ;; Debug.Print Count(vs(0))", DisplayName = "G10 an element of a Variant array")]
    [DataRow(" As Variant", "Debug.Print Count(Split(\"a b c\"))", DisplayName = "G14 what Split returns, to a Variant()")]
    [DataRow(" As Long", "Dim n As Long ;; Debug.Print Count(n)", DisplayName = "G15 a Long")]
    [DataRow(" As Long", "Debug.Print Count(MakeI())", DisplayName = "G16 what a function returns, an Integer(), to a Long()")]
    [DataRow(" As Long", "Dim c(2) As Long ;; Debug.Print Count((c))", DisplayName = "G17 a Long() in parentheses")]
    [DataRow(" As String", "Debug.Print Count(Split(\"a b c\"))", DisplayName = "G19 what Split returns, to a String()")]
    [DataRow(" As Long", "Dim v As Variant ;; Debug.Print Count(a:=v)", DisplayName = "G20 a Variant, a named argument")]
    [DataRow(" As Long", "Dim c(2) As MyEnum ;; Debug.Print Count(c)", DisplayName = "s61 G21 an Enum() to a Long()")]
    [DataRow(" As MyEnum", "Dim c(2) As Long ;; Debug.Print Count(c)", DisplayName = "s61b G22 a Long() to an Enum()")]
    [DataRow(" As Long", "Dim o As Object ;; Set o = New Box ;; Debug.Print Count(o.Arr)", DisplayName = "s61 G24 a member of an Object")]
    [DataRow(" As Long", "Dim w As Variant ;; Set w = New Box ;; Debug.Print Count(w.Arr)", DisplayName = "s61 G25 a member of a Variant")]
    [DataRow(" As Long", "Dim v As Variant ;; v = Array(-100#, 60#, 60#) ;; Debug.Print IRR(v)", DisplayName = "s61 G27 a Variant to IRR")]
    [DataRow(" As Long", "Dim d(2) As Double ;; Debug.Print IRR((d))", DisplayName = "s61 G28 an array in parentheses to IRR")]
    [DataRow(" As Long", "Dim c(2) As Long ;; Takes (c)", DisplayName = "s61 G30 an array in parentheses, in a call statement")]
    [DataRow(" As String", "Dim s(2) As String * 3 ;; Debug.Print Count(s)", DisplayName = "s61 G31 a String * 3 array to a String()")]
    public async Task AnArgumentThatIsNoArrayOfTheElementType_IsACompileError(string parameter, string body)
        => StringAssert.Contains(string.Join(" / ", await LoadErrorsAsync([("Box", Box)], Program(parameter, body, Makers))),
            "Type mismatch: array or user-defined type expected");

    private static readonly string Box = ClassModule("Box",
        "Public Property Get Arr() As Long()", "    Dim r(2) As Long", "    Arr = r", "End Property");

    private static readonly string[] Makers =
    [
        "Private Function Make() As Long()", "    Dim r(2) As Long", "    Make = r", "End Function",
        "Private Function MakeI() As Integer()", "    Dim r(2) As Integer", "    MakeI = r", "End Function",
        "Private Sub Takes(ByRef a() As Long)", "    a(0) = 7", "End Sub",
        "Private Sub TakesB(ByRef a() As EnumB)", "    a(0) = 7", "End Sub",
    ];

    [TestMethod]
    public async Task AVariantParameter_GivenToAnArrayParameter_IsACompileError()
        => StringAssert.Contains(string.Join(" / ", await LoadErrorsAsync([], Program(" As Long", "Dim c(2) As Long ;; Debug.Print Pass(c)",
                "Private Function Pass(ByVal v As Variant) As Long", "    Pass = Count(v)", "End Function"))),
            "Type mismatch: array or user-defined type expected", "G12");

    [TestMethod]
    [DataRow(" As Long", "Dim c(2) As Long ;; Debug.Print Count(c)", "3", DisplayName = "G11 a Long()")]
    [DataRow(" As Long", "Debug.Print Count(Make())", "3", DisplayName = "G13 what a function returns, a Long()")]
    [DataRow(" As Long", "Dim r As TArr ;; Debug.Print Count(r.A)", "3", DisplayName = "G18 an array field of a record")]
    [DataRow(" As Long", "Dim b As Box ;; Set b = New Box ;; Debug.Print Count(b.Arr)", "3", DisplayName = "s61 G26 what a Property Get returns, a Long()")]
    [DataRow(" As Long", "Dim d(2) As Double ;; d(0) = -100 ;; d(1) = 60 ;; d(2) = 60 ;; Debug.Print Round(IRR(d), 6)", "0.130662", DisplayName = "s61 G29 a Double() to IRR")]
    [DataRow(" As Box", "Dim bs(2) As Box ;; Debug.Print Count(bs)", "3", DisplayName = "s61 G32 a Box() to a Box()")]
    [DataRow(" As Variant", "Dim c(2) As Variant ;; Debug.Print Count(c)", "3", DisplayName = "s61 G33 a fixed-size Variant() to a Variant()")]
    [DataRow(" As MyEnum", "Dim c(2) As MyEnum ;; Debug.Print Count(c)", "3", DisplayName = "s61b G34 an Enum() to its Enum()")]
    [DataRow(" As EnumB", "Dim c(2) As MyEnum ;; Debug.Print Count(c)", "3", DisplayName = "s61b G23 an Enum() to another Enum()")]
    [DataRow(" As Long", "Dim c(2) As MyEnum ;; TakesB c ;; Debug.Print c(0)", "7", DisplayName = "s61b G23 an Enum() to another Enum(), by reference")]
    public async Task AnArrayOfTheElementType_IsGivenToTheParameter(string parameter, string body, string printed)
        => CollectionAssert.AreEqual(new[] { printed }, (await RunAsync([("Box", Box)], Program(parameter, body, Makers))).Select(line => line.Trim()).ToArray());
}
