using RDCore.Diagnostics;
using static RDCore.Tests.Diagnostics.CodeActionFixture;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The fixes of <c>RDC00106</c> and <c>RDC00101</c>: the type a declaration has, written; <c>Option Explicit</c>, with a declaration for each variable it
/// would make an error of. The code means what it meant.
/// </summary>
[TestClass]
public sealed class ImplicitDeclarationFixTests
{
    [TestMethod]
    [DataRow("    Dim x", "    Dim x As Variant", DisplayName = "a local")]
    [DataRow("    Dim x()", "    Dim x() As Variant", DisplayName = "a dynamic array")]
    [DataRow("    Dim x(1 To 3)", "    Dim x(1 To 3) As Variant", DisplayName = "an array with bounds")]
    [DataRow("    Dim x ' a comment", "    Dim x As Variant ' a comment", DisplayName = "a comment after it")]
    [DataRow("    Static x", "    Static x As Variant", DisplayName = "a Static local")]
    public async Task AVariableOfNoType_IsDeclaredAsVariant(string declaration, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitVariantDeclaration,
            ["Private Sub S()", declaration, "End Sub"], ["Private Sub S()", written, "End Sub"]);

    [TestMethod]
    public async Task TheSecondVariableOfADeclaration_IsDeclaredAsVariant()
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitVariantDeclaration,
            ["Private Sub S()", "    Dim a As Long, x", "End Sub"], ["Private Sub S()", "    Dim a As Long, x As Variant", "End Sub"]);

    [TestMethod]
    public async Task AModuleVariableOfNoType_IsDeclaredAsVariant()
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitVariantDeclaration, ["Private x", "Private y As Long"], ["Private x As Variant", "Private y As Long"]);

    [TestMethod]
    [DataRow("Private Sub S(ByVal x)", "Private Sub S(ByVal x As Variant)", DisplayName = "a parameter")]
    [DataRow("Private Sub S(ByRef x())", "Private Sub S(ByRef x() As Variant)", DisplayName = "an array parameter")]
    [DataRow("Private Sub S(Optional ByVal x = 5)", "Private Sub S(Optional ByVal x As Variant = 5)", DisplayName = "an optional parameter with a default value")]
    [DataRow("Private Sub S(Optional ByVal x)", "Private Sub S(Optional ByVal x As Variant)", DisplayName = "an optional parameter")]
    [DataRow("Private Sub S(ParamArray x())", "Private Sub S(ParamArray x() As Variant)", DisplayName = "a ParamArray")]
    public async Task AParameterOfNoType_IsDeclaredAsVariant(string header, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitVariantDeclaration, [header, "End Sub"], [written, "End Sub"]);

    [TestMethod]
    public async Task AVariableDeclaredAsVariant_HoldsWhatItHeld()
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitVariantDeclaration,
            ["Public Sub Main()", "    Dim x", "    Debug.Print TypeName(x)", "    x = \"a\"", "    Debug.Print TypeName(x)", "End Sub"],
            ["Public Sub Main()", "    Dim x As Variant", "    Debug.Print TypeName(x)", "    x = \"a\"", "    Debug.Print TypeName(x)", "End Sub"],
            runs: true);

    [TestMethod]
    [DataRow("Private Function F()", "Private Function F() As Variant", DisplayName = "a function")]
    [DataRow("Private Function F(ByVal x As Long, _\r\n        ByVal y As Long)", "Private Function F(ByVal x As Long, _\r\n        ByVal y As Long) As Variant", DisplayName = "a parameter list on two lines")]
    [DataRow("Private Function F() ' a comment", "Private Function F() As Variant ' a comment", DisplayName = "a comment after it")]
    public async Task AFunctionOfNoReturnType_IsDeclaredToReturnAVariant(string header, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitVariantReturnType, [header, "End Function"], [written, "End Function"]);

    [TestMethod]
    public async Task APropertyGetOfNoReturnType_IsDeclaredToReturnAVariant()
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitVariantReturnType, ["Public Property Get P()", "End Property"], ["Public Property Get P() As Variant", "End Property"]);

    [TestMethod]
    public async Task AFunctionDeclaredToReturnAVariant_ReturnsWhatItReturned()
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitVariantReturnType,
            ["Public Sub Main()", "    Debug.Print TypeName(F())", "End Sub", "Private Function F()", "    F = \"a\"", "End Function"],
            ["Public Sub Main()", "    Debug.Print TypeName(F())", "End Sub", "Private Function F() As Variant", "    F = \"a\"", "End Function"],
            runs: true);

    [TestMethod]
    public async Task ADeclareOfNoReturnType_HasNoFix()
        => await AssertNoFixAsync(RDCoreDiagnosticId.ImplicitVariantReturnType, "Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" ()");

    [TestMethod]
    public async Task OptionExplicit_IsWritten_WithADimForEachImplicitVariable()
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitDeclarationsEnabled,
            ["Private Const K As Long = 2", "Public Sub Main()", "    total = 1", "    total = total + K", "    name$ = \"a\"", "    Debug.Print total; name$", "End Sub",
                "Private Sub Other()", "    Dim i As Long", "    For i = 1 To 2", "        count = count + i", "    Next", "End Sub"],
            ["Option Explicit", "Private Const K As Long = 2", "Public Sub Main()", "    Dim total As Variant", "    Dim name As String", "    total = 1", "    total = total + K",
                "    name = \"a\"", "    Debug.Print total; name", "End Sub", "Private Sub Other()", "    Dim count As Variant", "    Dim i As Long", "    For i = 1 To 2",
                "        count = count + i", "    Next", "End Sub"],
            runs: true);

    [TestMethod]
    public async Task OptionExplicit_UnderADefTypeDirective_DeclaresTheVariableOfTheDirectivesType()
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitDeclarationsEnabled,
            ["DefLng N", "Public Sub Main()", "    number = 7 / 2", "    Debug.Print number", "End Sub"],
            ["Option Explicit", "DefLng N", "Public Sub Main()", "    Dim number", "    number = 7 / 2", "    Debug.Print number", "End Sub"],
            runs: true);

    [TestMethod]
    public async Task OptionExplicit_InAModuleWithNoImplicitVariable_IsWrittenAlone()
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitDeclarationsEnabled,
            ["Public Sub Main()", "    Dim n As Long", "    Debug.Print n", "End Sub"],
            ["Option Explicit", "Public Sub Main()", "    Dim n As Long", "    Debug.Print n", "End Sub"],
            runs: true);

    [TestMethod]
    public async Task OptionExplicit_IsNotOffered_WhereTheBodyStartsOnTheLineOfTheDeclaration()
        => await AssertNoFixAsync(RDCoreDiagnosticId.ImplicitDeclarationsEnabled, "Public Sub Main(): total = 1", "End Sub");

    [TestMethod]
    public async Task OptionExplicit_IsNotOffered_WhereADirectiveIsInTheProcedure()
        => await AssertNoFixAsync(RDCoreDiagnosticId.ImplicitDeclarationsEnabled, "Public Sub Main()", "#If VBA7 Then", "    total = 1", "#End If", "End Sub");
}
