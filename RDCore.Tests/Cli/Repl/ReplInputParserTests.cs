using RDCore.CLI.App.Repl;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The shell's input rules, which are BASIC's: a leading number is program text, a bare number
/// deletes, a known verb is a command, everything else is a statement to run now.
/// </summary>
[TestClass]
public sealed class ReplInputParserTests
{
    private static readonly HashSet<string> Commands = new(["LIST", "RUN", "EXIT"], StringComparer.OrdinalIgnoreCase);

    private static ReplInput Parse(string? line) => ReplInputParser.Parse(line, Commands.Contains);

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void NothingTyped_IsEmpty(string? line)
        => Assert.AreEqual(ReplInputKind.Empty, Parse(line).Kind);

    [TestMethod]
    public void ALeadingNumber_StoresTheRestAsThatLine()
    {
        var input = Parse("100 X = 1 + 2");

        Assert.AreEqual(ReplInputKind.StoreLine, input.Kind);
        Assert.AreEqual(100, input.LineNumber);
        Assert.AreEqual("X = 1 + 2", input.Text);
    }

    [TestMethod]
    public void ALineNumberWithNothingAfterIt_DeletesThatLine()
    {
        var input = Parse("100");

        Assert.AreEqual(ReplInputKind.DeleteLine, input.Kind);
        Assert.AreEqual(100, input.LineNumber);
    }

    [TestMethod]
    public void AKnownVerb_IsACommand_WithEverythingAfterItAsArguments()
    {
        var input = Parse("LIST 100-200");

        Assert.AreEqual(ReplInputKind.Command, input.Kind);
        Assert.AreEqual("LIST", input.CommandName);
        Assert.AreEqual("100-200", input.Arguments);
    }

    [TestMethod]
    public void AVerb_MatchesWhateverItsCasing()
    {
        Assert.AreEqual(ReplInputKind.Command, Parse("list").Kind);
        Assert.AreEqual(ReplInputKind.Command, Parse("List").Kind);
    }

    [TestMethod]
    public void AnUnknownWord_IsAStatementToRunNow()
    {
        var input = Parse("X = 42");

        Assert.AreEqual(ReplInputKind.Immediate, input.Kind);
        Assert.AreEqual("X = 42", input.Text);
    }

    [TestMethod]
    public void AVerbInsideANumberedLine_IsProgramText_NotACommand()
    {
        // the leading number settles it before the verb is ever looked at, which is what lets a
        // program contain a statement that starts with a word the shell also uses.
        var input = Parse("10 LIST = 1");

        Assert.AreEqual(ReplInputKind.StoreLine, input.Kind);
        Assert.AreEqual("LIST = 1", input.Text);
    }

    [TestMethod]
    public void PrintShorthand_ExpandsToDebugPrint_InImmediateMode()
    {
        var input = Parse("?2 + 2");

        Assert.AreEqual(ReplInputKind.Immediate, input.Kind);
        Assert.AreEqual("Debug.Print 2 + 2", input.Text);
    }

    [TestMethod]
    public void PrintShorthand_ExpandsToDebugPrint_InANumberedLine()
    {
        // the buffer holds real VBA, so a listing shows the expansion rather than a shell-only spelling.
        var input = Parse("20 ?X");

        Assert.AreEqual(ReplInputKind.StoreLine, input.Kind);
        Assert.AreEqual("Debug.Print X", input.Text);
    }

    [TestMethod]
    public void PrintShorthand_OnItsOwn_IsABareDebugPrint()
        => Assert.AreEqual("Debug.Print", Parse("?").Text);

    [TestMethod]
    public void AQuestionMarkThatIsNotLeading_IsNotTheShorthand()
        => Assert.AreEqual("X = Y ? Z", Parse("X = Y ? Z").Text);

    [TestMethod]
    public void ANumberTooBigForALineNumber_IsAStatement()
    {
        // past Int32: not a line number, so it can only be a statement starting with a numeric
        // literal - a syntax error the parser gets to report, not one the shell invents.
        var input = Parse("99999999999 = 1");

        Assert.AreEqual(ReplInputKind.Immediate, input.Kind);
    }
}
