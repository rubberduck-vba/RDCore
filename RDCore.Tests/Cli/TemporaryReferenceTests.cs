using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// What an expression evaluates to holds the objects it references for as long as the statement that evaluates it runs (<strong>MS-VBAL
/// §5.3.1.10</strong>: an object is destroyed when nothing references it, and the statement's own value does): the object <c>New</c> makes, what a
/// function returns, the object of a <c>With</c> block for as long as the block runs, and <c>Me</c> for as long as a member of the object runs.
/// </summary>
/// <remarks>
/// Before these, none of them was counted: an object a function returned was never destroyed, one only a <c>With</c> block or <c>Me</c> still held
/// was destroyed while it was in use, and one nothing kept was never destroyed either.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.10 Object lifetime")]
public sealed class TemporaryReferenceTests
{
    private static readonly (string, string) Widget = ("Widget", ClassModule("Widget",
        "Public Name As String",
        "Public Function Hello() As String",
        "    Hello = \"hello \" & Name",
        "End Function",
        "Public Sub DropTheLastVariable()",
        "    Forget",
        "    Debug.Print \"still \" & Name",
        "End Sub",
        "Private Sub Class_Terminate()",
        "    Debug.Print \"terminated \" & Name",
        "End Sub"));

    private static Task<string[]> RunWith(string declarations, params string[] lines)
        => RunAsync([Widget], $"Attribute VB_Name = \"Program\"\r\nPublic Kept As Widget\r\nPublic Sub Forget()\r\n    Set Kept = Nothing\r\nEnd Sub\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    private const string Make = "Private Function Make(ByVal name As String) As Widget\r\n    Set Make = New Widget\r\n    Make.Name = name\r\nEnd Function";

    #region New

    // its Name is empty: the lines end with a space, which the output does not keep. The object is the argument, held by the parameter while the
    // function runs and by the expression's value until the statement is over.
    [TestMethod]
    public async Task ANewObjectNothingKeeps_IsDestroyedWhenTheStatementIsOver()
        => CollectionAssert.AreEqual(new[] { "hello", "terminated", "after" }, await RunWith(
            "Private Function Greet(ByVal w As Widget) As String\r\n    Greet = w.Hello\r\nEnd Function",
            "Debug.Print Greet(New Widget)",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task ANewObjectAssignedToAVariable_LivesAsLongAsTheVariableHoldsIt()
        => CollectionAssert.AreEqual(new[] { "w", "terminated w", "after" }, await RunWith(string.Empty,
            "Dim w As Widget",
            "Set w = New Widget",
            "w.Name = \"w\"",
            "Debug.Print w.Name",
            "Set w = Nothing",
            "Debug.Print \"after\""));

    #endregion

    #region what a function returns

    [TestMethod]
    public async Task AnObjectAFunctionReturns_AndNothingKeeps_IsDestroyedWhenTheStatementIsOver()
        => CollectionAssert.AreEqual(new[] { "hello m", "terminated m", "after" }, await RunWith(Make,
            "Debug.Print Make(\"m\").Hello",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task AnObjectAFunctionReturns_LivesAsLongAsTheVariableItIsAssignedTo()
        => CollectionAssert.AreEqual(new[] { "m", "terminated m", "after" }, await RunWith(Make,
            "Dim w As Widget",
            "Set w = Make(\"m\")",
            "Debug.Print w.Name",
            "Set w = Nothing",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task AnObjectALocalOfTheFunctionHeld_IsReturned_AndLivesOn()
        => CollectionAssert.AreEqual(new[] { "l", "terminated l", "after" }, await RunWith(
            "Private Function Pass() As Object\r\n    Dim o As Widget\r\n    Set o = New Widget\r\n    o.Name = \"l\"\r\n    Set Pass = o\r\nEnd Function",
            "Dim w As Object",
            "Set w = Pass()",
            "Debug.Print w.Name",
            "Set w = Nothing",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task AnObjectAFunctionsResultWasSetTo_ThenAnother_IsDestroyedWhenTheResultIsSetAgain()
        => CollectionAssert.AreEqual(new[] { "terminated first", "second", "terminated second", "after" }, await RunWith(
            "Private Function Twice() As Widget\r\n    Set Twice = New Widget\r\n    Twice.Name = \"first\"\r\n    Set Twice = New Widget\r\n    Twice.Name = \"second\"\r\nEnd Function",
            "Dim w As Widget",
            "Set w = Twice()",
            "Debug.Print w.Name",
            "Set w = Nothing",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task AnArrayOfObjectsAFunctionReturns_KeepsThemAsLongAsTheVariableItIsAssignedTo()
        => CollectionAssert.AreEqual(new[] { "a b", "terminated a", "terminated b", "after" }, await RunWith(
            Make + "\r\nPrivate Function Both() As Variant\r\n    Both = Array(Make(\"a\"), Make(\"b\"))\r\nEnd Function",
            "Dim v As Variant",
            "v = Both()",
            "Debug.Print v(0).Name & \" \" & v(1).Name",
            "v = Empty",
            "Debug.Print \"after\""));

    #endregion

    #region With

    [TestMethod]
    public async Task TheObjectOfAWithBlock_LivesUntilTheBlockIsOver()
        => CollectionAssert.AreEqual(new[] { "hello w", "terminated w", "after" }, await RunWith(string.Empty,
            "With New Widget",
            "    .Name = \"w\"",
            "    Debug.Print .Hello",
            "End With",
            "Debug.Print \"after\""));

    // MS-VBAL §5.4.2.21: "If <expression> is classified as a variable, that variable is the With block variable" - the block does not hold the
    // object: the variable does, and what it is assigned is what the block reads.
    [TestMethod]
    public async Task AWithBlockOnAVariable_IsTheVariable_AndHoldsNothingOfItsOwn()
        => CollectionAssert.AreEqual(new[] { "terminated k", "91", "after" }, await RunWith(string.Empty,
            "On Error Resume Next",
            "Set Kept = New Widget",
            "Kept.Name = \"k\"",
            "With Kept",
            "    Set Kept = Nothing",
            "    Debug.Print .Hello",
            "    Debug.Print Err.Number",
            "End With",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task TheObjectOfAWithBlockOnAFunctionsResult_OutlivesTheStatement_UntilTheBlockIsOver()
        => CollectionAssert.AreEqual(new[] { "hello m", "terminated m", "after" }, await RunWith(Make,
            "With Make(\"m\")",
            "    Debug.Print .Hello",
            "End With",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task TheObjectOfAWithBlockAProcedureLeavesFromInside_IsDestroyedWhenTheProcedureReturns()
        => CollectionAssert.AreEqual(new[] { "in", "terminated x", "after" }, await RunWith(
            "Private Sub Leave()\r\n    With New Widget\r\n        .Name = \"x\"\r\n        Debug.Print \"in\"\r\n        Exit Sub\r\n    End With\r\nEnd Sub",
            "Leave",
            "Debug.Print \"after\""));

    #endregion

    #region Me

    [TestMethod]
    public async Task AnObject_WhoseMemberLetsGoOfItsLastVariable_LivesUntilTheMemberReturns()
        => CollectionAssert.AreEqual(new[] { "still k", "terminated k", "after" }, await RunWith(string.Empty,
            "Set Kept = New Widget",
            "Kept.Name = \"k\"",
            "Kept.DropTheLastVariable",
            "Debug.Print \"after\""));

    #endregion

    #region For Each

    [TestMethod]
    // MS-VBAL §5.4.2.4: the elements of an array declared Object are Set-assigned to the control variable. The array is the function's result,
    // which only the loop holds once the statement that opened it is over.
    public async Task TheObjectsOfAnArrayAForEachEnumerates_LiveUntilTheLoopIsOver()
        => CollectionAssert.AreEqual(new[] { "a", "b", "terminated a", "terminated b", "after" }, await RunWith(
            Make + "\r\nPrivate Function Both() As Variant\r\n    Dim r(1) As Object\r\n    Set r(0) = Make(\"a\")\r\n    Set r(1) = Make(\"b\")\r\n    Both = r\r\nEnd Function",
            "Dim x As Object",
            "For Each x In Both()",
            "    Debug.Print x.Name",
            "Next",
            "Set x = Nothing",
            "Debug.Print \"after\""));

    [TestMethod]
    public async Task TheMembersOfACollectionAForEachEnumerates_OutliveTheCollection_UntilTheLoopReachesThem()
        => CollectionAssert.AreEqual(new[] { "a", "b", "terminated a", "terminated b", "after" }, await RunWith(Make,
            "Dim c As Collection, x As Variant",
            "Set c = New Collection",
            "c.Add Make(\"a\")",
            "c.Add Make(\"b\")",
            "For Each x In c",
            "    Set c = Nothing",
            "    Debug.Print x.Name",
            "Next",
            "x = Empty",
            "Debug.Print \"after\""));

    #endregion
}
