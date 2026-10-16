using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The <c>As</c> clause of a <c>ReDim</c> of a <c>Variant</c> (<strong>MS-VBAL §5.4.3.3</strong>): a <c>&lt;redim-untyped-dcl&gt;</c> has the static
/// semantics of an <c>&lt;untyped-variable-dcl&gt;</c>, so the clause is the type of the elements of the array the <c>Variant</c> is given.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim Statement")]
public sealed class ReDimAsClauseTests
{
    private static Task<string[]> Run(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    [DataRow("Long", "Long() [0]")]
    [DataRow("Double", "Double() [0]")]
    [DataRow("String", "String() []")]
    [DataRow("Boolean", "Boolean() [False]")]
    public async Task AVariantReDimmedAsAType_HoldsAnArrayOfThatType_WhoseElementsAreItsDefault(string typeName, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await Run(
            "Dim v As Variant",
            $"ReDim v(1) As {typeName}",
            "Debug.Print TypeName(v) & \" [\" & v(1) & \"]\""));

    [TestMethod]
    public async Task AVariantReDimmedWithoutAnAsClause_HoldsAVariantArray()
        => CollectionAssert.AreEqual(new[] { "Variant()" }, await Run(
            "Dim v As Variant",
            "ReDim v(1)",
            "Debug.Print TypeName(v)"));

    [TestMethod]
    public async Task AVariantHoldingAnArray_ReDimmedAsAType_HoldsAnArrayOfThatType()
        => CollectionAssert.AreEqual(new[] { "Variant()", "Long()" }, await Run(
            "Dim v As Variant",
            "v = Array(1, 2)",
            "Debug.Print TypeName(v)",
            "ReDim v(3) As Long",
            "Debug.Print TypeName(v)"));
}
