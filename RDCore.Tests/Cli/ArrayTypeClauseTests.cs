using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.3.1.4</strong>: a type written with <c>()</c> - <c>As Long()</c> - is what a <c>Function</c>, a <c>Property Get</c> or a <c>Declare</c>'s
/// <c>Function</c> returns, and nothing else is declared with one: <strong>MS-VBA</strong> reads it anywhere else as a syntax error (recorded in Excel,
/// oracle\s23 T01-T18). An array variable or parameter is declared with <c>()</c> after its name.
/// </summary>
[TestClass]
public sealed class ArrayTypeClauseTests
{
    private const string Program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nEnd Sub\r\n";

    private static Task<string[]> ErrorsOfModuleAsync(string declarations)
        => LoadErrorsAsync([], [("Decls", StandardModule("Decls", ["Option Explicit", .. declarations.Split('|')]))], Program);

    private static Task<string[]> ErrorsOfClassAsync(string declarations)
        => LoadErrorsAsync([("Holder", ClassModule("Holder", ["Option Explicit", .. declarations.Split('|')]))], [], Program);

    [TestMethod]
    [DataRow("Public b As Long()", DisplayName = "T01 a Public variable")]
    [DataRow("Private b As Long()", DisplayName = "T02 a Private variable")]
    [DataRow("Public Sub X()|    Static s As Long()|End Sub", DisplayName = "T03 a Static local")]
    [DataRow("Public Type T|    F As Long()|End Type", DisplayName = "T04 a field of a Type")]
    [DataRow("Public Declare PtrSafe Sub X Lib \"kernel32\" Alias \"Sleep\" (a As Long())", DisplayName = "T09 a Declare's parameter")]
    [DataRow("Public Sub X()|    Dim a() As Long|    ReDim a(2) As Long()|End Sub", DisplayName = "T10 a ReDim")]
    [DataRow("Public Sub X()|    Dim a() As Long, b As Long()|End Sub", DisplayName = "T13 the second of a Dim's variables")]
    [DataRow("Public Sub X()|    Dim c As Long()|End Sub", DisplayName = "T15 a Dim")]
    [DataRow("Public Sub X()|    Dim w As Collection()|End Sub", DisplayName = "a Dim of a class")]
    [DataRow("Public Sub X()|    Dim b As New Collection()|End Sub", DisplayName = "T14 As New")]
    [DataRow("Public Const K As Long() = 1", DisplayName = "T16 a constant")]
    [DataRow("Public Sub X(a As Long())|End Sub", DisplayName = "B02 a parameter")]
    [DataRow("Public Sub X(ByVal a As Long())|End Sub", DisplayName = "A02 a ByVal parameter")]
    [DataRow("Public Sub X(Optional a As Long() = 1)|End Sub", DisplayName = "T17 an optional parameter")]
    public async Task ATypeWithParenthesesOfAStandardModulesDeclaration_IsASyntaxError(string declarations)
        => StringAssert.Contains(string.Join(" / ", await ErrorsOfModuleAsync(declarations)), "Syntax error");

    [TestMethod]
    [DataRow("Public Event E(a As Long())", DisplayName = "T08 an event's parameter")]
    [DataRow("Public Items As Long()", DisplayName = "a field of a class")]
    public async Task ATypeWithParenthesesOfAClassModulesDeclaration_IsASyntaxError(string declarations)
        => StringAssert.Contains(string.Join(" / ", await ErrorsOfClassAsync(declarations)), "Syntax error");

    [TestMethod]
    [DataRow("Public Function X() As Long()|End Function", DisplayName = "T05 a Function")]
    [DataRow("Public Declare PtrSafe Function X Lib \"kernel32\" Alias \"GetTickCount\" () As Long()", DisplayName = "T07 a Declare's Function")]
    [DataRow("Public Function X() As Variant()|End Function", DisplayName = "T11 of Variant")]
    [DataRow("Public Function X(ByVal n As Long) As String()|End Function", DisplayName = "T12 with a parameter")]
    [DataRow("Public Function X() As Long ( )|End Function", DisplayName = "T18 with spaces")]
    [DataRow("Public Function X() As Collection()|End Function", DisplayName = "of a class")]
    [DataRow("Public Sub X(a() As Long)|    Dim b() As Long|End Sub", DisplayName = "arrays declared by the () after the name")]
    public async Task ATypeWithParenthesesOfWhatAFunctionReturns_IsValid(string declarations)
        => Assert.IsEmpty(await ErrorsOfModuleAsync(declarations));

    // T06
    [TestMethod]
    public async Task ATypeWithParenthesesOfWhatAPropertyGetReturns_IsValid()
        => Assert.IsEmpty(await ErrorsOfClassAsync("Public Property Get P() As Long()|End Property"));
}
