using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.3.1.5</strong> A param array "behaves as if it was an array whose elements were 'byref' &lt;positional-param&gt; elements whose declared
/// types were Variant": an element given a variable is that variable, which what the procedure assigns to the element is assigned to, as the variable is
/// declared.
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA printed for the same code, in Excel (64-bit, en-US, oracle\s44 P01-P12).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.5 Parameter Lists")]
public sealed class ParamArrayByRefTests
{
    private const string Bump = "Private Sub Bump(ParamArray a() As Variant) ;;     a(0) = a(0) + 1 ;; End Sub";

    // the procedures of the case, then the statements of Main, each printing with P.
    private static async Task<string> RunAsync(string procedures, string body)
    {
        var program = string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Private acc As String",
            string.Join("\r\n", procedures.Split(" ;; ")),
            "Private Sub P(ByVal x As Variant)", "    acc = acc & CStr(x) & \"|\"", "End Sub",
            "Public Sub Main()",
            "    On Error Resume Next",
            "    " + string.Join("\r\n    ", body.Split(" ;; ")),
            "    Debug.Print acc",
            "End Sub",
            string.Empty);

        return (await ModuleWorkspace.RunAsync([], program)).Last();
    }

    [TestMethod]
    [DataRow(Bump, "Dim n As Variant ;; n = 1 ;; Bump n ;; P n", "2|", DisplayName = "P01 a Variant variable")]
    [DataRow(Bump, "Dim n As Long ;; n = 1 ;; Bump n ;; P n ;; P TypeName(n)", "2|Long|", DisplayName = "P02 a Long variable stays a Long")]
    [DataRow("Private Sub Bump(ParamArray a() As Variant) ;;     a(1) = a(1) + 1 ;; End Sub", "Dim x(2) As Long ;; x(1) = 5 ;; Bump 0, x(1) ;; P x(1)", "6|",
        DisplayName = "P05 an element of an array")]
    [DataRow(Bump + " ;; Private Sub Outer(ParamArray b() As Variant) ;;     Bump b(0) ;; End Sub", "Dim n As Variant ;; n = 1 ;; Outer n ;; P n", "2|",
        DisplayName = "P12 an element of a param array, passed on")]
    public async Task AVariableGivenToAParamArray_IsTheVariable(string procedures, string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(procedures, body));

    [TestMethod]
    [DataRow(Bump, "Dim n As Variant ;; n = 1 ;; Bump (n) ;; P n", "1|", DisplayName = "P03 in parentheses")]
    [DataRow(Bump, "Dim n As Variant ;; n = 1 ;; Bump n + 0 ;; P n", "1|", DisplayName = "P04 an expression")]
    public async Task AValueGivenToAParamArray_IsACopy(string procedures, string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(procedures, body));

    private const string Stash = "Private Sub Stash(ParamArray a() As Variant) ;;     a(0) = {0} ;; End Sub";

    [TestMethod]
    [DataRow("\"abc\"", "Dim n As Long ;; n = 1 ;; Stash n ;; P Err.Number ;; P n", "13|1|", DisplayName = "P06 what a Long cannot hold is a type mismatch")]
    [DataRow("\"7\"", "Dim n As Long ;; n = 1 ;; Stash n ;; P Err.Number ;; P n ;; P TypeName(n)", "0|7|Long|", DisplayName = "P07 let-coerced to a Long")]
    [DataRow("3.5", "Dim s As String ;; s = \"x\" ;; Stash s ;; P s ;; P TypeName(s)", "3.5|String|", DisplayName = "P08 let-coerced to a String")]
    public async Task WhatIsAssignedToTheElement_IsLetCoercedToTheVariablesDeclaredType(string value, string body, string expected)
        => Assert.AreEqual(expected, await RunAsync(string.Format(Stash, value), body));

    [TestMethod]
    public async Task AnObjectSetToTheElement_IsSetToTheVariable()
        => Assert.AreEqual("Collection|", await RunAsync(
            "Private Sub Stash(ParamArray a() As Variant) ;;     Set a(0) = New Collection ;; End Sub", "Dim v As Variant ;; v = 1 ;; Stash v ;; P TypeName(v)"));

    // P10: what the element reads as is a Variant holding the variable's value, of the type the variable is declared.
    [TestMethod]
    public async Task TheElement_ReadsAsAVariantOfTheVariablesType()
        => Assert.AreEqual("Long:3|Integer:2|", await RunAsync(
            "Private Function Peek(ParamArray a() As Variant) As String ;;     Peek = TypeName(a(0)) & \":\" & VarType(a(0)) ;; End Function",
            "Dim n As Long ;; P Peek(n) ;; Dim i As Integer ;; P Peek(i)"));

    // P11 (with names that do not hide P): every element of the array is its own argument's.
    [TestMethod]
    public async Task EveryElement_IsItsOwnArgument()
        => Assert.AreEqual("4|6|", await RunAsync(
            "Private Sub Twice(ParamArray a() As Variant) ;;     Dim k As Long ;;     For k = 0 To UBound(a) ;;         a(k) = a(k) * 2 ;;     Next ;; End Sub",
            "Dim u As Variant, w As Variant ;; u = 2 ;; w = 3 ;; Twice u, w, 4 ;; P u ;; P w"));
}
