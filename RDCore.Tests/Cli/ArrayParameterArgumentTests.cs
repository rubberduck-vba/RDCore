using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// MS-VBAL §5.3.1.11: an array parameter is given an array of its element type, and MS-VBA refuses anything else at compile time - an array of another
/// element type, a <c>Variant</c>, whatever it holds, a scalar, and an array in parentheses.
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA reported, or printed, for the same code in Excel (64-bit, en-US, oracle\s57 A05, s58 G01-G12, s59 G13-G20).
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
    public async Task AnArgumentThatIsNoArrayOfTheElementType_IsACompileError(string parameter, string body)
        => StringAssert.Contains(string.Join(" / ", await LoadErrorsAsync([], Program(parameter, body, Makers))), "Type mismatch: array or user-defined type expected");

    private static readonly string[] Makers =
    [
        "Private Function Make() As Long()", "    Dim r(2) As Long", "    Make = r", "End Function",
        "Private Function MakeI() As Integer()", "    Dim r(2) As Integer", "    MakeI = r", "End Function",
    ];

    [TestMethod]
    public async Task AVariantParameter_GivenToAnArrayParameter_IsACompileError()
        => StringAssert.Contains(string.Join(" / ", await LoadErrorsAsync([], Program(" As Long", "Dim c(2) As Long ;; Debug.Print Pass(c)",
                "Private Function Pass(ByVal v As Variant) As Long", "    Pass = Count(v)", "End Function"))),
            "Type mismatch: array or user-defined type expected", "G12");

    [TestMethod]
    [DataRow("Dim c(2) As Long ;; Debug.Print Count(c)", DisplayName = "G11 a Long()")]
    [DataRow("Debug.Print Count(Make())", DisplayName = "G13 what a function returns, a Long()")]
    [DataRow("Dim r As TArr ;; Debug.Print Count(r.A)", DisplayName = "G18 an array field of a record")]
    public async Task AnArrayOfTheElementType_IsGivenToTheParameter(string body)
        => CollectionAssert.AreEqual(new[] { "3" }, await RunAsync([], Program(" As Long", body, Makers)));
}
