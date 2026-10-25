using RDCore.CLI.App.Console;

namespace RDCore.Tests.Cli;

/// <summary>
/// Text measured in the cells of a terminal, which is what a box drawn around it lines up by.
/// </summary>
[TestClass]
public sealed class ConsoleTextTests
{
    [TestMethod]
    [DataRow("Duck", 4)]
    [DataRow("", 0)]
    [DataRow("⚠️", 2)]
    [DataRow("⚠", 1)]
    [DataRow("ℹ️ ", 3)]
    [DataRow("⛔", 2)]
    [DataRow("🦆", 2)]
    [DataRow("canard é", 8)]
    [DataRow("é", 1)]
    [DataRow("日本", 4)]
    public void AWidth_IsInCells(string text, int expected) => Assert.AreEqual(expected, ConsoleText.WidthOf(text));

    [TestMethod]
    public void Wrapping_BreaksBetweenWords_AndInsideAWordTooLongForALine()
    {
        CollectionAssert.AreEqual(new[] { "one two", "three" }, ConsoleText.Wrap("one two three", 8).ToArray());
        CollectionAssert.AreEqual(new[] { "abcde", "fgh" }, ConsoleText.Wrap("abcdefgh", 5).ToArray());
        CollectionAssert.AreEqual(new[] { "one", "", "two" }, ConsoleText.Wrap("one\r\n\ntwo", 10).ToArray(), "the lines of the text are kept, the empty ones too");
    }

    [TestMethod]
    public void Truncating_EndsWithAnEllipsis_OnlyWhenSomethingHadToGo()
    {
        Assert.AreEqual("Project1", ConsoleText.Truncate("Project1", 8));
        Assert.AreEqual("Proj…", ConsoleText.Truncate("Project1", 5));
    }
}
