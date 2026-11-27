using RDCore.SDK.Runtime.Abstract.Execution;
using LanguageCoreErrors = RDCore.Runtime.LanguageCore.Exceptions;

namespace RDCore.Tests.Cli;

/// <summary>
/// An argument list written after a member that takes no arguments and returns a typed array, through its object, is the member's own (observed in
/// Excel), as it is for a function (<see cref="ArrayResultArgumentListTests"/>): MS-VBA refuses it, though <strong>MS-VBAL §5.6.13</strong> lets those
/// arguments index the array returned. The array is indexed by a second argument list, <c>b.Items()(0)</c>, and is the argument <c>UBound</c> takes.
/// A member that returns a Variant holding an array is indexed by the first, and an element assigned through it is one of the array returned.
/// </summary>
[TestClass]
public sealed class PropertyGetArrayIndexTests
{
    private static readonly string Customer = ModuleWorkspace.ClassModule("Customer", "Option Explicit", "Public Name As String");

    // Ann and Bob, the Long counts 10 to 12, a 2 by 3 grid with 7 at (1, 2), as Property Gets of a Basket and a function of it; the counts in a Variant, by
    // a Property Get and by a function.
    private static readonly string Basket = ModuleWorkspace.ClassModule("Basket",
        "Option Explicit",
        "Private mItems() As Customer",
        "Private mCounts() As Long",
        "Private mGrid() As Long",
        "Private Sub Class_Initialize()",
        "    ReDim mItems(1)",
        "    Set mItems(0) = New Customer",
        "    mItems(0).Name = \"Ann\"",
        "    Set mItems(1) = New Customer",
        "    mItems(1).Name = \"Bob\"",
        "    ReDim mCounts(2)",
        "    mCounts(0) = 10",
        "    mCounts(1) = 11",
        "    mCounts(2) = 12",
        "    ReDim mGrid(1, 2)",
        "    mGrid(1, 2) = 7",
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
        "Public Property Get Variants() As Variant()",
        "    Dim v(1) As Variant",
        "    v(1) = \"vee\"",
        "    Variants = v",
        "End Property",
        "Public Property Get Wrapped() As Variant",
        "    Wrapped = mCounts",
        "End Property",
        "Public Function WrappedF() As Variant",
        "    WrappedF = mCounts",
        "End Function");

    private static string Program(params string[] body)
        => ModuleWorkspace.StandardModule(
            "Program", ["Option Explicit", "Public Sub Main()", "    Dim b As New Basket", .. body.Select(line => "    " + line), "End Sub"]);

    private static async Task AssertInBothEnginesAsync(string expected, params string[] body)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var printed = await ModuleWorkspace.RunAsync([("Customer", Customer), ("Basket", Basket)], Program(body), engine);
            Assert.AreEqual(expected, string.Join(" | ", printed), engine.ToString());
        }
    }

    // MS-VBA, Compile error: Wrong number of arguments or invalid property assignment, each.
    [TestMethod]
    [DataRow("Debug.Print b.Items(0).Name", DisplayName = "a Property Get of an object array")]
    [DataRow("Debug.Print b.Counts(1)", DisplayName = "a Property Get of a Long array")]
    [DataRow("Debug.Print b.Grid(1, 2)", DisplayName = "two arguments, for a two-dimensional array")]
    [DataRow("Debug.Print b.Totals(1)", DisplayName = "a function of the object")]
    [DataRow("Debug.Print b.Variants(1)", DisplayName = "a Property Get of a Variant array")]
    public async Task AnArgument_ToAMemberThatTakesNone_AndReturnsATypedArray_IsWrongNumberOfArguments(string line)
        => StringAssert.Contains(
            Assert.ContainsSingle(await ModuleWorkspace.LoadErrorsAsync([("Customer", Customer), ("Basket", Basket)], Program(line))),
            LanguageCoreErrors.VBCompileError_WrongNumberOfArgumentsOrInvalidPropertyAssignment);

    [TestMethod]
    [DataRow("Debug.Print b.Items()(0).Name", "Ann", DisplayName = "a member of an object element")]
    [DataRow("Debug.Print UBound(b.Items) & b.Items()(1).Name", "1Bob", DisplayName = "UBound of the array, then an element")]
    [DataRow("Debug.Print b.Counts()(2) + 1", "13", DisplayName = "an element of a Long array")]
    [DataRow("Debug.Print b.Grid()(1, 2)", "7", DisplayName = "two indices, of a two-dimensional array")]
    [DataRow("Debug.Print b.Totals()(1)", "11", DisplayName = "a function's array")]
    [DataRow("Dim i As Long: For i = 0 To UBound(b.Counts): Debug.Print b.Counts()(i);: Next", "10  11  12",
        DisplayName = "an index that is a variable")]
    [DataRow("Debug.Print b.Wrapped(1)", "11", DisplayName = "a Variant holding the array, by the first argument list")]
    public Task TheArrayAMemberReturns_IsIndexedByTheNextArgumentList(string line, string printed)
        => AssertInBothEnginesAsync(printed, line);

    [TestMethod]
    public Task AnIndexTheArrayDoesNotHave_IsError9()
        => AssertInBothEnginesAsync("9", "On Error Resume Next", "Dim x As Long", "x = b.Counts()(3)", "Debug.Print Err.Number");

    // MS-VBA prints 10, each: the element assigned is one of the array the member returned, and not of the property's own.
    [TestMethod]
    [DataRow("b.Counts()(0) = 99", "Debug.Print b.Counts()(0)", DisplayName = "a typed array, by the next argument list")]
    [DataRow("b.Wrapped(0) = 99", "Debug.Print b.Wrapped(0)", DisplayName = "a Property Get's Variant holding the array")]
    [DataRow("b.WrappedF(0) = 99", "Debug.Print b.WrappedF(0)", DisplayName = "a function's Variant holding the array")]
    public Task AnElementAssigned_IsOneOfTheArrayReturned_AndNotOfThePropertysOwn(string assignment, string print)
        => AssertInBothEnginesAsync("10", assignment, print);

    // MS-VBA, each: the element is let as any array's is.
    [TestMethod]
    [DataRow("b.Wrapped(5) = 99", "9", DisplayName = "an index the array does not have")]
    [DataRow("b.Wrapped(0) = \"abc\"", "13", DisplayName = "a value its element type does not take")]
    [DataRow("b.Wrapped(0) = \"42\"", "0", DisplayName = "a value let-coerced to it")]
    public Task AnElementAssignedThroughAVariantsArgumentList_IsLetAsAnArraysElementIs(string assignment, string error)
        => AssertInBothEnginesAsync(error, "On Error Resume Next", assignment, "Debug.Print Err.Number");
}
