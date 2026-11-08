using RDCore.SDK.Model.Symbols.Completion;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The logical line a position is in: its physical lines joined where a line continuation ends one, cut at the position.
/// </summary>
[TestClass]
public sealed class LogicalLineTests
{
    [TestMethod]
    public void ALineThatNoContinuationEnds_IsALogicalLineOfItsOwn()
        => Assert.AreEqual(new LogicalLine("    Send ", "\"a\"", 1, 1), LogicalLine.At(["Sub Main()\r", "    Send \"a\"\r", "End Sub"], 1, 9));

    [TestMethod]
    public void ThePhysicalLinesBefore_AreJoinedWhereAContinuationEndsThem_EachContinuationASpace()
        => Assert.AreEqual(new LogicalLine("Send \"a\",   1,   ", "", 0, 2), LogicalLine.At(["Send \"a\", _\r", "  1, _\r", "  "], 2, 2));

    [TestMethod]
    public void ThePhysicalLinesAfter_AreJoinedWhenThePositionsLineIsContinued()
        => Assert.AreEqual(new LogicalLine("Send ", "\"a\",   Copies:=2", 0, 1), LogicalLine.At(["Send \"a\", _\r", "  Copies:=2\r", "x = 1"], 0, 5));

    [TestMethod]
    [DataRow("x = a_", DisplayName = "an underscore that ends a name")]
    [DataRow("x = \"_", DisplayName = "no space before it")]
    public void AnUnderscoreWithNoSpaceBeforeIt_IsNoContinuation(string line)
        => Assert.AreEqual(1, LogicalLine.At([line, "y = 2"], 1, 0).FirstLine);

    // MS-VBAL §3.2.2 takes any WSC before the underscore; the lexer takes a space or a tab, and a line the parser reads as two is two here too.
    [TestMethod]
    [DataRow("Send \"a\",　_", DisplayName = "an ideographic space")]
    [DataRow("Send \"a\", _", DisplayName = "a no-break space")]
    public void AnUnderscoreAfterAWhitespaceTheLexerDoesNotTake_IsNoContinuation(string line)
        => Assert.AreEqual(1, LogicalLine.At([line, "  2"], 1, 0).FirstLine);

    // a chain of continuations as long as a module is one logical line.
    [TestMethod]
    public void ALongChainOfContinuations_IsOneLogicalLine()
    {
        var lines = Enumerable.Repeat("  1, _\r", 10_000).Append("  2").ToArray();

        var logical = LogicalLine.At(lines, 5_000, 2);

        Assert.AreEqual((0, 10_000), (logical.FirstLine, logical.LastLine));
        Assert.AreEqual((5_000 * "  1, ".Length) + 2, logical.Before.Length);
        Assert.AreEqual("1, ".Length + (4_999 * "  1, ".Length) + "  2".Length, logical.After.Length);
        StringAssert.EndsWith(logical.After, "  1,   2");
    }
}
