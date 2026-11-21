using RDCore.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The fixes of <c>RDC00119</c>, <c>RDC00121</c>, <c>RDC00123</c> and <c>RDC00125</c> to <c>RDC00127</c>: each names the default members the code calls
/// without naming them (MS-VBAL §5.6.2.2, §5.4.3.8, §5.6.13, §5.6.14), and the code does what it did.
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

    // Root's default member returns a Branch, whose default member returns a Leaf, whose default member takes the key.
    private static readonly (string Name, string Source) Leaf = ("Leaf", ModuleWorkspace.ClassModule("Leaf", "Option Explicit",
        "Public Property Get Item(ByVal key As Variant) As String", "Attribute Item.VB_UserMemId = 0", "    Item = \"leaf \" & key", "End Property"));

    private static readonly (string Name, string Source) Branch = ("Branch", ModuleWorkspace.ClassModule("Branch", "Option Explicit",
        "Public Function Child() As Leaf", "Attribute Child.VB_UserMemId = 0", "    Set Child = New Leaf", "End Function"));

    private static readonly (string Name, string Source) Root = ("Root", ModuleWorkspace.ClassModule("Root", "Option Explicit",
        "Public Property Get Inner() As Branch", "Attribute Inner.VB_UserMemId = 0", "    Set Inner = New Branch", "End Property"));

    private static readonly IReadOnlyList<(string Name, string Source)> Classes = [Cell, Items, Sheet, Leaf, Branch, Root];

    // the lines of a module whose Sub Main has the given body
    private static string[] Body(params string[] body) => ["Option Explicit", "Public Sub Main()", .. body, "End Sub"];

    // the Excel library, whose Range has a default member named _Default, which is not an identifier
    private static ModuleWorkspace.WorkspaceLibraries Excel => new(["Excel"], new RDCore.SDK.Runtime.Libraries.DirectoryLibrarySource(
        new System.IO.Abstractions.FileSystem(), RDCore.Tests.Runtime.Libraries.ShippedLibrariesTests.Symbols));

    // the lines of a module with a Sub Main, and a procedure with two Excel.Range parameters whose body is given
    private static string[] Ranges(params string[] body)
        => ["Option Explicit", "Public Sub Main()", "End Sub", "Private Sub Peek(ByVal r As Excel.Range, ByVal s As Excel.Range)", .. body, "End Sub"];

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
    [DataRow("Dim t As New Branch", "t(1)", "t.Child.Item(1)", DisplayName = "through two default members")]
    [DataRow("Dim d As New Root", "d(\"k\")", "d.Inner.Child.Item(\"k\")", DisplayName = "through three default members")]
    public Task AnArgumentListThroughSeveralDefaultMembers_IsGivenToTheLastOfThemNamed(string declaration, string indexed, string named)
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.IndexedRecursiveDefaultMemberAccess,
            Body(declaration, $"Debug.Print {indexed}"),
            Body(declaration, $"Debug.Print {named}"),
            runs: true, classes: Classes);

    [TestMethod]
    [DataRow("Dim t As New Branch", "t!Name", "t.Child.Item(\"Name\")", DisplayName = "through two default members")]
    [DataRow("Dim d As New Root", "d!Name", "d.Inner.Child.Item(\"Name\")", DisplayName = "through three default members")]
    [DataRow("Dim d As New Root", "d![a key]", "d.Inner.Child.Item(\"a key\")", DisplayName = "a name in brackets")]
    public Task ABangNameThroughSeveralDefaultMembers_IsGivenToTheLastOfThemNamedAsAString(string declaration, string bang, string named)
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.RecursiveBangNotation,
            Body(declaration, $"Debug.Print {bang}"),
            Body(declaration, $"Debug.Print {named}"),
            runs: true, classes: Classes);

    [TestMethod]
    public Task ABangNameThroughSeveralDefaultMembersInAWithBlock_IsGivenToTheLastOfThemNamed()
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.RecursiveBangNotation,
            Body("Dim d As New Root", "With d", "Debug.Print !Name", "End With"),
            Body("Dim d As New Root", "With d", "Debug.Print .Inner.Child.Item(\"Name\")", "End With"),
            runs: true, classes: Classes);

    [TestMethod]
    public Task AnObjectLetToAnObject_KeepsTheLetOfTheValues()
        => CodeActionFixture.AssertFixedAsync(
            RDCoreDiagnosticId.SuspiciousLetAssignment,
            Body("Dim a As New Cell", "Dim b As New Cell", "b.Value = 4", "a = b", "b.Value = 5", "Debug.Print a.Value"),
            Body("Dim a As New Cell", "Dim b As New Cell", "b.Value = 4", "a.Value = b.Value", "b.Value = 5", "Debug.Print a.Value"),
            runs: true, choice: 0, classes: Classes);

    // a name that is not an identifier is written in brackets, which make it a name (MS-VBAL §3.3.5.3).
    [TestMethod]
    [DataRow(RDCoreDiagnosticId.ImplicitDefaultMemberAccess, "x = r", "x = r.[_Default]", DisplayName = "read as a value")]
    [DataRow(RDCoreDiagnosticId.ImplicitDefaultMemberAccess, "r = 5", "r.[_Default] = 5", DisplayName = "assigned a value")]
    [DataRow(RDCoreDiagnosticId.IndexedDefaultMemberAccess, "Debug.Print r(1)", "Debug.Print r.[_Default](1)", DisplayName = "given an argument list")]
    [DataRow(RDCoreDiagnosticId.BangNotation, "Debug.Print r!A1", "Debug.Print r.[_Default](\"A1\")", DisplayName = "given a name with !")]
    [DataRow(RDCoreDiagnosticId.SuspiciousLetAssignment, "r = s", "r.[_Default] = s.[_Default]", DisplayName = "an object let to an object")]
    public Task ADefaultMemberNotNamedByAnIdentifier_IsNamedInBrackets(RDCoreDiagnosticId code, string implicitly, string named)
        => CodeActionFixture.AssertFixedAsync(code, Ranges("Dim x As Variant", implicitly), Ranges("Dim x As Variant", named), choice: 0, libraries: Excel);
}
