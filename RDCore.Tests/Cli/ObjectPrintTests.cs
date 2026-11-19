namespace RDCore.Tests.Cli;

/// <summary>
/// <c>obj.Print</c> is the Print method of the object: a Visual Basic 6.0 form or PictureBox, an Access form or report. Any other object has no such method.
/// </summary>
[TestClass]
public sealed class ObjectPrintTests
{
    private static Task<string[]> RunAsync(params string[] body)
        => ModuleWorkspace.RunAsync([("Class1", ModuleWorkspace.ClassModule("Class1", "Public Sub Work()", "End Sub"))],
            ModuleWorkspace.StandardModule("Program", ["Public Sub Main()", "On Error Resume Next", .. body, "End Sub"]));

    [TestMethod]
    [DataRow("Set o = New Collection", DisplayName = "a library's object")]
    [DataRow("Set o = New Class1", DisplayName = "an object of the workspace's class")]
    public async Task AnObjectThatHasNoPrintMethod_Raises438(string assignment)
    {
        var output = await RunAsync("Dim o As Object", assignment, "o.Print \"x\"; 1, Spc(2); Tab(3); Tab", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "438" }, output);
    }

    [TestMethod]
    public async Task Nothing_Raises91()
    {
        var output = await RunAsync("Dim o As Object", "o.Print \"x\"", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "91" }, output);
    }

    [TestMethod]
    public async Task AValueThatIsNoObject_Raises424()
    {
        var output = await RunAsync("Dim v As Variant", "v = 1", "v.Print \"x\"", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "424" }, output);
    }

    [TestMethod]
    public async Task AnItemThatRaises_RaisesItsError_BeforeTheObjectIsCalled()
    {
        var output = await RunAsync("Dim o As Object", "Set o = New Collection", "o.Print 1 / 0", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "11" }, output);
    }
}
