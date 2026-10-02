using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.2.4</strong> <c>For Each</c> over an object: its enumeration member (<c>_NewEnum</c>, <c>VB_UserMemId = -4</c>) returns the enumerator that is asked for each
/// member in turn, which MS-VBAL leaves "implementation-defined".
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.2.4 For Each Statement")]
public sealed class ForEachObjectTests
{
    private static async Task<string[]> RunAsync(params string[] body)
    {
        var result = await SupportedLanguageTests.RunAsync(expression: "", statement: string.Join("\r\n", body));

        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, $"{result.ErrorMessage} {string.Join("; ", result.Diagnostics ?? [])}");
        return [.. result.Output.Select(line => line.Trim())];
    }

    private static string Program(params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    // a class that holds a collection and is enumerated by it: what almost every VBA class that is a collection is.
    private static (string, string) Bag(string newEnumType)
        => ("Bag", ModuleWorkspace.ClassModule("Bag",
            "Private mItems As New Collection",
            "Public Sub Add(ByVal v As Variant)", "mItems.Add v", "End Sub",
            $"Public Property Get NewEnum() As {newEnumType}", "Attribute NewEnum.VB_UserMemId = -4", "Set NewEnum = mItems.[_NewEnum]", "End Property"));

    // a class that is its own enumerator: MoveNext and Current, which is what the enumerator of the library has.
    private static readonly (string, string) Countdown = ("Countdown", ModuleWorkspace.ClassModule("Countdown",
        "Private mN As Long",
        "Public Function MoveNext() As Boolean", "mN = mN + 1", "MoveNext = (mN <= 3)", "End Function",
        "Public Property Get Current() As Variant", "Current = mN * 10", "End Property",
        "Public Property Get NewEnum() As Object", "Attribute NewEnum.VB_UserMemId = -4", "Set NewEnum = Me", "End Property"));

    // ---- a Collection ----

    [TestMethod]
    public async Task ACollection_IsEnumeratedInOrder()
        => CollectionAssert.AreEqual(new[] { "1", "2", "3" },
            await RunAsync("Dim c As New Collection", "c.Add 1", "c.Add 2", "c.Add 3", "Dim v As Variant", "For Each v In c", "Debug.Print v", "Next"));

    [TestMethod]
    public async Task AnEmptyCollection_RunsTheBodyNever()
        => CollectionAssert.AreEqual(new[] { "done" },
            await RunAsync("Dim c As New Collection", "Dim v As Variant", "For Each v In c", "Debug.Print v", "Next", "Debug.Print \"done\""));

    [TestMethod]
    public async Task ExitFor_LeavesTheLoop()
        => CollectionAssert.AreEqual(new[] { "1", "after" },
            await RunAsync("Dim c As New Collection", "c.Add 1", "c.Add 2", "Dim v As Variant", "For Each v In c", "Debug.Print v", "Exit For", "Next", "Debug.Print \"after\""));

    [TestMethod]
    public async Task NestedLoops_OverTwoCollections_EachHaveTheirOwnEnumerator()
        => CollectionAssert.AreEqual(new[] { "10", "20", "20", "40" },
            await RunAsync("Dim a As New Collection", "Dim b As New Collection", "a.Add 1", "a.Add 2", "b.Add 10", "b.Add 20",
                "Dim i As Variant", "Dim j As Variant", "For Each i In a", "For Each j In b", "Debug.Print i * j", "Next", "Next"));

    [TestMethod]
    public async Task TwoLoops_OverTheSameCollection_EachStartAtTheFirstMember()
        => CollectionAssert.AreEqual(new[] { "1", "2", "1", "2" },
            await RunAsync("Dim c As New Collection", "c.Add 1", "c.Add 2", "Dim v As Variant",
                "For Each v In c", "Debug.Print v", "Next", "For Each v In c", "Debug.Print v", "Next"));

    [TestMethod]
    public async Task AMemberAddedWhileTheLoopRuns_IsNotTheLoops()
        => CollectionAssert.AreEqual(new[] { "1", "2", "4" },
            await RunAsync("Dim c As New Collection", "c.Add 1", "c.Add 2", "Dim v As Variant", "For Each v In c", "Debug.Print v", "c.Add 9", "Next", "Debug.Print c.Count"));

    [TestMethod]
    public async Task TheControlVariable_HoldsTheLastMember_WhenTheLoopIsOver()
        => CollectionAssert.AreEqual(new[] { "3" },
            await RunAsync("Dim c As New Collection", "c.Add 1", "c.Add 2", "c.Add 3", "Dim v As Variant", "For Each v In c", "Next", "Debug.Print v"));

    [TestMethod]
    public async Task Members_ThatAreObjects_AreSetAssignedToAControlOfTheirClass()
        => CollectionAssert.AreEqual(new[] { "1", "2" },
            await RunAsync("Dim c As New Collection", "Dim a As New Collection", "Dim b As New Collection", "a.Add 1", "b.Add 1", "b.Add 2", "c.Add a", "c.Add b",
                "Dim x As Collection", "For Each x In c", "Debug.Print x.Count", "Next"));

    [TestMethod]
    public async Task Members_ThatAreObjects_AreTheObjectsThemselves_InAVariantControl()
        => CollectionAssert.AreEqual(new[] { "5" },
            await RunAsync("Dim c As New Collection", "Dim d As New Collection", "d.Add 5", "c.Add d", "Dim x As Variant", "For Each x In c", "Debug.Print x(1)", "Next"));

    [TestMethod]
    public async Task Members_OfAnyType_AreLetAssignedToAVariantControl()
        => CollectionAssert.AreEqual(new[] { "text", "2.5", "True" },
            await RunAsync("Dim c As New Collection", "c.Add \"text\"", "c.Add 2.5", "c.Add True", "Dim v As Variant", "For Each v In c", "Debug.Print v", "Next"));

    [TestMethod]
    public async Task Members_AreLetCoercedToAControlOfAnotherType()
        => CollectionAssert.AreEqual(new[] { "3", "4" },
            await RunAsync("Dim c As New Collection", "c.Add 3", "c.Add 4", "Dim n As Long", "For Each n In c", "Debug.Print n", "Next"));

    // ---- a collection that a class holds ----

    [TestMethod]
    [DataRow("IUnknown")]
    [DataRow("Object")]
    public async Task AClass_WhoseEnumerationMemberReturnsTheEnumeratorOfACollection_IsEnumeratedByIt(string returns)
        => CollectionAssert.AreEqual(new[] { "7", "8" },
            await ModuleWorkspace.RunAsync([Bag(returns)], Program("Dim b As New Bag", "b.Add 7", "b.Add 8", "Dim v As Variant", "For Each v In b", "Debug.Print v", "Next")));

    [TestMethod]
    public async Task AClass_IsEnumeratedThroughAVariableDeclaredAsObject()
        => CollectionAssert.AreEqual(new[] { "7" },
            await ModuleWorkspace.RunAsync([Bag("IUnknown")], Program("Dim b As New Bag", "b.Add 7", "Dim o As Object", "Set o = b", "Dim v As Variant", "For Each v In o", "Debug.Print v", "Next")));

    [TestMethod]
    public async Task AClass_IsEnumeratedThroughAVariantThatHoldsIt()
        => CollectionAssert.AreEqual(new[] { "7" },
            await ModuleWorkspace.RunAsync([Bag("IUnknown")], Program("Dim b As New Bag", "b.Add 7", "Dim h As Variant", "Set h = b", "Dim v As Variant", "For Each v In h", "Debug.Print v", "Next")));

    // ---- an enumerator written in VBA ----

    [TestMethod]
    public async Task AClass_ThatIsItsOwnEnumerator_IsEnumeratedByItsMoveNextAndCurrent()
        => CollectionAssert.AreEqual(new[] { "10", "20", "30" },
            await ModuleWorkspace.RunAsync([Countdown], Program("Dim c As New Countdown", "Dim v As Variant", "For Each v In c", "Debug.Print v", "Next")));

    // ---- what is not enumerable ----

    [TestMethod]
    public async Task AnObject_WithNoEnumerationMember_IsError438()
    {
        var plain = ("Plain", ModuleWorkspace.ClassModule("Plain", "Public X As Long"));

        // a handler, not Resume Next: resumed after the failed statement, the loop's own Next would raise 92 over it.
        CollectionAssert.AreEqual(new[] { "438" },
            await ModuleWorkspace.RunAsync([plain], Program("Dim p As New Plain", "Dim v As Variant", "On Error GoTo Failed", "For Each v In p", "Next", "Exit Sub", "Failed:", "Debug.Print Err.Number")));
    }

    [TestMethod]
    public async Task ANothing_IsError91()
        => CollectionAssert.AreEqual(new[] { "91" },
            await RunAsync("Dim c As Collection", "Dim v As Variant", "On Error GoTo Failed", "For Each v In c", "Next", "Exit Sub", "Failed:", "Debug.Print Err.Number"));

    // ---- the pieces this rests on ----

    [TestMethod]
    public async Task AFunction_ThatReturnsAnObject_SetsItsOwnName()
        => CollectionAssert.AreEqual(new[] { "2" },
            await ModuleWorkspace.RunAsync([], Program("Debug.Print Make().Count") + "Public Function Make() As Collection\r\nSet Make = New Collection\r\nMake.Add 1\r\nMake.Add 2\r\nEnd Function\r\n"));

    [TestMethod]
    public async Task AWorkspaceClass_HasItsDefaultMember_ByTheAttributeItsModuleStates()
    {
        var table = ("Table", ModuleWorkspace.ClassModule("Table",
            "Private mValue As Long",
            "Public Property Get Item(ByVal i As Long) As Long", "Attribute Item.VB_UserMemId = 0", "Item = i * 100", "End Property"));

        CollectionAssert.AreEqual(new[] { "300" },
            await ModuleWorkspace.RunAsync([table], Program("Dim t As New Table", "Debug.Print t(3)")));
    }
}
