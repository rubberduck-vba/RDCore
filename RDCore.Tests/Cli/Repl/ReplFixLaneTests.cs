using Newtonsoft.Json.Linq;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.Themes;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Workspace;
using System.IO.Abstractions.TestingHelpers;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The light bulb in <c>LIST</c>'s breakpoint lane: a line that has a diagnostic with a fix in its <c>data</c> is marked, unless it has a breakpoint, whose mark
/// takes the lane; the lane is as wide on every line as the bulb is; and the findings are asked for with the report the shell has, so that a program that did not
/// change costs an <c>unchanged</c> report.
/// </summary>
[TestClass]
public sealed class ReplFixLaneTests
{
    private readonly ReplProgram _program = new();
    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();
    private ReplCommandContext _context = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _program.Store(10, "d = 1");
        _program.Store(20, "x = 2");
        _program.Store(30, "y = 3");
        _platform.GetSemanticTokensAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ReplSemanticToken>>([]));
        _platform.GetBlockFoldsAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ReplFold>>([]));

        var workspace = await ReplWorkspace.CreateAsync(new MockFileSystem(), Substitute.For<IProjectFileWriter>());
        _context = new ReplCommandContext(_program, _console, _platform, new ReplDocument(_program, _platform, workspace), []);
    }

    // a finding on the line at a position of the program, with a rename as its fix - or with the metadata of an error, which is no fix.
    private static Diagnostic Finding(int line, bool withAFix = true) => new()
    {
        Range = new LspRange(line, 0, line, 1),
        Code = "RDC01001",
        Data = withAFix ? new DiagnosticFixes { Rename = new RenameFix { Suggestions = ["Distance"] } }.ToData() : JObject.FromObject(new { kind = "error" }),
    };

    private void ServerFinds(params Diagnostic[] findings)
        => _platform.PullDiagnosticReportAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReplDiagnosticReport?>(new ReplDiagnosticReport("v1", false, findings)));

    private async Task<List<IReadOnlyList<ReplTextRun>>> ListedMarginsAsync()
    {
        _console.ClearReceivedCalls();
        var themes = Substitute.For<IAppThemeService>();
        themes.Theme.Returns(new AppTheme(new ThemeDocument()));
        await new ListReplCommand(themes).ExecuteAsync(_context, "", CancellationToken.None);
        return [.. _console.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine))
            .Select(call => (IReadOnlyList<ReplTextRun>)call.GetArguments()[0]!)];
    }

    [TestMethod]
    public async Task ALineWithAFix_IsMarkedWithALightBulb_AndTheOtherLinesHaveTheLaneBlankAndAsWide()
    {
        _console.DrawsUnicode.Returns(true);
        ServerFinds(Finding(0));

        var margins = await ListedMarginsAsync();

        Assert.AreEqual("💡", margins[0][0].Text);
        Assert.AreEqual("  ", margins[1][0].Text, "the lane is the bulb's two cells on a line with none");
    }

    [TestMethod]
    public async Task OnAConsoleWithNoUnicode_TheMarkIsAnAsterisk_InOneCell()
    {
        ServerFinds(Finding(1));

        var margins = await ListedMarginsAsync();

        Assert.AreEqual("*", margins[1][0].Text);
        Assert.AreEqual(" ", margins[0][0].Text);
    }

    [TestMethod]
    public async Task ABreakpoint_TakesTheLaneBeforeTheBulb_AndIsPaddedToTheLanesWidth()
    {
        _console.DrawsUnicode.Returns(true);
        _context.Debugger.ToggleBreakpoint(10);
        ServerFinds(Finding(0), Finding(2));

        var margins = await ListedMarginsAsync();

        Assert.AreEqual("● ", margins[0][0].Text);
        Assert.AreEqual(ReplTextStyle.BreakpointGlyph, margins[0][0].Style);
        Assert.AreEqual("💡", margins[2][0].Text);
    }

    [TestMethod]
    public async Task AFindingWithNoFix_IsNoBulb_AndTheLaneStaysOneCell()
    {
        _console.DrawsUnicode.Returns(true);
        ServerFinds(Finding(0, withAFix: false));

        var margins = await ListedMarginsAsync();

        Assert.IsTrue(margins.All(margin => margin[0].Text == " "));
    }

    [TestMethod]
    public async Task AServerThatCannotBeAsked_IsAListingWithNoBulbs()
    {
        _platform.PullDiagnosticReportAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ReplDiagnosticReport?>>(_ => throw new InvalidOperationException("the server is gone"));

        var margins = await ListedMarginsAsync();

        Assert.HasCount(3, margins);
        Assert.IsTrue(margins.All(margin => margin[0].Text == " "));
    }

    [TestMethod]
    public async Task TheFindings_AreAskedForWithTheReportTheShellHas_AndAnUnchangedReportKeepsThem()
    {
        ServerFinds(Finding(0));
        await _context.Diagnostics.RefreshAsync(_context, CancellationToken.None);
        _platform.PullDiagnosticReportAsync(Arg.Any<Uri>(), "v1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReplDiagnosticReport?>(new ReplDiagnosticReport("v1", true, [])));

        var again = await _context.Diagnostics.RefreshAsync(_context, CancellationToken.None);

        Assert.HasCount(1, again, "an unchanged report carries no findings, and the shell keeps its own");
        await _platform.Received(1).PullDiagnosticReportAsync(Arg.Any<Uri>(), "v1", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AnotherProgram_IsAskedAboutFromNothing()
    {
        ServerFinds(Finding(0));
        await _context.Diagnostics.RefreshAsync(_context, CancellationToken.None);

        _context.Diagnostics.Clear();
        await _context.Diagnostics.RefreshAsync(_context, CancellationToken.None);

        await _platform.Received(2).PullDiagnosticReportAsync(Arg.Any<Uri>(), null, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AFindingIsAskedForOnceTheProgramWasLeftAlone_AndNotForEveryLineOfAPastedOne()
    {
        // the program is left alone when the test says so: each edit drops the wait of the one before, and the last is the one that asks.
        var leftAlone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostics = new ReplDiagnostics { Delay = (_, token) => leftAlone.Task.WaitAsync(token) };
        ServerFinds(Finding(0));

        diagnostics.RefreshWhenIdle(_context);
        diagnostics.RefreshWhenIdle(_context);
        diagnostics.RefreshWhenIdle(_context);
        leftAlone.SetResult();
        await diagnostics.Pending.WaitAsync(TimeSpan.FromSeconds(20));

        await _platform.Received(1).PullDiagnosticReportAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        Assert.HasCount(1, diagnostics.Items);
    }
}
