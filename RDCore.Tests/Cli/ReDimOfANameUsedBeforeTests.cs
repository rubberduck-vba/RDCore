using RDCore.SDK;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.3.3</strong> a <c>ReDim</c> of a name that matches no declaration declares the array it re-dimensions. A name the procedure uses before
/// its first <c>ReDim</c>, with no declaration, was declared there by that use (§5.6.10), whether the earlier line writes it, reads it, or never runs: the
/// <c>ReDim</c> re-dimensions that variable, a <c>Variant</c>, as vb6.exe runs it. MS-VBA refuses it at compile time, "Invalid ReDim" (recorded in Excel: grid
/// <c>oracle\findings-labels</c> R01-R10), which the author lets slip with <c>RDC11022</c> (HexIDE's findings desk #9). A name used only after it is the array
/// the <c>ReDim</c> declared.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim Statement")]
public sealed class ReDimOfANameUsedBeforeTests
{
    // no Option Explicit: an undeclared name is a variable declared where it is first used.
    private static string Program(params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n" + string.Join("\r\n", body) + "\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("y = 5", DisplayName = "Assigned before")]
    [DataRow("Debug.Print TypeName(y)", DisplayName = "Read before")]
    [DataRow("y = Array(1, 2)", DisplayName = "Given an array before")]
    [DataRow("If False Then y = 5", DisplayName = "Assigned before on a line that never runs")]
    public async Task ReDim_OfANameUsedBefore_ReDimensionsTheImplicitVariant(string before)
    {
        var output = await ModuleWorkspace.RunAsync([], Program(before, "ReDim y(2)", "y(1) = 5", "Debug.Print TypeName(y) & \" \" & UBound(y) & \" \" & y(1)"));

        Assert.AreEqual("Variant() 2 5", output[^1]);
    }

    [TestMethod]
    public async Task ReDim_OfANameUsedBefore_Compiles()
        => Assert.IsEmpty(await ModuleWorkspace.LoadErrorsAsync([], Program("y = 5", "ReDim y(2)")));

    [TestMethod]
    public async Task ReDim_OfANameUsedOnlyAfter_DeclaresTheArray()
        => CollectionAssert.AreEqual(new[] { "2", "5" }, await ModuleWorkspace.RunAsync([], Program("ReDim y(2)", "Debug.Print UBound(y)", "y(1) = 5", "Debug.Print y(1)")));

    [TestMethod]
    public async Task AReDim_ThenAnother_ReDimensionsTheSameArray()
        => CollectionAssert.AreEqual(new[] { "3" }, await ModuleWorkspace.RunAsync([], Program("ReDim y(2)", "ReDim y(3)", "Debug.Print UBound(y)")));

    [TestMethod]
    public async Task AReDimInAnotherProcedure_DeclaresItsOwnArray()
        => CollectionAssert.AreEqual(new[] { "5" }, await ModuleWorkspace.RunAsync([],
            "Attribute VB_Name = \"Program\"\r\nPrivate Sub Other()\r\nReDim y(2)\r\nEnd Sub\r\nPublic Sub Main()\r\ny = 5\r\nDebug.Print y\r\nEnd Sub\r\n"));

    // under Option Explicit no use declares anything: the ReDim declares the array for the whole procedure, and the earlier use is of that array.
    [TestMethod]
    public async Task UnderOptionExplicit_ReDim_OfANameUsedBefore_IsInvalidReDim()
        => StringAssert.Contains(Assert.ContainsSingle(await ModuleWorkspace.LoadErrorsAsync([],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\ny = 5\r\nReDim y(2)\r\nEnd Sub\r\n")), Exceptions.VBCompileError_InvalidReDim);
}
