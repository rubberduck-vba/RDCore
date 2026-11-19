using RDCore.Diagnostics;
using static RDCore.Tests.Diagnostics.CodeActionFixture;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The fixes of <c>RDC00202</c>, <c>RDC00203</c>, <c>RDC00204</c> and <c>RDC00205</c>: a declaration written the way it is read. The code means what it
/// meant.
/// </summary>
[TestClass]
public sealed class DeclarationDesignFixTests
{
    [TestMethod]
    [DataRow("Dim x As Long", "Private x As Long", DisplayName = "a variable")]
    [DataRow("Dim x As Long, y As String", "Private x As Long\r\nPrivate y As String", DisplayName = "two variables, each on a line of its own")]
    [DataRow("Dim x As Long, _\r\n    y As String", "Private x As Long\r\nPrivate y As String", DisplayName = "two variables on two lines")]
    [DataRow("Dim x(1 To 3) As Long", "Private x(1 To 3) As Long", DisplayName = "an array")]
    public async Task AModuleLevelDim_IsWrittenPrivate(string declaration, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ModuleScopeDimDeclaration, [declaration], [written]);

    [TestMethod]
    public async Task AModuleLevelDimOfSeveralNames_WrittenPrivate_KeepsTheTypeOfEach()
        => await AssertFixedAsync(RDCoreDiagnosticId.ModuleScopeDimDeclaration,
            ["Dim a, b As Long", "Public Sub Main()", "    Debug.Print TypeName(a); TypeName(b)", "End Sub"],
            ["Private a", "Private b As Long", "Public Sub Main()", "    Debug.Print TypeName(a); TypeName(b)", "End Sub"],
            runs: true);

    [TestMethod]
    public async Task AModuleLevelDim_WrittenPrivate_HoldsWhatItHeld()
        => await AssertFixedAsync(RDCoreDiagnosticId.ModuleScopeDimDeclaration,
            ["Dim count As Long", "Public Sub Main()", "    count = 2", "    Bump", "    Debug.Print count", "End Sub", "Private Sub Bump()", "    count = count + 1", "End Sub"],
            ["Private count As Long", "Public Sub Main()", "    count = 2", "    Bump", "    Debug.Print count", "End Sub", "Private Sub Bump()", "    count = count + 1", "End Sub"],
            runs: true);

    [TestMethod]
    [DataRow("    Dim a As Long, b As String", "    Dim a As Long\r\n    Dim b As String", DisplayName = "two locals")]
    [DataRow("    Dim a As Long, b As String, c As Double", "    Dim a As Long\r\n    Dim b As String\r\n    Dim c As Double", DisplayName = "three locals")]
    [DataRow("    Static a As Long, b As Long", "    Static a As Long\r\n    Static b As Long", DisplayName = "Static locals")]
    [DataRow("    Const A As Long = 1, B As Long = 2", "    Const A As Long = 1\r\n    Const B As Long = 2", DisplayName = "local constants")]
    [DataRow("    Dim a As Long, _\r\n        b As Long", "    Dim a As Long\r\n    Dim b As Long", DisplayName = "on two lines")]
    [DataRow("    Dim a As Long, b As Long ' a comment", "    Dim a As Long\r\n    Dim b As Long ' a comment", DisplayName = "a comment after it")]
    public async Task ADeclarationOfSeveralNames_IsOneDeclarationALine(string declaration, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.MultipleDeclarations, ["Private Sub S()", declaration, "End Sub"], ["Private Sub S()", written, "End Sub"]);

    [TestMethod]
    [DataRow("Private a As Long, b As String", "Private a As Long\r\nPrivate b As String", DisplayName = "Private variables")]
    [DataRow("Public a As Long, b As String", "Public a As Long\r\nPublic b As String", DisplayName = "Public variables")]
    [DataRow("Private Const A As Long = 1, B As Long = 2", "Private Const A As Long = 1\r\nPrivate Const B As Long = 2", DisplayName = "Private constants")]
    public async Task AModuleLevelDeclarationOfSeveralNames_IsOneDeclarationALine(string declaration, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.MultipleDeclarations, [declaration], [written]);

    [TestMethod]
    public async Task AVariableOfNoType_DeclaredOnALineOfItsOwn_IsStillAVariant()
        => await AssertFixedAsync(RDCoreDiagnosticId.MultipleDeclarations,
            ["Public Sub Main()", "    Dim a, b As Long", "    Debug.Print TypeName(a); TypeName(b)", "End Sub"],
            ["Public Sub Main()", "    Dim a", "    Dim b As Long", "    Debug.Print TypeName(a); TypeName(b)", "End Sub"],
            runs: true);

    [TestMethod]
    public async Task ADeclarationOfSeveralNames_InASingleLineIf_IsNotSplit()
        => await AssertNoFixAsync(RDCoreDiagnosticId.MultipleDeclarations, "Private Sub S(ByVal x As Boolean)", "    If x Then Dim a As Long, b As Long", "End Sub");

    [TestMethod]
    [DataRow("Private Sub S(ByVal _\r\n    n As Long)", "Private Sub S(ByVal n As Long)", DisplayName = "after its modifier")]
    [DataRow("Private Sub S(ByVal n _\r\n    As Long)", "Private Sub S(ByVal n As Long)", DisplayName = "before its As clause")]
    [DataRow("Private Sub S(ByVal n As _\r\n    Long)", "Private Sub S(ByVal n As Long)", DisplayName = "inside its As clause")]
    [DataRow("Private Sub S(Optional ByVal n As Long _\r\n    = 5)", "Private Sub S(Optional ByVal n As Long = 5)", DisplayName = "before its default value")]
    [DataRow("Private Sub S(ByRef n() _\r\n    As Long)", "Private Sub S(ByRef n() As Long)", DisplayName = "an array")]
    public async Task AParameterSplitOverLines_IsWrittenOnOne(string header, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.MultilineParameterDeclaration, [header, "End Sub"], [written, "End Sub"]);

    [TestMethod]
    public async Task AParameterNamedBetweenBrackets_IsNotRewritten()
        => await AssertNoFixAsync(RDCoreDiagnosticId.MultilineParameterDeclaration, "Private Sub S(ByVal _", "    [n] As Long)", "End Sub");

    [TestMethod]
    public async Task TheValueParameterOfAPropertyLet_WrittenByRef_IsWrittenByVal()
        => await AssertFixedAsync(RDCoreDiagnosticId.MisleadingByRefParameter,
            ["Private mSize As Long", "Public Property Let Size(ByRef value As Long)", "    mSize = value", "End Property"],
            ["Private mSize As Long", "Public Property Let Size(ByVal value As Long)", "    mSize = value", "End Property"]);
}
