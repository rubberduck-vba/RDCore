using RDCore.Diagnostics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Diagnostics;

/// <summary>
/// The fix that makes a <c>Let</c> of an object a <c>Set</c> (MS-VBAL §5.4.3.9), of <c>RDC11050</c>, <c>RDC00125</c> and <c>RDC11015</c>: the target is
/// given the object itself, which changes what the code does, and is offered only where the target can hold the object.
/// </summary>
[TestClass]
public sealed class SetAssignmentFixTests
{
    // a class with no default member
    private static readonly (string Name, string Source) Plain = ("Plain", ModuleWorkspace.ClassModule("Plain", "Option Explicit", "Public Name As String"));

    // a class whose default member is a Long property
    private static readonly (string Name, string Source) Cell = ("Cell", ModuleWorkspace.ClassModule("Cell", "Option Explicit", "Private mValue As Long",
        "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "    Value = mValue", "End Property",
        "Public Property Let Value(ByVal v As Long)", "    mValue = v", "End Property"));

    // a class with a Plain property that has a Property Set, and one that has none
    private static readonly (string Name, string Source) Holder = ("Holder", ModuleWorkspace.ClassModule("Holder", "Option Explicit", "Private mItem As Plain",
        "Public Property Get Item() As Plain", "    Set Item = mItem", "End Property",
        "Public Property Set Item(ByVal v As Plain)", "    Set mItem = v", "End Property",
        "Public Property Get Only() As Plain", "    Set Only = mItem", "End Property"));

    // another class whose default member is a Long property
    private static readonly (string Name, string Source) Gauge = ("Gauge", ModuleWorkspace.ClassModule("Gauge", "Option Explicit",
        "Public Property Get Level() As Long", "Attribute Level.VB_UserMemId = 0", "    Level = 3", "End Property"));

    private static readonly IReadOnlyList<(string Name, string Source)> Classes = [Plain, Cell, Holder, Gauge];

    // the lines of a module whose Sub Main has the given body
    private static string[] Body(params string[] body) => ["Option Explicit", "Public Sub Main()", .. body, "End Sub"];

    // the fix made, as CodeActionFixture holds it, in a Main that prints whether the target holds the object; the fix says it changes what the code does,
    // and the fixed program prints that the target holds it.
    private static async Task AssertMadeASetAsync(RDCoreDiagnosticId code, string[] before, string[] after, string holds, int? choice = null)
    {
        string[] Printing(string[] body) => [.. body.SkipLast(1), $"Debug.Print {holds}", "End Sub"];
        await CodeActionFixture.AssertFixedAsync(code, Printing(before), Printing(after), choice: choice, classes: Classes);

        var found = (await CodeActionFixture.FindingsOfAsync(CodeActionFixture.Module(Printing(before)), Classes)).First(each => each.Id == code);
        var fix = choice is { } index ? found.Fixes!.Edits[index] : found.Fixes!.Edits.Single();
        Assert.IsTrue(fix.ChangesBehavior, "a Set assigns the object, where the Let assigned a value");

        var printed = await ModuleWorkspace.RunAsync(Classes, CodeActionFixture.Module(Printing(after)));
        Assert.AreEqual("True", printed.LastOrDefault(), string.Join(" | ", printed));
    }

    [TestMethod]
    [DataRow("p = q", DisplayName = "an implicit Let")]
    [DataRow("Let p = q", DisplayName = "an explicit Let")]
    public Task AnObjectLetToAnObjectWithNoDefaultMember_IsMadeASet(string let)
        => AssertMadeASetAsync(RDCoreDiagnosticId.LetToAnObjectWithNoDefaultMember,
            Body("Dim p As Plain", "Dim q As New Plain", let), Body("Dim p As Plain", "Dim q As New Plain", "Set p = q"), "p Is q");

