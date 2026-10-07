namespace RDCore.Tests.Cli;

/// <summary>
/// An object is let-coerced through its default member (<strong>MS-VBAL §5.5.1.2.13</strong>) wherever a statement needs a value of it: a condition, a
/// selector, a printed item, the arguments of <c>Mid</c> and the error number of <c>Error</c> coerce it the way an assignment does.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.2.13 Let-coercion to and from a class")]
public sealed class DefaultMemberCoercionSiteTests
{
    private static readonly (string, string) Box = ("Box", ModuleWorkspace.ClassModule("Box",
        "Private mValue As Variant",
        "Public Property Get Value() As Variant", "Attribute Value.VB_UserMemId = 0", "Value = mValue", "End Property",
        "Public Property Let Value(ByVal v As Variant)", "mValue = v", "End Property"));

    // what the program printed, a line each.
    private static async Task AssertPrintsAsync(string expected, params string[] body)
    {
        var printed = string.Join(" | ", await ModuleWorkspace.RunAsync([Box],
            $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error GoTo Failed\r\nDim b As New Box\r\n{string.Join("\r\n", body)}\r\nExit Sub\r\nFailed:\r\nDebug.Print \"Error\"; Err.Number\r\nEnd Sub\r\n"));
        Assert.AreEqual(expected, printed, $"printed: {printed}");
    }

    [TestMethod]
    public Task ALetAssignmentOfAnObject_IsTheValueOfItsDefaultMember()
        => AssertPrintsAsync("42", "b.Value = 42", "Dim n As Long", "n = b", "Debug.Print CStr(n)");

}
