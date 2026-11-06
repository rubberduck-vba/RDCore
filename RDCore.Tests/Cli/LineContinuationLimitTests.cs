using RDCore.Parsing;

namespace RDCore.Tests.Cli;

/// <summary>
/// A logical line joins at most 25 physical lines: 24 line-continuations in a row, and the 25th is a syntax error, "Too many line continuations", as MS-VBA has
/// it (MS Learn; recorded in Excel: oracle\size-limits B01-B03).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.2.2 Logical Line Grammar")]
public sealed class LineContinuationLimitTests
{
    // Main assigns x a sum continued over the given number of line-continuations, and prints it.
    private static string Program(int continuations, string after = "")
        => "Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n    Dim x As Long\r\n    x = 1 _\r\n"
            + string.Concat(Enumerable.Repeat("        + 1 _\r\n", continuations - 1)) + "        + 1\r\n" + after + "    Debug.Print x\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task TwentyFourLineContinuations_AreOneStatement()
        => CollectionAssert.AreEqual(new[] { "25" }, await ModuleWorkspace.RunAsync([], Program(24)));

    [TestMethod]
    public void TheTwentyFifthLineContinuation_IsASyntaxError()
    {
        var result = new ModuleParser().Parse(new Uri("file:///C:/Program.bas"), Program(25));

        var error = Assert.ContainsSingle(result.SyntaxErrors);
        Assert.AreEqual((int)RDCore.SDK.Model.Errors.VBCompileErrorId.SyntaxError, error.ErrorId);
        StringAssert.Contains(error.Verbose, "Too many line continuations");
    }

    [TestMethod]
    public async Task TheLimitIsOfOneLogicalLine()
        // two statements of 20 line-continuations each: 40 in all, but no more than 20 in a row.
        => CollectionAssert.AreEqual(new[] { "21", "41" }, await ModuleWorkspace.RunAsync([], Program(20,
            "    Debug.Print x\r\n    x = x _\r\n" + string.Concat(Enumerable.Repeat("        + 1 _\r\n", 19)) + "        + 1\r\n")));

    [TestMethod]
    public void TheError_IsAtTheTwentyFifthUnderscore_Once()
    {
        var program = Program(40);
        var result = new ModuleParser().Parse(new Uri("file:///C:/Program.bas"), program);

        var error = Assert.ContainsSingle(result.SyntaxErrors);
        // line 3 is "    x = 1 _", the first; the 25th is 24 lines further.
        Assert.AreEqual(3 + 24, error.Location.Range.Start.Line);
        Assert.AreEqual("        + 1 _".Length - 1, error.Location.Range.Start.Character);
    }
}
