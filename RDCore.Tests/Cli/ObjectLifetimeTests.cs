using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An object is destroyed - its <c>Class_Terminate</c> runs - when the last thing that held it lets it go, and not before
/// (<strong>MS-VBAL §5.3.1.10</strong>): whatever holds it, a variable, a <c>Variant</c>, an element of an array, a field of a record, a member of a
/// collection or a <c>ByVal</c> parameter, keeps it alive, and lets it go when it is assigned something else or ceases to exist.
/// </summary>
/// <remarks>
/// Before these, only a <c>Set</c> to a variable was counted: an object something else held as well was destroyed while that still held it, and one a
/// <c>Variant</c> held was never destroyed at all.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.10 Object lifetime")]
public sealed class ObjectLifetimeTests
{
    private static readonly (string, string) Widget = ("Widget", ClassModule("Widget",
        "Public Name As String",
        "Private Sub Class_Terminate()",
        "    Debug.Print \"terminated \" & Name",
        "End Sub"));

    private static Task<string[]> Run(params string[] lines) => RunWith(string.Empty, lines);

    private static Task<string[]> RunWith(string declarations, params string[] lines)
        => RunAsync([Widget], $"Attribute VB_Name = \"Program\"\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    #region not destroyed while something still holds it

    [TestMethod]
    public async Task AnObjectACollectionHolds_OutlivesTheVariableThatWasSetToIt()
        => CollectionAssert.AreEqual(new[] { "w", "terminated w", "after" }, await Run(
            "Dim c As New Collection",
            "Dim o As Object",
            "Set o = New Widget",
            "o.Name = \"w\"",
            "c.Add o",
            "Set o = Nothing",
            "Debug.Print c(1).Name",
            "c.Remove 1",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task AnObjectAVariantArrayHolds_OutlivesTheVariableThatWasSetToIt()
        => CollectionAssert.AreEqual(new[] { "w", "terminated w", "after" }, await Run(
            "Dim a(1) As Variant",
            "Dim o As Object",
            "Set o = New Widget",
            "o.Name = \"w\"",
            "Set a(0) = o",
            "Set o = Nothing",
            "Debug.Print a(0).Name",
            "a(0) = Empty",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task AnObjectAByValParameterHolds_OutlivesTheVariableItWasPassedFrom()
        => CollectionAssert.AreEqual(new[] { "w", "terminated w", "after" }, await RunWith(
            "Private g As Object\r\nPrivate Sub Use(ByVal o As Object)\r\n    Set g = Nothing\r\n    Debug.Print o.Name\r\nEnd Sub",
            "Set g = New Widget",
            "g.Name = \"w\"",
            "Use g",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task AnObjectARecordFieldHolds_OutlivesTheVariableThatWasSetToIt()
        => CollectionAssert.AreEqual(new[] { "w", "terminated w", "after" }, await RunWith(
            "Private Type THolder\r\n    Item As Object\r\nEnd Type",
            "Dim r As THolder",
            "Dim o As Object",
            "Set o = New Widget",
            "o.Name = \"w\"",
            "Set r.Item = o",
            "Set o = Nothing",
            "Debug.Print r.Item.Name",
            "Set r.Item = Nothing",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task AnObjectInACopiedArray_OutlivesTheArrayItWasCopiedFrom()
        => CollectionAssert.AreEqual(new[] { "w", "terminated w", "after" }, await Run(
            "Dim v As Variant, w As Variant",
            "Dim o As Object",
            "Set o = New Widget",
            "o.Name = \"w\"",
            "v = Array(o)",
            "Set o = Nothing",
            "w = v",
            "v = Empty",
            "Debug.Print w(0).Name",
            "w = Empty",
            "Debug.Print \"after\""));

    #endregion

    #region destroyed when the last thing that held it lets it go

    // a Let of a number to it is BoundAssignmentTests.AVariant_ThatHeldAnObject_LetsItGo_WhenANumberIsAssignedToIt.
    [TestMethod]
    public async Task AVariant_ThatHeldAnObject_LetsItGo_WhenSetToNothing()
        => CollectionAssert.AreEqual(new[] { "terminated w", "after" }, await Run(
            "Dim V As Variant",
            "Set V = New Widget",
            "V.Name = \"w\"",
            "Set V = Nothing",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task ALocalVariant_LetsItsObjectGo_WhenTheProcedureEnds()
        => CollectionAssert.AreEqual(new[] { "terminated w", "after" }, await RunWith(
            "Private Sub Make()\r\n    Dim V As Variant\r\n    Set V = New Widget\r\n    V.Name = \"w\"\r\nEnd Sub",
            "Make",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task ALocalArray_LetsTheObjectsOfItsElementsGo_WhenTheProcedureEnds()
        => CollectionAssert.AreEqual(new[] { "terminated a", "terminated b", "after" }, await RunWith(
            "Private Sub Make()\r\n    Dim a(1) As Object\r\n    Set a(0) = New Widget\r\n    a(0).Name = \"a\"\r\n    Set a(1) = New Widget\r\n    a(1).Name = \"b\"\r\nEnd Sub",
            "Make",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task ACollection_LetsItsMembersGo_WhenItIsDestroyed()
        => CollectionAssert.AreEqual(new[] { "terminated w", "after" }, await Run(
            "Dim c As Collection",
            "Set c = New Collection",
            "c.Add New Widget",
            "c(1).Name = \"w\"",
            "Set c = Nothing",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task AnObjectsVariantField_LetsItsObjectGo_WhenTheObjectIsDestroyed()
        => CollectionAssert.AreEqual(new[] { "terminated inner", "after" }, await RunAsync(
            [Widget, ("Holder", ClassModule("Holder", "Public Item As Variant"))],
            "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n" +
            "Dim h As Holder\r\nSet h = New Holder\r\nSet h.Item = New Widget\r\nh.Item.Name = \"inner\"\r\nSet h = Nothing\r\nDebug.Print \"after\"\r\nEnd Sub\r\n"));

    [TestMethod]
    public async Task EraseOfAnObjectArray_LetsTheObjectsGo()
        => CollectionAssert.AreEqual(new[] { "terminated w", "after" }, await Run(
            "Dim a(0) As Object",
            "Set a(0) = New Widget",
            "a(0).Name = \"w\"",
            "Erase a",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task ForEach_LetsTheElementItHeldGo_WhenItHoldsTheNext()
        => CollectionAssert.AreEqual(new[] { "a", "b", "terminated a", "terminated b", "after" }, await Run(
            "Dim c As New Collection",
            "Dim x As Variant",
            "c.Add New Widget",
            "c(1).Name = \"a\"",
            "c.Add New Widget",
            "c(2).Name = \"b\"",
            "For Each x In c",
            "    Debug.Print x.Name",
            "Next",
            "Set c = Nothing",
            "x = Empty",
            "Debug.Print \"after\""));

    #endregion
}
