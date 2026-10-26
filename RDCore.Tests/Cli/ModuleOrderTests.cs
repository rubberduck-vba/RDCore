using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A project lists its modules in any order, and the workspace's modules are defined, and then loaded, in that order: a module that uses another listed
/// after it is the same program as one listed before it. HexIDE's parity runner lists the module it starts first (findings desk #1, #23).
/// </summary>
[TestClass]
public sealed class ModuleOrderTests
{
    private static readonly (string, string)[] Widget =
        [("Widget", ClassModule("Widget", "Public Function Value() As Long", "Attribute Value.VB_UserMemId = 0", "    Value = 42", "End Function"))];

    private static string Main(params string[] body) => StandardModule("Program", ["Public Sub Main()", .. body, "End Sub"]);

    [TestMethod]
    public async Task AClassListedAfterTheModuleThatUsesIt_HasItsDefaultMember()
        => CollectionAssert.AreEqual(new[] { "0", "42" }, await RunProgramFirstAsync(Widget, [],
            Main("Dim o As Widget", "Set o = New Widget", "Dim c", "On Error Resume Next", "c = Split(o)", "Debug.Print Err.Number", "Debug.Print o + 0")));

    [TestMethod]
    public async Task AClassListedAfter_IsIndexedThroughItsDefaultMember_AndItsTextLengthIsItsValues()
        => CollectionAssert.AreEqual(new[] { "b", "3", "abc" }, await RunProgramFirstAsync(
            [("Box", ClassModule("Box", "Public Function Item(ByVal key As String) As String", "Attribute Item.VB_UserMemId = 0", "    Item = key", "End Function")),
             ("Label", ClassModule("Label", "Public Property Get Text() As String", "Attribute Text.VB_UserMemId = 0", "    Text = \"abc\"", "End Property"))], [],
            Main("Dim b As New Box", "Debug.Print b(\"b\")", "Dim t As New Label", "Debug.Print Len(t)", "Debug.Print t")));

    // two classes that name each other: no order of the two makes both complete before the other is bound.
    [TestMethod]
    public async Task ClassesThatNameEachOther_ListedAfter_AreEachComplete()
        => CollectionAssert.AreEqual(new[] { "7", "7" }, await RunProgramFirstAsync(
            [("Node", ClassModule("Node", "Public Value As Long", "Public Function Owner() As Tree", "    Set Owner = New Tree", "End Function", "Public Function Self() As Long", "Attribute Self.VB_UserMemId = 0", "    Self = Value", "End Function")),
             ("Tree", ClassModule("Tree", "Public Function Root() As Node", "    Set Root = New Node", "    Root.Value = 7", "End Function"))], [],
            Main("Dim t As New Tree", "Dim n As Node", "Set n = t.Root", "Debug.Print n + 0", "Debug.Print t.Root.Owner.Root + 0")));

    [TestMethod]
    public async Task ATypeAndAnEnumOfAModuleListedAfter_AreTheirs()
        => CollectionAssert.AreEqual(new[] { "5", "2" }, await RunProgramFirstAsync([],
            [("Shapes", StandardModule("Shapes", "Public Type Point", "    X As Long", "    Y As Long", "End Type", "Public Type Segment", "    A As Point", "    B As Point", "End Type", "Public Enum Kind", "    kFirst = 1", "    kSecond", "End Enum"))],
            Main("Dim s As Segment", "s.B.Y = 5", "Debug.Print s.B.Y", "Dim k As Kind", "k = kSecond", "Debug.Print k")));

    [TestMethod]
    public async Task AModuleListedAfterTheModuleThatUsesIt_HasItsConstantsAndProcedures()
        => CollectionAssert.AreEqual(new[] { "100", "42" }, await RunProgramFirstAsync([],
            [("Consts", StandardModule("Consts", "Public Const MaxItems = 100")), ("Greetings", StandardModule("Greetings", "Public Sub PrintGreeting()", "    Debug.Print 42", "End Sub"))],
            Main("Debug.Print Consts.MaxItems", "PrintGreeting")));
}
