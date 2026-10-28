using NSubstitute;
using RDCore.CLI;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.Themes;
using RDCore.SDK.Workspace;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// <c>INLAYS</c> and the hints of a listing: <c>LIST</c> shows, faint and before the character each is for, the hints the language server gives for the program
/// - asked once for the whole listing - once <c>INLAYS ON</c> has been said, and as it is typed otherwise.
/// </summary>
[TestClass]
public sealed class ReplInlaysTests
{
    private readonly ReplProgram _program = new();
    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();
    private ReplCommandContext _context = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _program.Store(10, "MsgBox \"Hello\", vbOKOnly");
        _program.Store(20, "Dim x");

        _platform.GetBlockFoldsAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ReplFold>>([]));
        _platform.GetSemanticTokensAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ReplSemanticToken>>([]));
        // "10 MsgBox "Hello", vbOKOnly": the arguments begin at characters 10 and 19; "20 Dim x" ends at 8.
        _platform.GetInlayHintsAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ReplInlayHint>>(
        [
            new ReplInlayHint(0, 10, "Prompt: "),
            new ReplInlayHint(0, 19, "Buttons: "),
            new ReplInlayHint(1, 8, " As Variant"),
        ]));

        var workspace = await ReplWorkspace.CreateAsync(new MockFileSystem(), Substitute.For<IProjectFileWriter>());
        _context = new ReplCommandContext(_program, _console, _platform, new ReplDocument(_program, _platform, workspace), []);
    }

    private static IAppThemeService Themes()
    {
        var themes = Substitute.For<IAppThemeService>();
        themes.Theme.Returns(new AppTheme(new ThemeDocument()));
        return themes;
    }

    private Task InlaysAsync(string arguments = "") => new InlaysReplCommand().ExecuteAsync(_context, arguments, CancellationToken.None);

    private async Task<List<IReadOnlyList<ReplTextRun>>> ListAsync()
    {
        _console.ClearReceivedCalls();
        await new ListReplCommand(Themes()).ExecuteAsync(_context, "", CancellationToken.None);
        return [.. _console.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine))
            .Select(call => (IReadOnlyList<ReplTextRun>)call.GetArguments()[1]!)];
    }

    private static string TextOf(IReadOnlyList<ReplTextRun> runs) => string.Concat(runs.Select(run => run.Text)).Trim();

    [TestMethod]
    public async Task ByDefault_AListingShowsNoHint_AndAsksForNone()
    {
        var listed = await ListAsync();

        CollectionAssert.AreEqual(new[] { "10 MsgBox \"Hello\", vbOKOnly", "20 Dim x" }, listed.Select(TextOf).ToArray());
        await _platform.DidNotReceiveWithAnyArgs().GetInlayHintsAsync(default!, default);
    }

    [TestMethod]
    public async Task InlaysWithNoArgument_IsOn_AndAListingShowsTheHints_AsGhostText()
    {
        await InlaysAsync();
        _console.Received().WriteLine(Resources.Repl_Inlays_On);
        var listed = await ListAsync();

        CollectionAssert.AreEqual(new[] { "10 MsgBox Prompt: \"Hello\", Buttons: vbOKOnly", "20 Dim x As Variant" }, listed.Select(TextOf).ToArray());
        Assert.AreEqual(ReplTextStyle.Ghost, listed[0].Single(run => run.Text == "Prompt: ").Style);
        Assert.AreEqual(ReplTextStyle.Ghost, listed[1].Single(run => run.Text == " As Variant").Style);
        await _platform.Received(1).GetInlayHintsAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task InlaysOff_ListsTheProgramAsItIsTyped()
    {
        await InlaysAsync("on");
        await InlaysAsync("OFF");
        _console.Received().WriteLine(Resources.Repl_Inlays_Off);
        var listed = await ListAsync();

        CollectionAssert.AreEqual(new[] { "10 MsgBox \"Hello\", vbOKOnly", "20 Dim x" }, listed.Select(TextOf).ToArray());
    }

    [TestMethod]
    public async Task InlaysWithAnythingElse_IsASyntaxError()
    {
        await InlaysAsync("maybe");

        Assert.IsFalse(_context.Inlays.Enabled);
        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Resources.Repl_SyntaxError, "maybe");
    }

    [TestMethod]
    public async Task AListing_IsAsLegible_WhenTheServerCannotGiveHints()
    {
        _platform.GetInlayHintsAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<ReplInlayHint>>>(_ => throw new InvalidOperationException("no answer"));
        await InlaysAsync();

        var listed = await ListAsync();

        CollectionAssert.AreEqual(new[] { "10 MsgBox \"Hello\", vbOKOnly", "20 Dim x" }, listed.Select(TextOf).ToArray());
    }

    [TestMethod]
    public void AHint_SplitsTheRunItIsIn_AndLeavesTheRestOfTheLine()
    {
        var runs = ReplInlayRuns.With([new ReplTextRun("ab", ReplTextStyle.Keyword), new ReplTextRun("cd", ReplTextStyle.Plain)], [new ReplInlayHint(0, 1, "<"), new ReplInlayHint(0, 3, ">")]);

        CollectionAssert.AreEqual(
            new[] { ("a", ReplTextStyle.Keyword), ("<", ReplTextStyle.Ghost), ("b", ReplTextStyle.Keyword), ("c", ReplTextStyle.Plain), (">", ReplTextStyle.Ghost), ("d", ReplTextStyle.Plain) },
            runs.Select(run => (run.Text, run.Style)).ToArray());
    }
}