    [TestMethod]
    public Task NothingLetToAnObjectWithNoDefaultMember_IsMadeASet()
        => AssertMadeASetAsync(RDCoreDiagnosticId.LetToAnObjectWithNoDefaultMember,
            Body("Dim p As Plain", "Set p = New Plain", "p = Nothing"), Body("Dim p As Plain", "Set p = New Plain", "Set p = Nothing"), "p Is Nothing");

    [TestMethod]
    public Task AnObjectLetToAPropertyWithAPropertySet_IsMadeASet()
        => AssertMadeASetAsync(RDCoreDiagnosticId.LetToAnObjectWithNoDefaultMember,
            Body("Dim h As New Holder", "Dim q As New Plain", "h.Item = q"), Body("Dim h As New Holder", "Dim q As New Plain", "Set h.Item = q"), "h.Item Is q");

    [TestMethod]
    public Task AnObjectLetToAnObjectOfItsClass_IsMadeASet_AfterTheFixThatKeepsTheLetOfTheValues()
        => AssertMadeASetAsync(RDCoreDiagnosticId.SuspiciousLetAssignment,
            Body("Dim a As New Cell", "Dim b As New Cell", "a = b"), Body("Dim a As New Cell", "Dim b As New Cell", "Set a = b"), "a Is b", choice: 1);

    [TestMethod]
    public Task AnObjectLetToAnObjectVariable_IsMadeASet_AndNotGivenDefaultMembers()
        => AssertMadeASetAsync(RDCoreDiagnosticId.SuspiciousLetAssignment,
            Body("Dim o As Object", "Set o = New Cell", "Dim b As New Cell", "o = b"), Body("Dim o As Object", "Set o = New Cell", "Dim b As New Cell", "Set o = b"),
            "o Is b");

    [TestMethod]
    public Task AnObjectWithNoDefaultMemberLetToAVariant_IsMadeASet()
        => AssertMadeASetAsync(RDCoreDiagnosticId.ValueOfObjectWithNoDefaultMember,
            Body("Dim v As Variant", "Dim q As New Plain", "v = q"), Body("Dim v As Variant", "Dim q As New Plain", "Set v = q"), "v Is q");

    [TestMethod]
    [DataRow(RDCoreDiagnosticId.LetToAnObjectWithNoDefaultMember, "Dim p As New Plain|p = 5", DisplayName = "a value")]
    [DataRow(RDCoreDiagnosticId.LetToAnObjectWithNoDefaultMember, "Dim p As New Plain|Dim c As New Cell|p = c", DisplayName = "an object of another class")]
    [DataRow(RDCoreDiagnosticId.LetToAnObjectWithNoDefaultMember, "Dim h As New Holder|Dim q As New Plain|h.Only = q", DisplayName = "a property with no Property Set")]
    [DataRow(RDCoreDiagnosticId.ValueOfObjectWithNoDefaultMember, "Dim q As New Plain|Dim n As Long|n = q", DisplayName = "an object let to a Long")]
    [DataRow(RDCoreDiagnosticId.ValueOfObjectWithNoDefaultMember, "Dim q As New Plain|Debug.Print q & \"\"", DisplayName = "an operand")]
    public Task ALetASetWouldNotAssign_IsNotMadeASet(RDCoreDiagnosticId code, string lines)
        => CodeActionFixture.AssertNoFixAsync(code, Classes, Body(lines.Split('|')));

    // a Gauge is not a Cell: a Set of it would raise error 13, and the only fix keeps the Let of the values.
    [TestMethod]
    public async Task AnObjectLetToAnObjectOfAnotherClass_IsNotMadeASet()
    {
        var found = (await CodeActionFixture.FindingsOfAsync(CodeActionFixture.Module(Body("Dim a As New Cell", "Dim g As New Gauge", "a = g")), Classes))
            .Where(each => each.Id == RDCoreDiagnosticId.SuspiciousLetAssignment).ToArray();

        Assert.IsNotEmpty(found, "the premise: RDC00125 is found");
        Assert.IsTrue(found.All(each => each.Fixes?.Edits is [{ ChangesBehavior: false }]), "the one fix keeps the Let of the values");
    }
}
