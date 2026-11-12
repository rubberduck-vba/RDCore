using NSubstitute;
using RDCore.CLI;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// A program that was stopped - by a <c>STOP</c>, or by a break at the keyboard - lists the lines around the one it stopped at, with it marked as the statement
/// the program waits before; then says where, as BASIC has always done, in the lines the program was typed in.
/// </summary>
[TestClass]
public sealed class ReplBreakTests
{
    private readonly ReplProgram _program = new();
    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();
    private ReplCommandContext _context;

    public ReplBreakTests()
    {
        _platform.Provides<SessionDiscard>().Returns(true);
        _platform.Provides<SessionExecute>().Returns(true);
        _platform.SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DebugBreakpointsResult()));
        _context = new ReplCommandContext(_program, _console, _platform, new ReplDocument(_program, _platform), []);
    }

    private async Task RunAsync(ExecuteResult result)
    {
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(result));
        await new RunReplCommand().ExecuteAsync(_context, string.Empty, CancellationToken.None);
    }

    // a platform that debugs: RUN runs the program under its debugger, and a STOP, a breakpoint or a break at the keyboard leaves it waiting.
    private async Task RunUnderTheDebuggerAsync(ExecuteResult result)
    {
        _platform.Provides<ProgramDebugging>().Returns(true);
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));
        await new RunReplCommand().ExecuteAsync(_context, string.Empty, CancellationToken.None);
    }

    private void ResumeReturns(ExecuteResult result)
        => _platform.ResumeAsync(Arg.Any<StepKind?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(result));

    // the module's first line is the Sub statement, so the line at index i of the program is the line i + 1 of its source.
    private ExecuteResult InterruptedAt(int number) => new() { Outcome = ExecutionOutcome.Interrupted, ErrorLine = _program.SourceLineOf(number) };

    private ExecuteResult SuspendedAt(int number) => new() { Outcome = ExecutionOutcome.Suspended, ErrorLine = _program.SourceLineOf(number) };

    // the lines 10 to 70.
    private void StoreSevenLines()
    {
        foreach (var number in Enumerable.Range(1, 7).Select(n => n * 10))
        {
            _program.Store(number, $"X = {number}");
        }
    }

    private IEnumerable<NSubstitute.Core.ICall> ListingCalls()
        => _console.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine));

    // the number of each line listed, and how the line is marked, in the order they were written.
    private (int Number, ReplLineStyle Style)[] Listed()
        => [.. ListingCalls().Select(call =>
        {
            var arguments = call.GetArguments();
            var text = string.Concat(((IReadOnlyList<ReplTextRun>)arguments[1]!).Select(run => run.Text)).TrimStart();
            return (int.Parse(text[..text.IndexOf(' ')]), (ReplLineStyle)arguments[2]!);
        })];

    private int[] ListedNumbers() => [.. Listed().Select(line => line.Number)];

    [TestMethod]
    public async Task ABreak_SaysTheLineItStoppedAt_NotTheLineOfTheGeneratedSource()
    {
        _program.Store(10, "X = 1");
        _program.Store(20, "STOP");

        // the module's first line is the Sub statement, so the second line of the program is the third line of its source: index 2.
        await RunAsync(new ExecuteResult { Outcome = ExecutionOutcome.Interrupted, ErrorLine = 2 });

        _console.Received().WriteLine(string.Format(Resources.Repl_Break_InLine, 20));
    }

    [TestMethod]
    public async Task ABreak_ThatCouldNotBePlaced_IsABreakAllTheSame_AndListsNothing()
    {
        _program.Store(10, "X = 1");

        await RunAsync(new ExecuteResult { Outcome = ExecutionOutcome.Interrupted });

        _console.Received().WriteLine(Resources.Repl_Break);
        Assert.IsEmpty(ListedNumbers());
    }

    [TestMethod]
    public async Task AnEnd_SaysNothing_AsBasicDoesNot()
    {
        _program.Store(10, "END");

        await RunAsync(new ExecuteResult { Outcome = ExecutionOutcome.Halted });

        _console.DidNotReceiveWithAnyArgs().WriteLine(default(string)!);
        _console.DidNotReceiveWithAnyArgs().WriteMessage(default, default!, default!);
        _console.DidNotReceiveWithAnyArgs().WriteListingLine(default!, default!, default);
    }

    // ---- the lines a break lists ----

    [TestMethod]
    [DataRow(10, 2, new[] { 10, 20, 30 }, DisplayName = "at the start: none above")]
    [DataRow(20, 2, new[] { 10, 20, 30, 40 }, DisplayName = "one line above it")]
    [DataRow(40, 2, new[] { 20, 30, 40, 50, 60 }, DisplayName = "in the middle: two above and two below")]
    [DataRow(60, 2, new[] { 40, 50, 60, 70 }, DisplayName = "one line below it")]
    [DataRow(70, 2, new[] { 50, 60, 70 }, DisplayName = "at the end: none below")]
    [DataRow(40, 1, new[] { 30, 40, 50 }, DisplayName = "set to 1: one above and one below")]
    [DataRow(40, 10, new[] { 10, 20, 30, 40, 50, 60, 70 }, DisplayName = "set to more than the program has: the whole program")]
    public async Task ABreak_ListsAsManyLinesAboveAndBelowAsItIsSetTo_AsThereAre(int stoppedAt, int around, int[] expected)
    {
        StoreSevenLines();
        _context = _context with { BreakListingLines = around };

        await RunAsync(InterruptedAt(stoppedAt));

        CollectionAssert.AreEqual(expected, ListedNumbers());
    }

    [TestMethod]
    public async Task ABreak_ListsTwoLinesAboveAndBelow_ByDefault()
    {
        StoreSevenLines();

        await RunAsync(InterruptedAt(40));

        Assert.AreEqual(2, _context.BreakListingLines);
        CollectionAssert.AreEqual(new[] { 20, 30, 40, 50, 60 }, ListedNumbers());
    }

    [TestMethod]
    public async Task ABreak_SetToListNoLines_SaysWhereItStopped_AndListsNothing()
    {
        StoreSevenLines();
        _context = _context with { BreakListingLines = 0 };

        await RunAsync(InterruptedAt(40));

        _console.Received().WriteLine(string.Format(Resources.Repl_Break_InLine, 40));
        Assert.IsEmpty(ListedNumbers());
    }

    [TestMethod]
    public async Task ABreak_MarksTheLineItStoppedAt_AsTheCurrentStatement_AndNoOther()
    {
        StoreSevenLines();

        await RunAsync(InterruptedAt(40));

        foreach (var (number, style) in Listed())
        {
            Assert.AreEqual(number == 40 ? ReplLineStyle.CurrentStatement : ReplLineStyle.Plain, style, $"line {number}");
        }
    }

    // the code first, then what stopped it.
    private void AssertListedBeforeSaid(int number)
    {
        var calls = _console.ReceivedCalls().ToList();
        var said = calls.FindIndex(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteLine)
            && call.GetArguments() is [string text] && text == string.Format(Resources.Repl_Break_InLine, number));
        var listed = calls.FindLastIndex(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine));
        Assert.IsGreaterThanOrEqualTo(0, said, $"BREAK IN {number} was said");
        Assert.IsGreaterThanOrEqualTo(0, listed, "the lines were listed");
        Assert.IsLessThan(said, listed, "the listing comes before it");
    }

    [TestMethod]
    public async Task ABreak_ListsTheLines_BeforeItSaysWhereItStopped()
    {
        StoreSevenLines();

        await RunAsync(InterruptedAt(40));

        AssertListedBeforeSaid(40);
    }

    [TestMethod]
    public async Task AProgramThatWaits_ListsTheLines_BeforeItSaysWhereItWaits()
    {
        StoreSevenLines();

        await RunUnderTheDebuggerAsync(SuspendedAt(30));

        AssertListedBeforeSaid(30);
    }

    // under a debugger the program waits where it stopped: at a STOP, a breakpoint or a break at the keyboard, which the platform answers alike.
    [TestMethod]
    public async Task AProgramThatWaits_ListsTheLinesAroundTheStatementItWaitsBefore()
    {
        StoreSevenLines();

        await RunUnderTheDebuggerAsync(SuspendedAt(30));

        _console.Received().WriteLine(string.Format(Resources.Repl_Break_InLine, 30));
        CollectionAssert.AreEqual(new[] { 10, 20, 30, 40, 50 }, ListedNumbers());
        Assert.AreEqual(ReplLineStyle.CurrentStatement, Listed().Single(line => line.Number == 30).Style);
    }

    // a folded block is listed open: the lines around a break are what the program was doing. The block of lines 30 to 60 is the third line of the program to
    // the sixth; the program is a document of the language server, which says what folds.
    [TestMethod]
    public async Task ABreak_InAFoldedBlock_ListsItsLines()
    {
        StoreSevenLines();
        _platform.GetBlockFoldsAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ReplFold>>([new ReplFold(2, 5)]));
        var workspace = await ReplWorkspace.CreateAsync(new MockFileSystem(), Substitute.For<IProjectFileWriter>());
        _context = _context with { Document = new ReplDocument(_program, _platform, workspace) };
        _ = _context.Folds.Toggle(30);

        await RunAsync(InterruptedAt(40));

        CollectionAssert.AreEqual(new[] { 20, 30, 40, 50, 60 }, ListedNumbers());
    }

    [TestMethod]
    public async Task AContinuedProgramThatStopsAgain_ListsTheLinesAroundWhereItWaits()
    {
        StoreSevenLines();
        await RunUnderTheDebuggerAsync(SuspendedAt(20));
        _console.ClearReceivedCalls();
        ResumeReturns(SuspendedAt(60));

        await new ContReplCommand().ExecuteAsync(_context, string.Empty, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { 40, 50, 60, 70 }, ListedNumbers());
    }

    [TestMethod]
    public async Task AStep_SaysWhereItStopped_AndListsNothing()
    {
        StoreSevenLines();
        await RunUnderTheDebuggerAsync(SuspendedAt(20));
        _console.ClearReceivedCalls();
        ResumeReturns(SuspendedAt(30));

        await new StepReplCommand().ExecuteAsync(_context, string.Empty, CancellationToken.None);

        _console.Received().WriteLine(string.Format(Resources.Repl_Break_InLine, 30));
        Assert.IsEmpty(ListedNumbers());
    }
}
