using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// An argument list written after a member that takes no arguments, called late through an <c>Object</c> or a <c>Variant</c>, is given to the default
/// member of the object the member returns (<strong>MS-VBAL §5.6.13</strong>), read or assigned; what returns no object is error 451, as IDispatch refuses
/// arguments to a property that takes none (observed in Excel). A second argument list indexes what the member returns, as it does early-bound.
/// </summary>
[TestClass]
public sealed class LateBoundParameterlessMemberTests
{
    private static readonly string Customer = ModuleWorkspace.ClassModule("Customer", "Option Explicit", "Public Name As String");

    private static readonly string Cell = ModuleWorkspace.ClassModule("Cell",
        "Option Explicit",
        "Private mV(3) As Variant",
        "Public Property Get Item(ByVal i As Long) As Variant",
        "Attribute Item.VB_UserMemId = 0",
        "    Item = mV(i)",
        "End Property",
        "Public Property Let Item(ByVal i As Long, ByVal v As Variant)",
        "Attribute Item.VB_UserMemId = 0",
        "    mV(i) = v",
        "End Property");

    // the Long counts 10 to 12 as a typed array, a Variant holding them and a Collection of them; a 2 by 3 grid with 7 at (1, 2); a Cell, as itself and in a
    // Variant; a String and a Long.
    private static readonly string Basket = ModuleWorkspace.ClassModule("Basket",
        "Option Explicit",
        "Private mItems() As Customer",
        "Private mCounts() As Long",
        "Private mGrid() As Long",
        "Private mBag As Collection",
        "Private mCell As Cell",
        "Private Sub Class_Initialize()",
        "    ReDim mItems(1)",
        "    Set mItems(0) = New Customer",
        "    mItems(0).Name = \"Ann\"",
        "    ReDim mCounts(2)",
        "    mCounts(0) = 10",
        "    mCounts(1) = 11",
        "    mCounts(2) = 12",
        "    ReDim mGrid(1, 2)",
        "    mGrid(1, 2) = 7",
        "    Set mBag = New Collection",
        "    mBag.Add 10",
        "    mBag.Add 11",
        "    mBag.Add 12",
        "    Set mCell = New Cell",
        "End Sub",
        "Public Property Get Items() As Customer()",
        "    Items = mItems",
        "End Property",
        "Public Property Get Counts() As Long()",
        "    Counts = mCounts",
        "End Property",
        "Public Property Get Grid() As Long()",
        "    Grid = mGrid",
        "End Property",
        "Public Function Totals() As Long()",
        "    Totals = mCounts",
        "End Function",
        "Public Property Get Wrapped() As Variant",
        "    Wrapped = mCounts",
        "End Property",
        "Public Function WrappedF() As Variant",
        "    WrappedF = mCounts",
        "End Function",
        "Public Property Get Bag() As Collection",
        "    Set Bag = mBag",
        "End Property",
        "Public Property Get BagO() As Object",
        "    Set BagO = mBag",
        "End Property",
        "Public Property Get BagV() As Variant",
        "    Set BagV = mBag",
        "End Property",
        "Public Function BagF() As Collection",
        "    Set BagF = mBag",
        "End Function",
        "Public Property Get TheCell() As Cell",
        "    Set TheCell = mCell",
        "End Property",
        "Public Property Get CellV() As Variant",
        "    Set CellV = mCell",
        "End Property",
        "Public Property Get Label() As String",
        "    Label = \"abc\"",
        "End Property",
        "Public Property Get Size() As Long",
        "    Size = 3",
        "End Property");

    private static string Program(params string[] body)
        => ModuleWorkspace.StandardModule("Program",
            ["Option Explicit", "Public Sub Main()", "    Dim o As Object", "    Set o = New Basket", "    Dim v As Variant", "    Set v = o",
                .. body.Select(line => "    " + line), "End Sub"]);

