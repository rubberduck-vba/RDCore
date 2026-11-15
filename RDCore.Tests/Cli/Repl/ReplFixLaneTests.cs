using Newtonsoft.Json.Linq;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.External.Interaction;
using RDCore.SDK.Workspace;
using System.IO.Abstractions.TestingHelpers;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The light bulb in <c>LIST</c>'s diagnostic lane: a line that has a diagnostic with a fix in its <c>data</c> is marked with it, and a line with no fix with its
/// most severe diagnostic; a breakpoint has a lane of its own; the lane is as wide on every line as its widest mark is; and the findings are asked for once a listing, with the report the shell has, so that a program that did not change costs an <c>unchanged</c> report.
/// </summary>
[TestClass]
public sealed class ReplFixLaneTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(5);

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
    private static Diagnostic Finding(int line, bool withAFix = true, DiagnosticSeverity? severity = null) => new()
    {
        Range = new LspRange(line, 0, line, 1),
        Code = "RDC01001",
        Severity = severity,
        Data = withAFix ? new DiagnosticFixes { Rename = new RenameFix { Suggestions = ["Distance"] } }.ToData() : JObject.FromObject(new { kind = "error" }),
    };

    // a finding with no fix, of a severity.
    private static Diagnostic Of(DiagnosticSeverity severity, int line) => Finding(line, withAFix: false, severity);

    private void ServerFinds(params Diagnostic[] findings)
        => _platform.PullDiagnosticReportAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReplDiagnosticReport?>(new ReplDiagnosticReport("v1", false, findings)));

    private async Task<List<IReadOnlyList<ReplTextRun>>> ListedMarginsAsync()
    {
        _console.ClearReceivedCalls();
        await new ListReplCommand().ExecuteAsync(_context, "", CancellationToken.None);
        return [.. _console.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine))
            .Select(call => (IReadOnlyList<ReplTextRun>)call.GetArguments()[0]!)];
    }

    // the lanes of the margin, by name.
    private const int Breakpoints = 0, Diagnostics = 1, Folds = 2;

    [TestMethod]
    public async Task TheMargin_HasThreeLanes_BreakpointsAtTheEdge_ThenDiagnostics_ThenFoldsNextToTheLine()
    {
        _console.DrawsUnicode.Returns(true);
        _platform.GetBlockFoldsAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ReplFold>>([new ReplFold(0, 1)]));
        _context.Debugger.ToggleBreakpoint(10);
        ServerFinds(Of(DiagnosticSeverity.Warning, 0));

        var margins = await ListedMarginsAsync();

        CollectionAssert.AreEqual(new[] { "●", "🔶", "▽" }, margins[0].Select(lane => lane.Text).ToArray());
        CollectionAssert.AreEqual(new[] { " ", "  ", " " }, margins[2].Select(lane => lane.Text).ToArray(), "each lane is as wide on a line with no mark");
    }

    [TestMethod]
    public async Task ALineWithAFix_IsMarkedWithALightBulb_AndTheOtherLinesHaveTheLaneBlankAndAsWide()
    {
        _console.DrawsUnicode.Returns(true);
        ServerFinds(Finding(0));

        var margins = await ListedMarginsAsync();

        Assert.AreEqual("💡", margins[0][Diagnostics].Text);
        Assert.AreEqual("  ", margins[1][Diagnostics].Text, "the lane is the bulb's two cells on a line with none");
    }

    [TestMethod]
    public async Task OnAConsoleWithNoUnicode_TheMarkIsAnAsterisk_InOneCell()
    {
        ServerFinds(Finding(1));

        var margins = await ListedMarginsAsync();

        Assert.AreEqual("*", margins[1][Diagnostics].Text);
        Assert.AreEqual(" ", margins[0][Diagnostics].Text);
    }

    [TestMethod]
    public async Task ABreakpoint_HidesNeitherTheBulbNorADiagnostic_OnItsLine()
    {
        _console.DrawsUnicode.Returns(true);
        _context.Debugger.ToggleBreakpoint(10);
        _context.Debugger.ToggleBreakpoint(20);
        ServerFinds(Finding(0), Of(DiagnosticSeverity.Error, 1));

        var margins = await ListedMarginsAsync();

        Assert.AreEqual(("●", ReplTextStyle.BreakpointGlyph, "💡"), (margins[0][Breakpoints].Text, margins[0][Breakpoints].Style, margins[0][Diagnostics].Text));
        Assert.AreEqual(("●", "❌"), (margins[1][Breakpoints].Text, margins[1][Diagnostics].Text));
        Assert.AreEqual((" ", ReplTextStyle.Plain), (margins[2][Breakpoints].Text, margins[2][Breakpoints].Style), "the lane of a breakpoint is its one cell");
    }

    [TestMethod]
    [DataRow(DiagnosticSeverity.Error, "❌")]
    [DataRow(DiagnosticSeverity.Warning, "🔶")]
    [DataRow(DiagnosticSeverity.Information, "🔹")]
    [DataRow(DiagnosticSeverity.Hint, "· ")]
    public async Task ALineWithNoFix_IsMarkedWithItsDiagnostic_InTheBulbsTwoCells(DiagnosticSeverity severity, string mark)
    {
        _console.DrawsUnicode.Returns(true);
        ServerFinds(Of(severity, 1));

        var margins = await ListedMarginsAsync();

        Assert.AreEqual((mark, ReplTextStyle.Plain), (margins[1][Diagnostics].Text, margins[1][Diagnostics].Style));
        Assert.AreEqual("  ", margins[0][Diagnostics].Text, "the lane is two cells on a line with none");
    }

    [TestMethod]
    [DataRow(DiagnosticSeverity.Error, "X")]
    [DataRow(DiagnosticSeverity.Warning, "!")]
    [DataRow(DiagnosticSeverity.Information, "i")]
    [DataRow(DiagnosticSeverity.Hint, ".")]
    public async Task OnAConsoleWithNoUnicode_TheMarkOfADiagnostic_IsOneCell(DiagnosticSeverity severity, string mark)
    {
        ServerFinds(Of(severity, 1));

        var margins = await ListedMarginsAsync();

        Assert.AreEqual(mark, margins[1][Diagnostics].Text);
        Assert.AreEqual(" ", margins[0][Diagnostics].Text);
    }

    [TestMethod]
    public async Task EachLane_IsAsWideOnEveryLine_WithUnicodeOrWithout()
    {
        ServerFinds(Finding(0), Of(DiagnosticSeverity.Error, 1));

        _console.DrawsUnicode.Returns(true);
        var unicode = await ListedMarginsAsync();
        _console.DrawsUnicode.Returns(false);
        var ascii = await ListedMarginsAsync();

        CollectionAssert.AreEqual(new[] { 3, 3, 3 }, unicode.Select(margin => margin.Sum(lane => ConsoleText.WidthOf(lane.Text))).ToArray(), "1 + 2, and no fold");
        CollectionAssert.AreEqual(new[] { 2, 2, 2 }, ascii.Select(margin => margin.Sum(lane => ConsoleText.WidthOf(lane.Text))).ToArray(), "1 + 1");
        CollectionAssert.AreEqual(new[] { "*", "X", " " }, ascii.Select(margin => margin[Diagnostics].Text).ToArray());
    }

    [TestMethod]
    public async Task TheLaneOfTheDiagnostics_IsThere_WhenNoLineHasOne()
    {
        _console.DrawsUnicode.Returns(true);
        ServerFinds();

        var margins = await ListedMarginsAsync();

        Assert.IsTrue(margins.All(margin => margin[Diagnostics].Text == "  "), "the code does not move when a diagnostic comes or goes");
    }

    [TestMethod]
    public async Task TheMostSevereDiagnostic_OfALine_IsTheOneMarked()
    {
        _console.DrawsUnicode.Returns(true);
        ServerFinds(Of(DiagnosticSeverity.Information, 0), Of(DiagnosticSeverity.Error, 0), Of(DiagnosticSeverity.Warning, 0),
            Of(DiagnosticSeverity.Hint, 1), Of(DiagnosticSeverity.Information, 1));

        var margins = await ListedMarginsAsync();

        Assert.AreEqual("❌", margins[0][Diagnostics].Text);
        Assert.AreEqual("🔹", margins[1][Diagnostics].Text);
    }

    [TestMethod]
    public async Task ADiagnosticThatStatesNoSeverity_IsMarkedAsAnError()
    {
        ServerFinds(Finding(2, withAFix: false));

        var margins = await ListedMarginsAsync();

        Assert.AreEqual("X", margins[2][Diagnostics].Text);
    }

    [TestMethod]
    public async Task AFix_WinsOverAnError_OnItsLine()
    {
        _console.DrawsUnicode.Returns(true);
        ServerFinds(Of(DiagnosticSeverity.Error, 0), Finding(0, severity: DiagnosticSeverity.Warning));

        var margins = await ListedMarginsAsync();

        Assert.AreEqual("💡", margins[0][Diagnostics].Text);
    }

    [TestMethod]
    public async Task AFoldedBlock_IsMarkedForTheDiagnosticsOfTheLinesItHides()
    {
        _console.DrawsUnicode.Returns(true);
        _platform.GetBlockFoldsAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ReplFold>>([new ReplFold(0, 2)]));
        _context.Folds.Toggle(10);
        ServerFinds(Of(DiagnosticSeverity.Warning, 1), Of(DiagnosticSeverity.Error, 2));

        var margins = await ListedMarginsAsync();

        Assert.HasCount(1, margins);
        Assert.AreEqual(("❌", "▲"), (margins[0][Diagnostics].Text, margins[0][Folds].Text));
    }

    [TestMethod]
    public async Task TheDiagnostics_AreAskedForOnceForTheWholeListing()
    {
        ServerFinds(Of(DiagnosticSeverity.Error, 0), Finding(1), Of(DiagnosticSeverity.Warning, 2));

        await ListedMarginsAsync();

        await _platform.Received(1).PullDiagnosticReportAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AServerThatCannotBeAsked_IsAListingWithNoBulbs()
    {
        _platform.PullDiagnosticReportAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ReplDiagnosticReport?>>(_ => throw new InvalidOperationException("the server is gone"));

        var margins = await ListedMarginsAsync();

        Assert.HasCount(3, margins);
        Assert.IsTrue(margins.All(margin => margin[Diagnostics].Text == " "));
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
        await diagnostics.Pending.WaitAsync(SafetyCap);

        await _platform.Received(1).PullDiagnosticReportAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        Assert.HasCount(1, diagnostics.Items);
    }
}
