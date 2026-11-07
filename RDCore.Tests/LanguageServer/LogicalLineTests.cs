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
}
