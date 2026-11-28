using RDCore.SDK.Runtime.Abstract.Execution;
using LanguageCoreErrors = RDCore.Runtime.LanguageCore.Exceptions;

namespace RDCore.Tests.Cli;

/// <summary>
/// A Let through the argument list of a member that takes no arguments and returns neither a <c>Variant</c> nor an object, through an object declared as
/// its class, is refused as a Let of the member itself is, whatever the arguments: a <c>Property Get</c> with no <c>Property Let</c> is a read-only
/// property, and a <c>Function</c> must return a <c>Variant</c> or an object (<strong>MS-VBAL §5.4.3.8</strong>; observed in Excel). A read of it, a
/// <c>Set</c> through it and a member of what it returns are the member's own argument list, and refused as one.
/// </summary>
[TestClass]
public sealed class LetThroughArgumentsOfAGetOnlyMemberTests
{
    private static readonly string Customer = ModuleWorkspace.ClassModule("Customer", "Option Explicit", "Public Name As String");

    // Property Gets of a Customer array, a Long array, a 2 by 3 Long grid, a Variant array, a Long, a String and a Collection; a Function of a Long array.
    private static readonly string Basket = ModuleWorkspace.ClassModule("Basket",
        "Option Explicit",
        "Private mItems() As Customer",
        "Private mCounts() As Long",
        "Private mGrid() As Long",
        "Private mBag As Collection",
        "Private Sub Class_Initialize()",
        "    ReDim mItems(1)",
        "    ReDim mCounts(2)",
        "    ReDim mGrid(1, 2)",
        "    Set mBag = New Collection",
        "    mBag.Add 10",
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
        "Public Property Get Variants() As Variant()",
        "    Dim v(1) As Variant",
        "    Variants = v",
        "End Property",
        "Public Property Get Size() As Long",
        "    Size = 3",
        "End Property",
        "Public Property Get Label() As String",
        "    Label = \"abc\"",
        "End Property",
        "Public Property Get Bag() As Collection",
        "    Set Bag = mBag",
        "End Property",
        "Public Function Totals() As Long()",
        "    Totals = mCounts",
        "End Function");

    private static string Program(params string[] body)
        => ModuleWorkspace.StandardModule(
            "Program", ["Option Explicit", "Public Sub Main()", "    Dim b As New Basket", .. body.Select(line => "    " + line), "End Sub"]);

    private static async Task AssertTheErrorIsAsync(string error, string line)
        => StringAssert.Contains(
            Assert.ContainsSingle(await ModuleWorkspace.LoadErrorsAsync([("Customer", Customer), ("Basket", Basket)], Program(line))), error);

    // MS-VBA, Compile error: Can't assign to read-only property, each.
    [TestMethod]
    [DataRow("b.Counts(0) = 99", DisplayName = "a Long array, given an index")]
    [DataRow("b.Grid(0, 1) = 5", DisplayName = "a two-dimensional array, given two")]
    [DataRow("b.Variants(0) = 1", DisplayName = "a Variant array, given an index")]
    [DataRow("b.Size(0) = 5", DisplayName = "a Long, given an argument")]
    [DataRow("b.Label(0) = \"x\"", DisplayName = "a String, given an argument")]
    [DataRow("b.Size = 5", DisplayName = "a Long, given none")]
    [DataRow("b.Counts = b.Counts", DisplayName = "a Long array, given none")]
    public Task ALetThroughAPropertyGetThatTakesNone_IsOfAReadOnlyProperty(string line)
        => AssertTheErrorIsAsync(LanguageCoreErrors.VBCompileError_CantAssignToReadOnlyProperty, line);

    // MS-VBA, Compile error: Function call on left-hand side of assignment must return Variant or Object, each.
    [TestMethod]
    [DataRow("b.Totals(0) = 5", DisplayName = "given an index")]
    [DataRow("b.Totals = b.Totals", DisplayName = "given none")]
    public Task ALetThroughAFunctionThatTakesNone_MustReturnAVariantOrAnObject(string line)
        => AssertTheErrorIsAsync(LanguageCoreErrors.VBCompileError_FunctionCallOnLeftHandSideMustReturnVariantOrObject, line);

    // MS-VBA, Compile error: Wrong number of arguments or invalid property assignment, each.
    [TestMethod]
    [DataRow("Debug.Print b.Size(0)", DisplayName = "a read of a Long")]
    [DataRow("Debug.Print b.Label(0)", DisplayName = "a read of a String")]
    [DataRow("Set b.Items(0) = New Customer", DisplayName = "a Set of an element of an object array")]
    [DataRow("b.Items(0).Name = \"Z\"", DisplayName = "a member of an element of an object array")]
    public Task AnArgumentReadOrSetThrough_IsTheMembersOwn(string line)
        => AssertTheErrorIsAsync(LanguageCoreErrors.VBCompileError_WrongNumberOfArgumentsOrInvalidPropertyAssignment, line);

    // MS-VBA: the Collection returned is given the arguments, and its Item, which no Let assigns, is error 424 when it runs.
    [TestMethod]
    public async Task ALetThroughAPropertyGetOfAnObject_IsOfItsDefaultMember()
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var printed = await ModuleWorkspace.RunAsync([("Customer", Customer), ("Basket", Basket)],
                Program("On Error Resume Next", "b.Bag(1) = 5", "Debug.Print Err.Number"), engine);
            Assert.AreEqual("424", string.Join(" | ", printed), engine.ToString());
        }
    }
}
