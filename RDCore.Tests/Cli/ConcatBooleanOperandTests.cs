using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.6.9.4</strong> A <c>Boolean</c> operand of <c>&amp;</c>: the specification's tables leave it out, and MS-VBA compiles and
/// concatenates it all the same, as RD-VBA does - both where a module is loaded and where it runs.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.9.4 & Operator")]
public sealed class ConcatBooleanOperandTests
{
    private static Task<string[]> Run(params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task ABooleanLiteral_ConcatenatesAsItsToken()
        => CollectionAssert.AreEqual(new[] { "xTrue", "Falsex" }, await Run(
            "Debug.Print \"x\" & True",
            "Debug.Print False & \"x\""));

    [TestMethod]
    public async Task ABooleanVariable_ConcatenatesAsItsToken()
        => CollectionAssert.AreEqual(new[] { "done: True" }, await Run(
            "Dim b As Boolean",
            "b = True",
            "Debug.Print \"done: \" & b"));

    [TestMethod]
    public async Task ABooleanAndNull_IsTheBooleansToken()
        => CollectionAssert.AreEqual(new[] { "True" }, await Run(
            "Debug.Print True & Null"));
}
