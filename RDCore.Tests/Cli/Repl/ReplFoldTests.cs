using NSubstitute;
using RDCore.CLI;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.Themes;
using RDCore.SDK.ConsoleIO.Model;
using RDCore.SDK.Workspace;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// <c>FOLD</c> and the margin of <c>LIST</c>: a block of the program - as the language server says it folds - is folded and unfolded by the line it starts at,
/// a folded one is its first line, faint and cut to the console's width, and the margin has a lane for a breakpoint at the edge, and one for the mark of a
/// block next to the line.
/// </summary>
[TestClass]
public sealed class ReplFoldTests
{
    private readonly ReplProgram _program = new();
    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();
    private ReplCommandContext _context = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _program.Store(10, "x = 1");
        _program.Store(20, "If x > 0 Then");
        _program.Store(30, "    Print \"positive\"");
        _program.Store(40, "End If");
        _program.Store(50, "Print \"done\"");

        // the block of lines 20 to 40: the second line of the program to the fourth.
        _platform.GetBlockFoldsAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ReplFold>>([new ReplFold(1, 3)]));
        _platform.GetSemanticTokensAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ReplSemanticToken>>([]));

        var workspace = await ReplWorkspace.CreateAsync(new MockFileSystem(), Substitute.For<IProjectFileWriter>());
        _context = new ReplCommandContext(_program, _console, _platform, new ReplDocument(_program, _platform, workspace), []);
    }

    private static IAppThemeService Themes()
    {
        var themes = Substitute.For<IAppThemeService>();
        themes.Theme.Returns(new AppTheme(new ThemeDocument()));
        return themes;
    }

    private Task FoldAsync(string arguments) => new FoldReplCommand().ExecuteAsync(_context, arguments, CancellationToken.None);

    // what the listing wrote: each line's margin lanes, and its text.
    private async Task<List<(IReadOnlyList<ReplTextRun> Margin, IReadOnlyList<ReplTextRun> Runs)>> ListAsync()
    {
        _console.ClearReceivedCalls();
        await new ListReplCommand(Themes()).ExecuteAsync(_context, "", CancellationToken.None);
        return [.. _console.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine))
            .Select(call => ((IReadOnlyList<ReplTextRun>)call.GetArguments()[0]!, (IReadOnlyList<ReplTextRun>)call.GetArguments()[1]!))];
    }

    private static string TextOf(IReadOnlyList<ReplTextRun> runs) => string.Concat(runs.Select(run => run.Text)).Trim();

    [TestMethod]
    public async Task AFoldedBlock_IsItsFirstLine_FaintAsGhostText_AndNoneOfTheLinesAfterIt()
    {
        await FoldAsync("20");

        _console.Received(1).WriteLine(string.Format(Resources.Repl_Fold_Folded, 20));
        var listed = await ListAsync();
        CollectionAssert.AreEqual(new[] { "10 x = 1", "20 If x > 0 Then", "50 Print \"done\"" }, listed.Select(line => TextOf(line.Runs)).ToArray());
        Assert.AreEqual(ReplTextStyle.Ghost, listed[1].Runs[^1].Style);
        Assert.AreEqual("+", listed[1].Margin[1].Text, "the mark of a folded block, in ASCII on a console that draws no emoji");
    }

    [TestMethod]
    public async Task FoldingItAgain_UnfoldsIt()
    {
        await FoldAsync("20");
        await FoldAsync("20");

        _console.Received(1).WriteLine(string.Format(Resources.Repl_Fold_Unfolded, 20));
        var listed = await ListAsync();
        Assert.HasCount(5, listed);
        Assert.AreEqual("-", listed[1].Margin[1].Text, "the mark of a block that is open");
        Assert.AreEqual(" ", listed[0].Margin[1].Text, "a line that starts no block has the lane, blank");
        Assert.AreNotEqual(ReplTextStyle.Ghost, listed[1].Runs[^1].Style);
    }

    [TestMethod]
    public async Task ALineThatStartsNoBlock_IsAnError()
    {
        await FoldAsync("10");

        _console.Received(1).WriteMessage(MessageKind.Error, string.Format(Resources.Repl_Fold_None, 10));
        Assert.IsFalse(_context.Folds.IsFolded(10));
    }

    [TestMethod]
    public async Task ALineThatIsNotInTheProgram_IsAnUndefinedLine()
    {
        await FoldAsync("99");

        _console.Received(1).WriteMessage(MessageKind.Error, Resources.Repl_UndefinedLine, "99");
    }

    [TestMethod]
    public async Task AFoldedBlocksFirstLine_IsCutToTheConsolesWidth_WithAnEllipsis()
    {
        _console.Width.Returns(16);
        await FoldAsync("20");

        var folded = (await ListAsync())[1];
        var text = folded.Runs[^1].Text;
        StringAssert.EndsWith(text, "…");
        Assert.IsLessThanOrEqualTo(16, folded.Margin.Concat(folded.Runs).Sum(run => RDCore.CLI.App.Console.ConsoleText.WidthOf(run.Text)) + 1);
    }

    [TestMethod]
    public async Task AConsoleThatDrawsEmoji_HasTheirMarks_AndTheBreakpointKeepsTheLaneAtTheEdge()
    {
        _console.DrawsUnicode.Returns(true);
        _context.Debugger.ToggleBreakpoint(20);

        var listed = await ListAsync();

        Assert.HasCount(2, listed[1].Margin);
        Assert.AreEqual(ReplTextStyle.BreakpointGlyph, listed[1].Margin[0].Style, "the breakpoint's lane is the left-most one, at the edge");
        Assert.AreEqual("🔽", listed[1].Margin[1].Text, "and the fold lane is next to the line");
        Assert.AreEqual("  ", listed[0].Margin[1].Text, "the fold lane is the emoji's two cells wide on every line");
    }

    [TestMethod]
    public void AFold_IsOnALineNumber_AndClearedWithTheProgram()
    {
        var folds = new ReplFolds();

        Assert.IsTrue(folds.Toggle(20));
        Assert.IsTrue(folds.IsFolded(20));
        Assert.IsFalse(folds.Toggle(20));
        folds.Toggle(20);
        folds.Clear();
        Assert.IsFalse(folds.IsFolded(20));
    }
}
