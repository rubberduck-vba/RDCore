namespace RDCore.Tests.Cli;

/// <summary>
/// An object that is indexed is a call of its default member (<strong>MS-VBAL §5.6.13</strong>): when the object has none to bind the call to, it is run-time
/// error 438, <em>Object doesn't support this property or method</em>.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.13 Index Expressions")]
public sealed class DefaultMemberBindingTests
{
    private static readonly (string, string) Plain = ("Plain", ModuleWorkspace.ClassModule("Plain", "Public Size As Long"));

    private static Task<string[]> RunAsync(params string[] body)
        => ModuleWorkspace.RunAsync([Plain],
            $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nOn Error GoTo Failed\r\n{string.Join("\r\n", body)}\r\nExit Sub\r\nFailed:\r\nDebug.Print Err.Number\r\nEnd Sub\r\n");

    [TestMethod]
    [DataRow("Dim o As Object", "Set o = New Plain", "Debug.Print o(1)")]
    [DataRow("Dim v As Variant", "Set v = New Plain", "Debug.Print v(1)")]
    [DataRow("Dim o As Object", "Set o = New Plain", "Debug.Print o!Title")]
    public async Task AnObjectWithNoDefaultMember_IsError438_WhenItIsIndexed(string declaration, string assignment, string use)
        => CollectionAssert.AreEqual(new[] { "438" }, await RunAsync(declaration, assignment, use));

    [TestMethod]
    [DataRow("Foo")]
    [DataRow("Name")]
    [DataRow("Date")]
    [DataRow("Open")]
    public async Task ADictionaryAccess_IsACallOfTheDefaultMember_WithTheNameOfTheMemberAsItsArgument(string member)
    {
        var dict = ("Dict", ModuleWorkspace.ClassModule("Dict",
            "Public Function Item(ByVal key As String) As String", "Attribute Item.VB_UserMemId = 0", "Item = \"<\" & key & \">\"", "End Function"));

        CollectionAssert.AreEqual(new[] { $"<{member}>" }, await ModuleWorkspace.RunAsync(
            [dict], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\nDim d As New Dict\r\nDebug.Print d!{member}\r\nEnd Sub\r\n"));
    }

    [TestMethod]
    public async Task ADictionaryAccess_OfAKeywordName_OnAnObjectWithNoDefaultMember_IsError438()
        => CollectionAssert.AreEqual(new[] { "438" }, await RunAsync("Dim o As Object", "Set o = New Plain", "Debug.Print o!Name"));

    [TestMethod]
    public async Task ANothing_IsError91_WhenItIsIndexed()
        => CollectionAssert.AreEqual(new[] { "91" }, await RunAsync("Dim o As Object", "Debug.Print o(1)"));
}
