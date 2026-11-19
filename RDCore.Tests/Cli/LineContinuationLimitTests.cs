using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Cli;

/// <summary>
/// A logical line joins at most 25 physical lines: 24 line-continuations in a row, and the 25th is a syntax error, "Too many line continuations", as MS-VBA has
/// it (MS Learn; observed in Excel).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 3.2.2 Logical Line Grammar")]
public sealed class LineContinuationLimitTests
{
    // Main assigns x a sum continued over the given number of line-continuations, each written with the given blanks before its underscore, and prints it.
    private static string Program(int continuations, string after = "", string blanks = " ")
        => $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n    Dim x As Long\r\n    x = 1{blanks}_\r\n"
            + string.Concat(Enumerable.Repeat($"        + 1{blanks}_\r\n", continuations - 1)) + "        + 1\r\n" + after + "    Debug.Print x\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow(" ", DisplayName = "a space")]
    [DataRow("    ", DisplayName = "spaces")]
    [DataRow("\t\t", DisplayName = "tabs")]
    public async Task TwentyFourLineContinuations_AreOneStatement(string blanks)
        => CollectionAssert.AreEqual(new[] { "25" }, await ModuleWorkspace.RunAsync([], Program(24, blanks: blanks)));

    [TestMethod]
    [DataRow(" ", DisplayName = "a space")]
    [DataRow("    ", DisplayName = "spaces")]
    [DataRow("\t\t", DisplayName = "tabs")]
    public void TheTwentyFifthLineContinuation_IsASyntaxError(string blanks)
    {
        var result = new ModuleParser().Parse(new Uri("file:///C:/Program.bas"), Program(25, blanks: blanks));

        var error = Assert.ContainsSingle(result.SyntaxErrors);
        Assert.AreEqual((int)RDCore.Runtime.LanguageCore.Errors.VBCompileErrorId.SyntaxError, error.ErrorId);
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
