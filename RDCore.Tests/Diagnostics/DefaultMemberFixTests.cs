using RDCore.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The fixes of <c>RDC00119</c>, <c>RDC00121</c>, <c>RDC00123</c> and <c>RDC00125</c>: each names the default member the code calls without naming it
/// (MS-VBAL §5.6.2.2, §5.4.3.8, §5.6.13, §5.6.14), and the code does what it did.
/// </summary>
[TestClass]
public sealed class DefaultMemberFixTests
{
    private static readonly (string Name, string Source) Cell = ("Cell", ModuleWorkspace.ClassModule("Cell", "Option Explicit", "Private mValue As Long",
        "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "    Value = mValue", "End Property",
        "Public Property Let Value(ByVal v As Long)", "    mValue = v", "End Property"));

    // a class whose default member takes only an optional argument
    private static readonly (string Name, string Source) Items = ("Items", ModuleWorkspace.ClassModule("Items", "Option Explicit",
        "Public Function Item(Optional ByVal i As Variant) As Long", "Attribute Item.VB_UserMemId = 0", "    Item = 7", "End Function"));

    // a class with a property that returns a Cell, and no Property Let
    private static readonly (string Name, string Source) Sheet = ("Sheet", ModuleWorkspace.ClassModule("Sheet", "Option Explicit", "Private mCell As New Cell",
        "Public Property Get Current() As Cell", "    Set Current = mCell", "End Property"));

    private static readonly IReadOnlyList<(string Name, string Source)> Classes = [Cell, Items, Sheet];

    // the lines of a module whose Sub Main has the given body
    private static string[] Body(params string[] body) => ["Option Explicit", "Public Sub Main()", .. body, "End Sub"];

    [TestMethod]
    public Task AnObjectReadAsAValue_IsReadThroughItsDefaultMember()
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.ImplicitDefaultMemberAccess,
            Body("Dim c As New Cell", "c.Value = 3", "Dim x As Long", "x = c", "Debug.Print x"),
            Body("Dim c As New Cell", "c.Value = 3", "Dim x As Long", "x = c.Value", "Debug.Print x"),
            runs: true, classes: Classes);

    [TestMethod]
    public Task AnObjectPassedToAValueParameter_IsPassedItsDefaultMember()
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.ImplicitDefaultMemberAccess,
            [.. Body("Dim c As New Cell", "c.Value = 3", "Report c"), "Private Sub Report(ByVal amount As Long)", "Debug.Print amount", "End Sub"],
            [.. Body("Dim c As New Cell", "c.Value = 3", "Report c.Value"), "Private Sub Report(ByVal amount As Long)", "Debug.Print amount", "End Sub"],
            runs: true, classes: Classes);

    [TestMethod]
    public Task AValueAssignedToAnObject_IsAssignedToItsDefaultMember()
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.ImplicitDefaultMemberAccess,
            Body("Dim c As New Cell", "c = 42", "Debug.Print c.Value"),
            Body("Dim c As New Cell", "c.Value = 42", "Debug.Print c.Value"),
            runs: true, classes: Classes);

    [TestMethod]
    public Task AValueAssignedToNothing_RaisesError91AsItDid()
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.ImplicitDefaultMemberAccess,
            Body("Dim c As Cell", "Set c = Nothing", "On Error Resume Next", "c = 42", "Debug.Print Err.Number"),
            Body("Dim c As Cell", "Set c = Nothing", "On Error Resume Next", "c.Value = 42", "Debug.Print Err.Number"),
            runs: true, classes: Classes);

    [TestMethod]
    public Task AValueAssignedToAParameter_IsAssignedToItsDefaultMember()
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.ImplicitDefaultMemberAccess,
            [.. Body("Dim c As New Cell", "Poke c", "Debug.Print c.Value"), "Private Sub Poke(ByVal p As Cell)", "p = 9", "End Sub"],
            [.. Body("Dim c As New Cell", "Poke c", "Debug.Print c.Value"), "Private Sub Poke(ByVal p As Cell)", "p.Value = 9", "End Sub"],
            runs: true, classes: Classes);

    [TestMethod]
    public Task AValueAssignedToTheObjectAPropertyReturns_IsAssignedToItsDefaultMember()
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.ImplicitDefaultMemberAccess,
            Body("Dim s As New Sheet", "s.Current = 42", "Debug.Print s.Current.Value"),
            Body("Dim s As New Sheet", "s.Current.Value = 42", "Debug.Print s.Current.Value"),
            runs: true, classes: Classes);

    [TestMethod]
    public Task ADefaultMemberThatRequiresAnArgument_IsNotNamed()
        => CodeActionFixture.AssertNoFixAsync(
            RDCoreDiagnosticId.ImplicitDefaultMemberAccess, Classes, Body("Dim c As New Collection", "Dim x As Variant", "x = c"));

    [TestMethod]
    public Task AnArgumentList_IsGivenToTheNamedDefaultMember()
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.IndexedDefaultMemberAccess,
            Body("Dim c As New Collection", "c.Add 5", "Debug.Print c(1)"),
            Body("Dim c As New Collection", "c.Add 5", "Debug.Print c.Item(1)"),
            runs: true, classes: Classes);

    [TestMethod]
    public Task AnEmptyArgumentList_IsGivenToTheNamedDefaultMember()
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.IndexedDefaultMemberAccess,
            Body("Dim t As New Items", "Debug.Print t()"),
            Body("Dim t As New Items", "Debug.Print t.Item()"),
            runs: true, classes: Classes);

    [TestMethod]
    [DataRow("c!k", "c.Item(\"k\")", DisplayName = "a name")]
    [DataRow("c![a b]", "c.Item(\"a b\")", DisplayName = "a name in brackets")]
    public Task ABangName_IsGivenToTheNamedDefaultMemberAsAString(string bang, string named)
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.BangNotation,
            Body("Dim c As New Collection", "c.Add 1, \"k\"", "c.Add 2, \"a b\"", $"Debug.Print {bang}"),
            Body("Dim c As New Collection", "c.Add 1, \"k\"", "c.Add 2, \"a b\"", $"Debug.Print {named}"),
            runs: true, classes: Classes);

    [TestMethod]
    public Task ABangNameInAWithBlock_IsGivenToTheNamedDefaultMemberOfItsObject()
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.BangNotation,
            Body("Dim c As New Collection", "c.Add 1, \"k\"", "With c", "Debug.Print !k", "End With"),
            Body("Dim c As New Collection", "c.Add 1, \"k\"", "With c", "Debug.Print .Item(\"k\")", "End With"),
            runs: true, classes: Classes);

    [TestMethod]
    public Task AnObjectLetToAnObject_KeepsTheLetOfTheValues()
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.SuspiciousLetAssignment,
            Body("Dim a As New Cell", "Dim b As New Cell", "b.Value = 4", "a = b", "b.Value = 5", "Debug.Print a.Value"),
            Body("Dim a As New Cell", "Dim b As New Cell", "b.Value = 4", "a.Value = b.Value", "b.Value = 5", "Debug.Print a.Value"),
            runs: true, classes: Classes);

    [TestMethod]
    public Task AnObjectLetToAnObjectVariable_IsNotFixed()
        => CodeActionFixture.AssertNoFixAsync(
            RDCoreDiagnosticId.SuspiciousLetAssignment, Classes, Body("Dim o As Object", "Set o = New Cell", "Dim b As New Cell", "o = b"));
}
