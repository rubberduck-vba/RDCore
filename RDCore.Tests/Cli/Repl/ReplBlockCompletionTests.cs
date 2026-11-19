using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Console;
using RDCore.CLI.App.Repl;
using RDCore.CLI.ConsoleIO.Model;
using RDCore.SDK.Formatting;
using static RDCore.Tests.Cli.Repl.ScriptedKeys;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// Block completion at the shell's prompt: a numbered line in a block the program has no closer for opens the next line on the number after it, selected, with
/// the closers the program needs drawn under it; once the block has a body, Tab takes its closer and enters it. Each line is stored as it is entered.
/// </summary>
[TestClass]
public sealed class ReplBlockCompletionTests
{
    private static readonly ConsoleKeyInfo Tab = Key(ConsoleKey.Tab);
    private static readonly ConsoleKeyInfo Enter = Key(ConsoleKey.Enter);
    private static readonly ConsoleKeyInfo Escape = Key(ConsoleKey.Escape);

    // a shell with its prompt wired as the shell's own is: the keys run out, and the shell ends.
    private sealed class Shell
    {
        public Shell(ScriptedKeys keys, params (int Number, string Statement)[] lines)
        {
            Program = ReplIndentation.Program(Microsoft.Extensions.Options.Options.Create(new RDCore.SDK.Server.Configuration.SdkAppOptions()), liveSettings: null, logger: null);
            foreach (var (number, statement) in lines)
            {
                Program.Store(number, statement);
            }

            Blocks = new ReplBlocks(Program);
            var ghostText = new ReplGhostText(Program);
            Editor = new ConsolePromptEditor(keys, Output, () => 120)
            {
                Ghost = typed => ghostText.For(typed) ?? Blocks.GhostFor(typed),
                EntersWithGhost = Blocks.Closes,
                GhostRows = Blocks.RowsFor,
            };
            Dispatcher.IsCommandName(Arg.Any<string>()).Returns(call => string.Equals(call.Arg<string>(), "LIST", StringComparison.OrdinalIgnoreCase));
        }

        public ReplProgram Program { get; }

        public ReplBlocks Blocks { get; }

        public ConsolePromptEditor Editor { get; }

        public StringWriter Output { get; } = new();

        public IReplConsole Console { get; } = Substitute.For<IReplConsole>();

        public IReplCommandDispatcher Dispatcher { get; } = Substitute.For<IReplCommandDispatcher>();

        public async Task RunAsync()
        {
            var platform = Substitute.For<IReplPlatformClient>();
            var shell = new ReplShell(
                Program, Console, platform, new ReplDocument(Program, platform), Dispatcher, NullLogger<ReplShell>.Instance, Editor, blocks: Blocks);
            await shell.RunAsync(CancellationToken.None);
        }

