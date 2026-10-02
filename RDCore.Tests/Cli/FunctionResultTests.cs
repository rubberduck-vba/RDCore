namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.3.1</strong> the result of a function or Property Get: what it is Set to, what it is when nothing sets it, and what is done with it.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.3.1 Procedure Declarations")]
public sealed class FunctionResultTests
{
    private static readonly (string, string) Holder = ("Holder", ModuleWorkspace.ClassModule("Holder",
        "Private mC As New Collection",
        "Public Property Get Items() As Collection", "Set Items = mC", "End Property"));

    private static string Program(string body, string procedures = "")
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{body}\r\nEnd Sub\r\n{procedures}\r\n";

    private const string VariantFunction = "Public Function F() As Variant\r\nDim c As New Collection\r\nc.Add 1\r\nSet F = c\r\nEnd Function";

    [TestMethod]
    public async Task AVariantFunction_SetToAnObject_ReturnsItAlive()
        => CollectionAssert.AreEqual(new[] { "1" }, await ModuleWorkspace.RunAsync([], Program("Dim x As Variant\r\nSet x = F()\r\nDebug.Print x.Count", VariantFunction)));

    [TestMethod]
    public async Task AVariantFunction_SetToAnObject_IsCalledThroughItsResult()
        => CollectionAssert.AreEqual(new[] { "1" }, await ModuleWorkspace.RunAsync([], Program("Debug.Print F().Count", VariantFunction)));

    [TestMethod]
    [DataRow("Object")]
    [DataRow("Collection")]
    public async Task AnObjectFunction_ThatNeverSetsItsResult_ReturnsNothing(string returns)
        => CollectionAssert.AreEqual(new[] { "True" },
            await ModuleWorkspace.RunAsync([], Program("Debug.Print H() Is Nothing", $"Public Function H() As {returns}\r\nEnd Function")));

    [TestMethod]
    public async Task AFunction_WithNoParameters_IndexesWhatItReturns()
        => CollectionAssert.AreEqual(new[] { "7" },
            await ModuleWorkspace.RunAsync([], Program("Debug.Print Make(1)", "Public Function Make() As Collection\r\nSet Make = New Collection\r\nMake.Add 7\r\nEnd Function")));

    [TestMethod]
    public async Task APropertyGet_WithNoParameters_IndexesWhatItReturns()
        => CollectionAssert.AreEqual(new[] { "4", "4", "1" },
            await ModuleWorkspace.RunAsync([Holder], Program("Dim h As New Holder\r\nh.Items.Add 4\r\nDebug.Print h.Items(1)\r\nDebug.Print h.Items.Item(1)\r\nDebug.Print h.Items.Count")));

    [TestMethod]
    [DataRow("Dim o As Object", "True")]
    [DataRow("Dim o As Collection", "True")]
    [DataRow("Dim o As New Collection", "False")]
    public async Task IsNothing_ComparesTheReferenceItself(string declaration, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await ModuleWorkspace.RunAsync([], Program($"{declaration}\r\nDebug.Print o Is Nothing")));

    [TestMethod]
    public async Task Is_ComparesTwoReferencesToTheSameObject()
        => CollectionAssert.AreEqual(new[] { "True", "False" },
            await ModuleWorkspace.RunAsync([], Program("Dim a As New Collection\r\nDim b As New Collection\r\nDim c As Collection\r\nSet c = a\r\nDebug.Print c Is a\r\nDebug.Print c Is b")));
}
