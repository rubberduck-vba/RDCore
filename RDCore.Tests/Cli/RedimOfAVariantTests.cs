using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>ReDim</c> of a Variant that holds a value, and an <c>Erase</c> of one (<strong>MS-VBAL §5.4.3.3-4</strong>, RD-VBAL 5.4.3.3); a value assigned
/// to a resizable array (§5.4.3.8).
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA printed, or the compile error it reported, for the same code in Excel (64-bit, en-US, oracle\findings-labels
/// <c>cases-redim2.txt</c>, <c>cases-redim.txt</c>).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim")]
[TestCategory("MS-VBAL 5.4.3.4 Erase")]
public sealed class RedimOfAVariantTests
{
    private static string Program(string body) => string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Option Base 0",
        "Public Sub Main()",
        "    On Error Resume Next",
        "    " + string.Join("\r\n    ", body.Split(" ;; ")),
        "End Sub",
        string.Empty);

    [TestMethod]
    [DataRow("Dim y ;; y = 5 ;; ReDim y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y) & \" \" & UBound(y)", "0 Variant() 2", DisplayName = "V01 a number")]
    [DataRow("Dim y ;; y = 5 ;; ReDim Preserve y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Integer", DisplayName = "V02 Preserve of a number")]
    [DataRow("Dim y ;; ReDim Preserve y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Empty", DisplayName = "H01 Preserve of nothing yet")]
    [DataRow("Dim y ;; ReDim Preserve y(2) ;; Debug.Print IsArray(y)", "False", DisplayName = "H02 Preserve of nothing yet makes no array")]
    [DataRow("Dim y As Variant ;; y = Empty ;; ReDim Preserve y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Empty", DisplayName = "H03 Preserve of Empty")]
    [DataRow("Dim y ;; y = Null ;; ReDim Preserve y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Null", DisplayName = "H04 Preserve of Null")]
    [DataRow("Dim y ;; y = \"abc\" ;; ReDim Preserve y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 String", DisplayName = "H05 Preserve of a String")]
    [DataRow("Dim y ;; ReDim y(1) ;; ReDim Preserve y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y) & \" \" & UBound(y)", "0 Variant() 2", DisplayName = "H06 Preserve of an array")]
    [DataRow("Dim y ;; ReDim Preserve y(2) As Long ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Empty", DisplayName = "H07 Preserve As Long of nothing yet")]
    [DataRow("Dim y ;; y = \"abc\" ;; ReDim y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "0 Variant()", DisplayName = "V04 a String")]
    [DataRow("Dim y ;; Set y = New Collection ;; ReDim y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "0 Variant()", DisplayName = "V05 an object")]
    [DataRow("Dim y ;; y = Null ;; ReDim y(2) ;; Debug.Print Err.Number & \" \" & TypeName(y)", "0 Variant()", DisplayName = "V06 Null")]
    [DataRow("Dim y ;; y = 5 ;; Erase y ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Integer", DisplayName = "V07 Erase of a number")]
    [DataRow("Dim y ;; Erase y ;; Debug.Print Err.Number & \" \" & TypeName(y)", "13 Empty", DisplayName = "V08 Erase of nothing yet")]
    [DataRow("Dim y As Variant ;; y = 5 ;; ReDim y(1 To 3) As Long ;; Debug.Print Err.Number & \" \" & TypeName(y)", "0 Long()", DisplayName = "V09 As Long")]
    public async Task AVariantThatHoldsAValue(string body, string expected)
        => Assert.AreEqual(expected, (await RunAsync([], Program(body))).Last());

    [TestMethod]
    [DataRow("Dim a() As Long ;; a = 5", DisplayName = "A03 a Long()")]
    [DataRow("Dim a() As Variant ;; a = 5", DisplayName = "A04 a Variant()")]
    [DataRow("ReDim y(2) As Long ;; y = 5", DisplayName = "A06 one a ReDim declares As Long")]
    [DataRow("ReDim y(2) ;; y = 5", DisplayName = "R02 one a ReDim declares")]
    [DataRow("Dim a() As Long ;; a = \"abc\"", DisplayName = "G01 a String to a Long()")]
    [DataRow("Dim a() As Variant ;; a = \"abc\"", DisplayName = "G02 a String to a Variant()")]
    [DataRow("Dim a() As String ;; a = \"abc\"", DisplayName = "G03 a String to a String()")]
    [DataRow("Dim a() As Long ;; Dim s As String ;; s = \"1\" ;; a = s", DisplayName = "G04 a String variable")]
    [DataRow("Dim a() As Long ;; a = #1/1/2000#", DisplayName = "G05 a Date")]
    [DataRow("Dim a() As Variant ;; Dim d As Date ;; a = d", DisplayName = "G06 a Date variable to a Variant()")]
    [DataRow("Dim a() As Long ;; a = True", DisplayName = "G07 a Boolean")]
    [DataRow("Dim a() As Boolean ;; Dim b As Boolean ;; a = b", DisplayName = "G08 a Boolean to a Boolean()")]
    [DataRow("Dim a() As Long ;; Dim c As Currency ;; a = c", DisplayName = "G10 a Currency")]
    public async Task AValueAssignedToAResizableArray_IsACompileError(string body)
    {
        var errors = await LoadErrorsAsync([], Program(body));

        StringAssert.StartsWith(Assert.ContainsSingle(errors), "Can't assign to array");
    }

    // A02's `y = Empty` compiles and raises 13 (findings desk #17), which keeps the Variant() it printed.
    [TestMethod]
    [DataRow("ReDim y(2) ;; y = Array(1, 2) ;; Debug.Print TypeName(y)", "Variant()", DisplayName = "A01 an array")]
    [DataRow("Dim a() As Variant ;; a = Array(1, 2) ;; Debug.Print UBound(a)", "1", DisplayName = "A05 an array to a Variant()")]
    [DataRow("Dim a() As Byte ;; a = \"AB\" ;; Debug.Print Err.Number & \" \" & UBound(a)", "0 3", DisplayName = "G09 a String to a Byte()")]
    [DataRow("Dim a() As Long ;; Dim v As Variant ;; v = 5 ;; a = v ;; Debug.Print Err.Number", "13", DisplayName = "G12 a Variant that holds a number")]
    public async Task AnArrayOrAVariantAssignedToAResizableArray_IsAssigned(string body, string expected)
        => Assert.AreEqual(expected, (await RunAsync([], Program(body))).Last());
}
