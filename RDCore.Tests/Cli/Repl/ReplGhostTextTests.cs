using RDCore.CLI.App.Console;
using RDCore.CLI.App.Repl;
using static RDCore.Tests.Cli.Repl.ScriptedKeys;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// A numbered line typed at the prompt is offered the line of the program it numbers, in ghost text: taken a token at a time, or all of it, and gone once what
/// is typed is not that line any more.
/// </summary>
[TestClass]
public sealed class ReplGhostTextTests
{
    private static ReplGhostText Ghost()
    {
        var program = new ReplProgram();
        program.Store(10, "Print \"Hello, \" & Name");
        program.Store(100, "End");
        return new ReplGhostText(program);
    }

    [TestMethod]
    [DataRow("10", " Print \"Hello, \" & Name")]
    [DataRow("10 ", "Print \"Hello, \" & Name")]
    [DataRow("10   Pri", "nt \"Hello, \" & Name")]
    [DataRow("100", " End")]
    [DataRow("10 Print \"Hello, \" & Name", "")]
    public void TheGhost_IsTheRestOfTheLineTheNumberNumbers(string typed, string expected)
        => Assert.AreEqual(expected, Ghost().For(typed));

    [TestMethod]
    [DataRow("20", " Y = 1")]
    [DataRow("20 Y", " = 1")]
    public void TheGhost_OfAnIndentedLine_IsTheLineAsItIsTyped(string typed, string expected)
    {
        // the program stores the statement indented by the block it is in; what is typed after the number is the statement, and storing it indents it again.
        var program = new ReplProgram(statements => [.. statements.Select((statement, index) => index == 1 ? "    " + statement.TrimStart() : statement.TrimStart())]);
        program.Store(10, "If X Then");
        program.Store(20, "Y = 1");

        Assert.AreEqual(expected, new ReplGhostText(program).For(typed));
    }

    [TestMethod]
    [DataRow("1")]
    [DataRow("20")]
    [DataRow("10 Pr1")]
    [DataRow("10 print")]
    [DataRow("x = 10")]
    [DataRow("")]
    public void ThereIsNoGhost_ForALineTheProgramDoesNotHave_OrOnceWhatIsTypedIsNotItAnyMore(string typed)
        => Assert.IsNull(Ghost().For(typed));

    private static async Task<(string? Line, string Output)> EditAsync(ScriptedKeys keys)
    {
        var output = new StringWriter();
        var editor = new ConsolePromptEditor(keys, output, () => 80) { CompleteWords = false, Ghost = Ghost().For };
        return (await editor.ReadLineAsync(CancellationToken.None), output.ToString());
    }

    [TestMethod]
    public async Task TheGhost_IsDrawnFaint_AfterWhatIsTyped()
    {
        var (_, output) = await EditAsync(new ScriptedKeys().Type("10").Press(Key(ConsoleKey.Escape), Key(ConsoleKey.Enter)));
        StringAssert.Contains(output, "10\u001b[2m Print \"Hello, \" & Name\u001b[22m");
    }

    [TestMethod]
    public async Task RightArrow_TakesTheNextTokenOfTheGhost()
    {
        var (line, _) = await EditAsync(new ScriptedKeys().Type("10").Press(Key(ConsoleKey.RightArrow), Key(ConsoleKey.RightArrow), Key(ConsoleKey.Enter)));
        Assert.AreEqual("10 Print \"Hello, \"", line, "a name, then a string literal, each with the blanks before it");
    }

    [TestMethod]
    public async Task TabOrEnd_TakesAllOfTheGhost_WhichIsThenTypedLikeAnythingElse()
    {
        var (byTab, _) = await EditAsync(new ScriptedKeys().Type("10 ").Press(Key(ConsoleKey.Tab)).Type(" & \"!\"").Press(Key(ConsoleKey.Enter)));
        var (byEnd, _) = await EditAsync(new ScriptedKeys().Type("100").Press(Key(ConsoleKey.End), Key(ConsoleKey.Enter)));

        Assert.AreEqual("10 Print \"Hello, \" & Name & \"!\"", byTab);
        Assert.AreEqual("100 End", byEnd);
    }

    [TestMethod]
    public async Task OnceTheLineDiverges_TheGhostIsGone_AndTheKeysAreTheirsAgain()
    {
        var (line, output) = await EditAsync(new ScriptedKeys().Type("10 X").Press(Key(ConsoleKey.LeftArrow), Key(ConsoleKey.End)).Type("Y").Press(Key(ConsoleKey.Enter)));

        Assert.AreEqual("10 XY", line, "End moves to the end of the line, and takes nothing");
        Assert.IsFalse(output[output.LastIndexOf("10 X", StringComparison.Ordinal)..].Contains("\u001b[2mPrint"));
    }

    [TestMethod]
    public async Task EnterEntersWhatIsTyped_NotTheGhost()
        => Assert.AreEqual("10", (await EditAsync(new ScriptedKeys().Type("10").Press(Key(ConsoleKey.Enter)))).Line);
}
