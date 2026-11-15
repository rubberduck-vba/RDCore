using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// Constants read in expressions evaluated on payloads, a <c>Select Case</c> whose selector is one, and a <c>For Each</c> over an array of a scalar type
/// have the same values, and the same errors, as the boxed evaluation - which the suite is also run with (<c>RDCORE_UNBOXED=0</c>), for these
/// to hold both ways.
/// </summary>
[TestClass]
public sealed class StatementPayloadTests
{
    private static async Task<string> Run(string declarations, string body)
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n" + string.Join("\r\n", declarations.Split(" ;; "))
            + "\r\nPublic Sub Main()\r\nOn Error Resume Next\r\n" + string.Join("\r\n", body.Split(" ;; ")) + "\r\nEnd Sub\r\n";
        return string.Join("/", await RunAsync([], program));
    }

    [TestMethod]
    [DataRow("Private Const N As Long = 10", "Dim i As Long, s As Long ;; Do While i < N ;; i = i + 1 ;; s = s + i ;; Loop ;; Debug.Print s", "55", DisplayName = "a module Const, in a Do While")]
    [DataRow("", "Const N As Long = 4 ;; Dim i As Long ;; While i < N ;; i = i + 1 ;; Wend ;; Debug.Print i", "4", DisplayName = "a local Const, in a While")]
    [DataRow("Private Const C As Integer = 32767", "Dim i As Integer ;; i = 1 ;; i = C + i ;; Debug.Print Err.Number & \"|\" & i", "6|1", DisplayName = "an Integer Const past its range")]
    [DataRow("Private Const Half = 0.5", "Dim d As Double ;; d = 2 ;; d = d + Half ;; Debug.Print d", "2.5", DisplayName = "a Const of the literal's type")]
    [DataRow("Private Enum Color ;; Red = 1 ;; Green ;; End Enum", "Dim l As Long ;; l = Green * 10 + Color.Red ;; Debug.Print l", "21", DisplayName = "enumeration members, bare and qualified")]
    [DataRow("Private Const Greeting = \"Hi\"", "Dim s As String ;; s = Greeting & \"!\" ;; Debug.Print s", "Hi!", DisplayName = "a String Const")]
    public async Task AConstant_IsReadAsItsFoldedValue(string declarations, string body, string printed)
        => Assert.AreEqual(printed, await Run(declarations, body));

    [TestMethod]
    [DataRow("Dim i As Long, s As String ;; For i = 0 To 5 ;; Select Case i ;; Case 0 ;; s = s & \"z\" ;; Case 1, 2 ;; s = s & \"a\" ;; Case 3 To 4 ;; s = s & \"r\" ;; Case Is > 4 ;; s = s & \"g\" ;; End Select ;; Next ;; Debug.Print s",
        "zaarrg", DisplayName = "values, a list, a range, a comparison")]
    [DataRow("Dim d As Double ;; d = 2.5 ;; Select Case d ;; Case 2 ;; Debug.Print \"2\" ;; Case 2 To 3 ;; Debug.Print \"2-3\" ;; Case Else ;; Debug.Print \"else\" ;; End Select", "2-3",
        DisplayName = "a Double selector")]
    [DataRow("Dim i As Long ;; i = 2 ;; Select Case i ;; Case \"2\" ;; Debug.Print \"text\" ;; Case Else ;; Debug.Print \"else\" ;; End Select", "text",
        DisplayName = "a Case of a String, compared boxed")]
    [DataRow("Dim i As Long ;; i = 1 ;; Select Case i ;; Case 1 ;; i = 2 ;; Debug.Print \"one\" ;; Case 2 ;; Debug.Print \"two\" ;; End Select", "one",
        DisplayName = "the selector is evaluated once")]
    [DataRow("Dim i As Long ;; i = 7 ;; Select Case i * 2 ;; Case 14 ;; Debug.Print \"fourteen\" ;; End Select", "fourteen", DisplayName = "an expression")]
    [DataRow("Dim i As Long ;; i = 7 ;; Select Case i ;; Case 1 To 5, 7 ;; Debug.Print \"in\" ;; End Select", "in", DisplayName = "a range, then a value")]
    public async Task ACase_ComparedOnPayloads_IsWhatTheBoxedEvaluationCompares(string body, string printed)
        => Assert.AreEqual(printed, await Run("", body));

    [TestMethod]
    [DataRow("Dim a(1 To 4) As Long, v As Variant, s As Long, i As Long, t As String ;; For i = 1 To 4 ;; a(i) = i * i ;; Next ;; For Each v In a ;; s = s + v ;; t = TypeName(v) ;; Next ;; Debug.Print s & \"|\" & t & \"|\" & TypeName(v)",
        "30|Long|Empty", DisplayName = "a Long array")]
    [DataRow("Dim a(1 To 3) As Integer, v As Variant, t As String ;; For Each v In a ;; t = t & TypeName(v) ;; Next ;; Debug.Print t", "IntegerIntegerInteger",
        DisplayName = "an Integer array: the Variant holds Integers")]
    [DataRow("Dim a(1 To 3) As Double, v As Variant, s As Double ;; a(1) = 0.5 ;; a(2) = 1.25 ;; a(3) = 2 ;; For Each v In a ;; s = s + v ;; Next ;; Debug.Print s", "3.75",
        DisplayName = "a Double array")]
    [DataRow("Dim a(1 To 2) As Boolean, v As Variant, t As String ;; a(2) = True ;; For Each v In a ;; t = t & CStr(v) & \",\" ;; Next ;; Debug.Print t", "False,True,",
        DisplayName = "a Boolean array")]
    [DataRow("Dim a(1 To 3) As Long, v As Variant, c As New Collection, s As Long ;; a(1) = 1 ;; a(2) = 2 ;; a(3) = 3 ;; For Each v In a ;; s = s + v ;; Set v = c ;; Next ;; Debug.Print s & \"|\" & TypeName(v)",
        "6|Empty", DisplayName = "the control variable holding an object between elements")]
    [DataRow("Dim a(1 To 3) As Variant, v As Variant, t As String ;; a(1) = 1 ;; a(2) = \"b\" ;; a(3) = 2.5 ;; For Each v In a ;; t = t & TypeName(v) & \",\" ;; Next ;; Debug.Print t",
        "Integer,String,Double,", DisplayName = "a Variant array, assigned boxed")]
    [DataRow("Dim a(1 To 2, 1 To 2) As Long, v As Variant, t As String ;; a(1, 1) = 1 ;; a(2, 1) = 2 ;; a(1, 2) = 3 ;; a(2, 2) = 4 ;; For Each v In a ;; t = t & v ;; Next ;; Debug.Print t",
        "1234", DisplayName = "two dimensions, in column-major order")]
    public async Task AForEachOverAnArray_AssignedOnPayloads_IsWhatTheBoxedEvaluationAssigns(string body, string printed)
        => Assert.AreEqual(printed, await Run("", body));
}
