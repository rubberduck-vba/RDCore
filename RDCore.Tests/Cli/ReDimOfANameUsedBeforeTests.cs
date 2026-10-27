using RDCore.SDK;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.3.3</strong> a <c>ReDim</c> of a name that matches no declaration declares the array it re-dimensions. MS-VBA decides by the text of the
/// procedure: a name the procedure uses before the <c>ReDim</c>, with no declaration, is a variable it declared implicitly there, and a <c>ReDim</c> of it is
/// "Invalid ReDim" (<c>VBC09306</c>) at compile time - whether the earlier line writes it, reads it, or never runs. A name used only after it is the array the
/// <c>ReDim</c> declared. Each case is what MS-VBA does, recorded in Excel (grid <c>oracle\findings-labels</c> R01-R10; HexIDE's findings desk #9).
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
    public async Task ReDim_OfANameUsedBefore_IsInvalidReDim(string before)
        => StringAssert.Contains(Assert.ContainsSingle(await ModuleWorkspace.LoadErrorsAsync([], Program(before, "ReDim y(2)"))),
            Exceptions.VBCompileError_InvalidReDim);

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
}