    private static async Task AssertInBothEnginesAsync(string expected, params string[] body)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var printed = await ModuleWorkspace.RunAsync([("Customer", Customer), ("Cell", Cell), ("Basket", Basket)], Program(body), engine);
            Assert.AreEqual(expected, string.Join(" | ", printed), engine.ToString());
        }
    }

    // MS-VBA prints each, and Err.Number 0.
    [TestMethod]
    [DataRow("Debug.Print o.Bag(2)", "11", DisplayName = "a Property Get of a Collection")]
    [DataRow("Debug.Print o.BagO(2)", "11", DisplayName = "a Property Get of an Object")]
    [DataRow("Debug.Print o.BagV(2)", "11", DisplayName = "a Property Get of a Variant holding an object")]
    [DataRow("Debug.Print o.BagF(2)", "11", DisplayName = "a function of a Collection")]
    public Task TheObjectReturned_IsGivenTheArguments(string line, string printed)
        => AssertInBothEnginesAsync(printed + " | 0", "On Error Resume Next", line, "Debug.Print Err.Number");

    // MS-VBA, Err.Number 451 each, and nothing printed.
    [TestMethod]
    [DataRow("Debug.Print o.Counts(1)", DisplayName = "a Property Get of a Long array")]
    [DataRow("Debug.Print o.Grid(1, 2)", DisplayName = "two arguments, for a two-dimensional array")]
    [DataRow("Debug.Print o.Items(0).Name", DisplayName = "a Property Get of an object array")]
    [DataRow("Debug.Print o.Totals(1)", DisplayName = "a function of a Long array")]
    [DataRow("Debug.Print o.Wrapped(1)", DisplayName = "a Property Get of a Variant holding an array")]
    [DataRow("Debug.Print o.WrappedF(1)", DisplayName = "a function of a Variant holding an array")]
    [DataRow("Debug.Print o.Label(1)", DisplayName = "a Property Get of a String")]
    [DataRow("Debug.Print o.Size(1)", DisplayName = "a Property Get of a Long")]
    [DataRow("Debug.Print v.Counts(1)", DisplayName = "through a Variant")]
    [DataRow("With o: Debug.Print .Counts(1): End With", DisplayName = "through a With block")]
    [DataRow("Dim c As Object: Set c = New Collection: c.Add 10: Debug.Print c.Count(1)", DisplayName = "a Collection's Count")]
    [DataRow("Dim e As Object: Set e = Err: Debug.Print e.Description(1)", DisplayName = "the Err object's Description")]
    public Task WhatReturnsNoObject_IsError451(string line)
        => AssertInBothEnginesAsync("451", "On Error Resume Next", line, "Debug.Print Err.Number");

    [TestMethod]
    [DataRow("Debug.Print o.Counts()(1)", "11", DisplayName = "an element of a Long array")]
    [DataRow("Debug.Print o.Wrapped()(1)", "11", DisplayName = "an element of a Variant holding an array")]
    [DataRow("Debug.Print UBound(o.Counts)", "2", DisplayName = "UBound of the array")]
    public Task TheArrayReturned_IsIndexedByTheNextArgumentList(string line, string printed)
        => AssertInBothEnginesAsync(printed, line);

    // MS-VBA: Err.Number 0, then 5, each.
    [TestMethod]
    [DataRow("o.TheCell(1) = 5", DisplayName = "a Property Get of a class")]
    [DataRow("o.CellV(1) = 5", DisplayName = "a Property Get of a Variant holding an object")]
    public Task AValueLet_IsLetToTheDefaultMemberOfTheObjectReturned(string assignment)
        => AssertInBothEnginesAsync("0 | 5", "On Error Resume Next", assignment, "Debug.Print Err.Number", "Debug.Print o.TheCell()(1)");

    // MS-VBA, Err.Number 451 each.
    [TestMethod]
    [DataRow("o.Counts(1) = 5", DisplayName = "a Property Get of a Long array")]
    [DataRow("o.Wrapped(0) = 99", DisplayName = "a Property Get of a Variant holding an array")]
    [DataRow("v.Wrapped(0) = 99", DisplayName = "through a Variant")]
    [DataRow("o.Label(1) = \"x\"", DisplayName = "a Property Get of a String")]
    [DataRow("o.Size(1) = 5", DisplayName = "a Property Get of a Long")]
    [DataRow("Dim c As Object: Set c = New Collection: c.Add 10: c.Count(1) = 5", DisplayName = "a Collection's Count, assigned")]
    public Task AValueLetThroughWhatReturnsNoObject_IsError451(string assignment)
        => AssertInBothEnginesAsync("451", "On Error Resume Next", assignment, "Debug.Print Err.Number");

    // MS-VBA: Err.Number 0, and the property's own array is not assigned.
    [TestMethod]
    public Task AnElementAssignedByTheNextArgumentList_IsOneOfTheArrayReturned()
        => AssertInBothEnginesAsync("0 | 11", "On Error Resume Next", "o.Counts()(1) = 5", "Debug.Print Err.Number", "Debug.Print o.Counts()(1)");
}