        public string[] Lines => [.. Program.Statements().Select(line => $"{line.Number} {line.Statement.TrimStart()}")];
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AnIfWithABody_IsClosedByTab()
    {
        var shell = new Shell(new ScriptedKeys()
            .Type("120 If x > 0 Then").Press(Enter)
            .Press(Tab).Type("Debug.Print 1").Press(Enter)
            .Press(Tab, Tab));

        await shell.RunAsync();

        CollectionAssert.AreEqual(new[] { "120 If x > 0 Then", "130 Debug.Print 1", "140 End If" }, shell.Lines);
        Assert.IsFalse(shell.Blocks.IsOpen, "the closer ends block completion");
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AnElse_OpensTheOtherBranch_AndItsBodyIsClosedByTab()
    {
        var shell = new Shell(new ScriptedKeys()
            .Type("10 If x Then").Press(Enter)
            .Press(Tab).Type("y = 1").Press(Enter)
            .Press(Tab).Type("Else").Press(Enter)
            .Press(Tab).Type("y = 2").Press(Enter)
            .Press(Tab, Tab));

        await shell.RunAsync();

        CollectionAssert.AreEqual(new[] { "10 If x Then", "20 y = 1", "30 Else", "40 y = 2", "50 End If" }, shell.Lines);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ALoopInAnIf_StacksItsCloserOverTheIfs()
    {
        var shell = new Shell(new ScriptedKeys()
            .Type("10 If x Then").Press(Enter)
            .Press(Tab).Type("For i = 1 To 3").Press(Enter)
            .Press(Tab).Type("Debug.Print i").Press(Enter)
            .Press(Tab, Tab)
            .Press(Tab, Tab));

        await shell.RunAsync();

        CollectionAssert.AreEqual(new[] { "10 If x Then", "20 For i = 1 To 3", "30 Debug.Print i", "40 Next", "50 End If" }, shell.Lines);
        var drawn = shell.Output.ToString();
        StringAssert.Contains(drawn, "40 Next");
        StringAssert.Contains(drawn, "50 End If");
    }

    [TestMethod]
    public void TheRowsUnderALine_AreTheClosersInnermostFirst_NumberedOnFromIt()
    {
        var shell = new Shell(new ScriptedKeys(), (10, "If x Then"), (20, "For i = 1 To 3"));
        shell.Blocks.Stored(20);

        CollectionAssert.AreEqual(new[] { "40 Next", "50 End If" }, shell.Blocks.RowsFor("30 ").ToArray());
        CollectionAssert.AreEqual(new[] { "40 Next", "50 End If" }, shell.Blocks.RowsFor("30 Debug.Print i").ToArray(), "a body line keeps them under it");
        CollectionAssert.AreEqual(new[] { "45 Next", "55 End If" }, shell.Blocks.RowsFor("35 ").ToArray(), "a typed number counts on");
        Assert.IsEmpty(shell.Blocks.RowsFor("Debug.Print i"), "a line with no number has none");
    }

    [TestMethod]
    public void TheLineThatCanBeTheInnermostCloser_LeavesItOutOfTheRows()
    {
        var shell = new Shell(new ScriptedKeys(), (10, "If x Then"), (20, "For i = 1 To 3"), (30, "Debug.Print i"));
        shell.Blocks.Stored(30);

        Assert.AreEqual("Next", shell.Blocks.GhostFor("40 "));
        CollectionAssert.AreEqual(new[] { "50 End If" }, shell.Blocks.RowsFor("40 ").ToArray());
        CollectionAssert.AreEqual(new[] { "50 End If" }, shell.Blocks.RowsFor("40 Ne").ToArray());
        CollectionAssert.AreEqual(new[] { "50 Next", "60 End If" }, shell.Blocks.RowsFor("40 y = i").ToArray());
    }

    [TestMethod]
    public void ACloserTheNextLineLeavesNoNumberFor_IsNotDrawn_AndTheRowSaysWhy()
    {
        var shell = new Shell(new ScriptedKeys(), (160, "If x Then"), (170, "For i = 1 To 3"), (200, "Debug.Print 9"));
        Assert.IsNull(shell.Blocks.Stored(170));

        var rows = shell.Blocks.RowsFor("180 ");

        CollectionAssert.AreEqual(new[] { "190 Next", string.Format(RDCore.CLI.Resources.Repl_BlockNoRoom, "End If", "200") }, rows.ToArray());
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task TakenNumbers_StopBlockCompletion_AndTheShellSaysWhy()
    {
        var shell = new Shell(new ScriptedKeys().Type("180 If x Then").Press(Enter), (200, "Debug.Print 9"));

        await shell.RunAsync();

        Assert.IsFalse(shell.Blocks.IsOpen);
        Assert.IsNull(shell.Blocks.Start);
        shell.Console.Received(1).WriteMessage(MessageKind.Information, string.Format(RDCore.CLI.Resources.Repl_BlockNoRoom, "End If", "200"), Arg.Any<string?>());
        CollectionAssert.AreEqual(new[] { "180 If x Then", "200 Debug.Print 9" }, shell.Lines);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ATypedNumber_ReplacesTheSuggestion_AndTheNextCountsOnFromIt()
    {
        var shell = new Shell(new ScriptedKeys()
            .Type("10 If x Then").Press(Enter)
            .Type("15").Press(Tab).Type("y = 1").Press(Enter)
            .Press(Tab, Tab));

        await shell.RunAsync();

        CollectionAssert.AreEqual(new[] { "10 If x Then", "15 y = 1", "25 End If" }, shell.Lines);
    }

    [TestMethod]
    public void ABlockThatIsClosedAlready_StartsNoBlockCompletion()
    {
        var shell = new Shell(new ScriptedKeys(), (120, "If x > 0 Then"), (130, "y = 1"), (200, "End If"));

        Assert.IsNull(shell.Blocks.Stored(120));

        Assert.IsFalse(shell.Blocks.IsOpen);
        Assert.IsNull(shell.Blocks.Start);
    }

    [TestMethod]
    public void AOneLineIf_StartsNoBlockCompletion()
    {
        var shell = new Shell(new ScriptedKeys(), (10, "If x Then y = 1"));

        Assert.IsNull(shell.Blocks.Stored(10));

        Assert.IsFalse(shell.Blocks.IsOpen);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task EscapeMidBlock_KeepsTheStoredLines_AndAnEmptyLineEndsBlockCompletion()
    {
        var shell = new Shell(new ScriptedKeys()
            .Type("10 If x Then").Press(Enter)
            .Press(Tab).Type("y = 1").Press(Enter)
            .Press(Escape, Escape, Enter));

        await shell.RunAsync();

        CollectionAssert.AreEqual(new[] { "10 If x Then", "20 y = 1" }, shell.Lines);
        Assert.IsFalse(shell.Blocks.IsOpen);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ACommand_EndsBlockCompletion_AndListShowsTheProgramIndented()
    {
        var shell = new Shell(new ScriptedKeys()
            .Type("10 If x Then").Press(Enter)
            .Press(Tab).Type("For i = 1 To 3").Press(Enter)
            .Press(Escape, Escape).Type("LIST").Press(Enter));

        await shell.RunAsync();

        Assert.IsFalse(shell.Blocks.IsOpen);
        await shell.Dispatcher.Received(1).DispatchAsync(Arg.Any<ReplCommandContext>(), "LIST", Arg.Any<string>(), Arg.Any<CancellationToken>());
        CollectionAssert.AreEqual(new[] { (10, "If x Then"), (20, "    For i = 1 To 3") }, shell.Program.Lines().ToArray());
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task InputReadALineAtATime_DrawsNoGhostRows_AndOpensNoLineOnANumber()
    {
        var shell = new Shell(new ScriptedKeys(interactive: false).Lines("10 If x Then", "20 For i = 1 To 3", "30 Next", "40 End If"));

        await shell.RunAsync();

        CollectionAssert.AreEqual(new[] { "10 If x Then", "20 For i = 1 To 3", "30 Next", "40 End If" }, shell.Lines);
        Assert.AreEqual(string.Empty, shell.Output.ToString(), "nothing is drawn at a prompt that reads lines");
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ALineStartedOnASnippet_HasItsNumberSelected_WhichTypingReplaces()
    {
        Snippet.TryParse("${1:30} $0", out var start);
        var editor = new ConsolePromptEditor(new ScriptedKeys().Type("25").Press(Tab).Type("x = 1").Press(Enter), new StringWriter(), () => 80);

        Assert.AreEqual("25 x = 1", await editor.ReadLineAsync(CancellationToken.None, start));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task TheGhostTextTakenWithEnd_DoesNotEnterTheLine()
    {
        var editor = new ConsolePromptEditor(new ScriptedKeys().Type("40 ").Press(Key(ConsoleKey.End)).Type("!").Press(Enter), new StringWriter(), () => 80)
        {
            Ghost = typed => typed == "40 " ? "End If" : null,
            EntersWithGhost = _ => true,
        };

        Assert.AreEqual("40 End If!", await editor.ReadLineAsync(CancellationToken.None));
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task GhostRows_AreDrawnFaintUnderTheLine_AndNotOnceItIsEntered()
    {
        var output = new StringWriter();
        var editor = new ConsolePromptEditor(new ScriptedKeys().Type("30").Press(Enter), output, () => 80)
        {
            GhostRows = typed => typed.Length > 0 ? ["40 End If"] : [],
        };

        await editor.ReadLineAsync(CancellationToken.None);

        StringAssert.Contains(output.ToString(), "\u001b[2m40 End If\u001b[22m");
    }
}
