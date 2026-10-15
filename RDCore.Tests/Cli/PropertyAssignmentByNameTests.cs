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

    [TestMethod]
    public Task APropertyWithNoPropertyLet_ThatReturnsNoObject_IsError451()
        => AssertPrintsAsync("Error 451", "ReadOnly = 2");
}
