using RDCore.SDK.ConsoleIO;

namespace RDCore.Tests.ConsoleIO;

[TestClass]
public sealed class SourcePathAnonymizerTests
{
    // a Debug/PDB stack trace as it arrives from a Windows build machine and from the Linux CI runner.
    private const string WindowsTrace =
        "System.NullReferenceException: Object reference not set to an instance of an object.\r\n" +
        "   at RDCore.Parsing.AST.DeclarationsParseTreeListener.ExitAsTypeClause(AsTypeClauseContext context) in C:\\Users\\somebody\\src\\RDCore\\RDCore.Parsing\\AST\\DeclarationsParseTreeListener.cs:line 241\r\n" +
        "   at Antlr4.Runtime.Tree.ParseTreeWalker.ExitRule(IParseTreeListener listener, IRuleNode r)\r\n" +
        "   at RDCore.Parsing.ModuleParser.ParseOnce[TListener](String content) in C:\\Users\\somebody\\src\\RDCore\\RDCore.Parsing\\ModuleParser.cs:line 124";

    private const string LinuxCiTrace =
        "System.InvalidCastException: cast failed\r\n" +
        "   at RDCore.Parsing.AST.DeclarationNodeBuilder.BuildImplementsDirective(ImplementsStmtContext context) in /home/runner/work/RDCore/RDCore/RDCore.Parsing/AST/DeclarationNodeBuilder.cs:line 28";

    [TestMethod]
    public void NoPath_ReturnsInputUnchanged()
    {
        const string antlrMessage = "no viable alternative at input 'Private Function'";
        Assert.AreEqual(antlrMessage, SourcePathAnonymizer.Scrub(antlrMessage));
        Assert.AreEqual(string.Empty, SourcePathAnonymizer.Scrub(string.Empty));
    }

    [TestMethod]
    public void RepoRelative_StripsEverythingBeforeTheProjectFolder()
    {
        var scrubbed = SourcePathAnonymizer.Scrub(WindowsTrace, SourcePathScrubMode.RepoRelative);

        StringAssert.Contains(scrubbed, "in RDCore.Parsing/AST/DeclarationsParseTreeListener.cs:line 241");
        StringAssert.Contains(scrubbed, "in RDCore.Parsing/ModuleParser.cs:line 124");
        Assert.IsFalse(scrubbed.Contains("somebody"), "the build-machine user name must be gone");
        Assert.IsFalse(scrubbed.Contains("C:\\"), "the drive-letter prefix must be gone");
        // non-frame lines are untouched
        StringAssert.Contains(scrubbed, "at Antlr4.Runtime.Tree.ParseTreeWalker.ExitRule");
    }

    [TestMethod]
    public void RepoRelative_AnchorsOnProjectFolderForAForeignBuildMachine()
    {
        var scrubbed = SourcePathAnonymizer.Scrub(LinuxCiTrace, SourcePathScrubMode.RepoRelative);

        StringAssert.Contains(scrubbed, "in RDCore.Parsing/AST/DeclarationNodeBuilder.cs:line 28");
        Assert.IsFalse(scrubbed.Contains("/home/runner"), "the CI runner path must be gone");
    }

    [TestMethod]
    public void FileName_KeepsOnlyTheFileName()
    {
        var scrubbed = SourcePathAnonymizer.Scrub(WindowsTrace, SourcePathScrubMode.FileName);

        StringAssert.Contains(scrubbed, "in DeclarationsParseTreeListener.cs:line 241");
        StringAssert.Contains(scrubbed, "in ModuleParser.cs:line 124");
        // no directory component survives in the path portion (the method-name portion legitimately
        // still reads "RDCore.Parsing.AST.…").
        Assert.IsFalse(scrubbed.Contains("in RDCore.Parsing"), "no directory component should survive the path");
        Assert.IsFalse(scrubbed.Contains("somebody"));
    }

    [TestMethod]
    public void Redacted_ReplacesThePathButKeepsTheLine()
    {
        var scrubbed = SourcePathAnonymizer.Scrub(WindowsTrace, SourcePathScrubMode.Redacted);

        StringAssert.Contains(scrubbed, "in <source>:line 241");
        Assert.IsFalse(scrubbed.Contains(".cs:line"), "the file name must be gone too");
    }

    [TestMethod]
    public void DefaultMode_IsRepoRelative()
        => Assert.AreEqual(
            SourcePathAnonymizer.Scrub(WindowsTrace, SourcePathScrubMode.RepoRelative),
            SourcePathAnonymizer.Scrub(WindowsTrace));
}
