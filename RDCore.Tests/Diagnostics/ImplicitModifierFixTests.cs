using RDCore.Diagnostics;
using static RDCore.Tests.Diagnostics.CodeActionFixture;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The fixes of <c>RDC00104</c> and <c>RDC00105</c>: the modifier that is implicit, written. The code means what it meant.
/// </summary>
[TestClass]
public sealed class ImplicitModifierFixTests
{
    [TestMethod]
    [DataRow("Private Sub Bump(n As Long)", "Private Sub Bump(ByRef n As Long)", DisplayName = "a parameter")]
    [DataRow("Private Sub Bump(Optional n As Long = 1)", "Private Sub Bump(Optional ByRef n As Long = 1)", DisplayName = "an optional parameter")]
    [DataRow("Private Sub Bump(n() As Long)", "Private Sub Bump(ByRef n() As Long)", DisplayName = "an array")]
    [DataRow("Private Sub Bump(n&)", "Private Sub Bump(ByRef n&)", DisplayName = "a name with a type hint")]
    [DataRow("Private Sub Bump(ByVal a As Long, _\r\n    n As Long)", "Private Sub Bump(ByVal a As Long, _\r\n    ByRef n As Long)", DisplayName = "on a line of its own")]
    public async Task AnImplicitByRefParameter_IsWrittenByRef(string header, string written)
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitByRefModifier, [header, "End Sub"], [written, "End Sub"]);

    [TestMethod]
    public async Task AnImplicitByRefParameter_WrittenByRef_IsStillPassedByReference()
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitByRefModifier,
            ["Public Sub Main()", "    Dim x As Long", "    x = 1", "    Bump x", "    Debug.Print x", "End Sub", "Private Sub Bump(n As Long)", "    n = n + 1", "End Sub"],
            ["Public Sub Main()", "    Dim x As Long", "    x = 1", "    Bump x", "    Debug.Print x", "End Sub", "Private Sub Bump(ByRef n As Long)", "    n = n + 1", "End Sub"],
            runs: true);

    [TestMethod]
    [DataRow("Sub DoIt()", "End Sub", DisplayName = "a Sub")]
    [DataRow("Function Twice(ByVal n As Long) As Long", "End Function", DisplayName = "a Function")]
    [DataRow("Static Function Count() As Long", "End Function", DisplayName = "a Static Function")]
    [DataRow("Property Get Size() As Long", "End Property", DisplayName = "a Property Get")]
    [DataRow("Enum Colour", "    Red\r\nEnd Enum", DisplayName = "an Enum")]
    [DataRow("Type Point", "    X As Long\r\nEnd Type", DisplayName = "a user-defined type")]
    public async Task AnImplicitlyPublicMember_IsWrittenPublic(string declaration, string rest)
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitPublicMember, [declaration, rest], ["Public " + declaration, rest]);

    [TestMethod]
    public async Task AnImplicitlyPublicSub_WrittenPublic_IsStillCalled()
        => await AssertFixedAsync(RDCoreDiagnosticId.ImplicitPublicMember,
            ["Public Sub Main()", "    Hello", "End Sub", "Sub Hello()", "    Debug.Print \"hello\"", "End Sub"],
            ["Public Sub Main()", "    Hello", "End Sub", "Public Sub Hello()", "    Debug.Print \"hello\"", "End Sub"],
            runs: true);
}
