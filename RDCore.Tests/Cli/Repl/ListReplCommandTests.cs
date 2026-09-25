using RDCore.CLI.App.Repl.Commands;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// <c>LIST</c>'s argument is a BASIC line range, in every shape BASIC allows.
/// </summary>
[TestClass]
public sealed class ListReplCommandTests
{
    [TestMethod]
    public void NoArgument_IsTheWholeBuffer()
    {
        Assert.IsTrue(ListReplCommand.TryParseRange("", out var from, out var to));
        Assert.IsNull(from);
        Assert.IsNull(to);
    }

    [TestMethod]
    public void ABareNumber_IsThatOneLine()
    {
        Assert.IsTrue(ListReplCommand.TryParseRange("100", out var from, out var to));
        Assert.AreEqual(100, from);
        Assert.AreEqual(100, to);
    }

    [TestMethod]
    public void AClosedRange_IsBothEnds()
    {
        Assert.IsTrue(ListReplCommand.TryParseRange("100-200", out var from, out var to));
        Assert.AreEqual(100, from);
        Assert.AreEqual(200, to);
    }

    [TestMethod]
    public void AnOpenEndedRange_LeavesTheMissingEndUnbounded()
    {
        Assert.IsTrue(ListReplCommand.TryParseRange("100-", out var from, out var openTo));
        Assert.AreEqual(100, from);
        Assert.IsNull(openTo);

        Assert.IsTrue(ListReplCommand.TryParseRange("-200", out var openFrom, out var to));
        Assert.IsNull(openFrom);
        Assert.AreEqual(200, to);
    }

    [TestMethod]
    [DataRow("abc")]
    [DataRow("1-abc")]
    [DataRow("abc-1")]
    [DataRow("-")]
    public void SomethingThatIsNotALineRange_IsRejected(string arguments)
        => Assert.IsFalse(ListReplCommand.TryParseRange(arguments, out _, out _));
}
