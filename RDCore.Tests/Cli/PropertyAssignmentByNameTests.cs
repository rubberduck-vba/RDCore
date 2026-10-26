namespace RDCore.Tests.Cli;

/// <summary>
/// A property assigned by its name alone (<strong>MS-VBAL §5.4.3.8</strong>) is assigned by invoking its <c>Property Let</c> with the value: a property of a standard
/// module on nothing, and one of a class, from the class's own code, on the object whose code it is.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.8 Let Statement")]
public sealed class PropertyAssignmentByNameTests
{
    private static readonly (string, string) Holder = ("Holder", ModuleWorkspace.ClassModule("Holder",
        "Option Explicit",
        "Private mSize As Long",
        "Public Property Get Size() As Long", "    Size = mSize", "End Property",
        "Public Property Let Size(ByVal v As Long)", "    mSize = v + 1", "End Property",
        "Public Sub Grow()", "    Size = 41", "End Sub",
        "Public Function Current() As Long", "    Current = mSize", "End Function"));

    private static async Task AssertPrintsAsync(string expected, params string[] body)
    {
        var program = string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Private mValue As Long",
            "Public Property Get Value() As Long", "    Value = mValue", "End Property",
            "Public Property Let Value(ByVal v As Long)", "    mValue = v * 2", "End Property",
            "Public Property Get ReadOnly() As Long", "    ReadOnly = 1", "End Property",
            "Public Sub Main()",
            "On Error GoTo Failed",
            string.Join("\r\n", body),
            "Exit Sub",
            "Failed:",
            "Debug.Print \"Error\"; Err.Number",
            "End Sub") + "\r\n";

        var printed = string.Join(" | ", await ModuleWorkspace.RunAsync([Holder], program));
        Assert.AreEqual(expected, printed, $"printed: {printed}");
    }

    [TestMethod]
    public Task APropertyOfAStandardModule_IsAssignedByItsPropertyLet()
        => AssertPrintsAsync("42", "Value = 21", "Debug.Print CStr(Value)");

    [TestMethod]
    public Task APropertyOfAClass_AssignedByNameFromItsOwnCode_IsAssignedOnTheObject()
        => AssertPrintsAsync("42", "Dim h As New Holder", "h.Grow", "Debug.Print CStr(h.Current)");

    // MS-VBA (Excel-verified, oracle\s28 W1): a property of a module with no Property Let, of a type that is neither a Variant nor an object, is no target
    // at all; one of a Variant is called, and what it returns that is no object is error 424 (FunctionTargetTests).
    [TestMethod]
    public async Task APropertyWithNoPropertyLet_ThatReturnsNoObject_IsCantAssignToReadOnlyProperty()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], "Attribute VB_Name = \"Program\"\r\nPublic Property Get ReadOnly() As Long\r\nReadOnly = 1\r\nEnd Property\r\nPublic Sub Main()\r\nReadOnly = 2\r\nEnd Sub\r\n");

        Assert.HasCount(1, errors, string.Join(" | ", errors));
        StringAssert.StartsWith(errors[0], "Can't assign to read-only property");
    }

    private static readonly (string, string) Wrapper = ("Wrapper", ModuleWorkspace.ClassModule("Wrapper",
        "Option Explicit",
        "Private mValue As Variant",
        "Public Property Get Value() As Variant", "Attribute Value.VB_UserMemId = 0", "    Value = mValue", "End Property",
        "Public Property Let Value(ByVal v As Variant)", "    mValue = v", "End Property"));

    // MS-VBAL §5.4.3.9: a Set of a property that has no Property Set is invalid, written by its bare name in another procedure of its module as through an
    // object (MS-VBA, Excel-measured, oracle\s30 C32, C35: "Invalid use of property").
    [TestMethod]
    public Task ASetOfAPropertyWithNoPropertySet_ByItsName_IsAnInvalidUseOfProperty()
        => AssertSetIsInvalidAsync("Set Items = Nothing");

    // MS-VBA, Excel-measured, oracle\s44 V01: qualified by its module, as by its bare name.
    [TestMethod]
    public Task ASetOfAPropertyWithNoPropertySet_QualifiedByItsModule_IsAnInvalidUseOfProperty()
        => AssertSetIsInvalidAsync("Set Program.Items = Nothing");

    [TestMethod]
    public async Task ASetOfAnotherModulesProperty_QualifiedByItsModule_IsCheckedAgainstThatModulesAccessors()
    {
        var program = string.Join("\r\n", "Attribute VB_Name = \"Program\"", "Public Sub Main()", "Set Store.Items = Nothing", "Set Store.Fixed = Nothing", "End Sub") + "\r\n";
        var store = ModuleWorkspace.StandardModule("Store",
            "Public Property Get Items() As Collection", "End Property", "Public Property Set Items(ByVal c As Collection)", "End Property",
            "Public Property Get Fixed() As Collection", "End Property");

        var errors = await ModuleWorkspace.LoadErrorsAsync([], [("Store", store)], program);

        StringAssert.StartsWith(Assert.ContainsSingle(errors), "Invalid use of property");
        StringAssert.Contains(errors[0], "Fixed");
    }

    private static async Task AssertSetIsInvalidAsync(string statement)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Public Property Get Items() As Collection", "End Property",
            "Public Sub Main()", statement, "End Sub") + "\r\n");

        StringAssert.StartsWith(Assert.ContainsSingle(errors), "Invalid use of property");
    }

    // MS-VBAL §5.4.3.8: a Let of a property that has no Property Let is invalid only when the Property Get's type is not a class, Object or Variant: what an
    // object property returns has its default member assigned (MS-VBA, Excel-measured, oracle\s30 C31, C36).
    [TestMethod]
    [DataRow("Box = 42")]
    [DataRow("Program.Box = 42")]
    public async Task ALetOfAnObjectPropertyWithNoPropertyLet_AssignsItsDefaultMember(string statement)
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([Wrapper], string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Private mBox As New Wrapper",
            "Public Property Get Box() As Wrapper", "    Set Box = mBox", "End Property",
            "Public Sub Main()", statement, "End Sub") + "\r\n");

        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }
}
