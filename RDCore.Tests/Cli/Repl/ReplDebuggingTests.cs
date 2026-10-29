using NSubstitute;
using RDCore.CLI;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.Themes;
using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using Spectre.Console;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// A program run from the shell waits where a <c>STOP</c> or a breakpoint stops it, says where as BASIC does, and is gone on with by <c>CONT</c> and <c>STEP</c>;
/// <c>BREAK</c> sets the breakpoints and <c>LIST</c> shows them.
/// </summary>
[TestClass]
public sealed class ReplDebuggingTests
{
    private readonly ReplProgram _program = new();
    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();
    private readonly ReplCommandContext _context;

    public ReplDebuggingTests()
    {
        _platform.Provides<SessionExecute>().Returns(true);
        _platform.Provides<SessionDiscard>().Returns(true);
        _platform.Provides<ProgramDebugging>().Returns(true);
        _platform.SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugBreakpointsResult()));
        _context = new ReplCommandContext(_program, _console, _platform, new ReplDocument(_program, _platform), []);

        _program.Store(10, "X = 1");
        _program.Store(20, "STOP");
        _program.Store(30, "X = X + 1");
    }

    // the module's first line is the Sub statement, so the line numbered 10 is the line of the source at index 1.
    private static ExecuteSessionResult SuspendedAt(int sourceLine) => new() { Outcome = ExecutionOutcome.Suspended, ErrorLine = sourceLine };

    private void ExecuteReturns(ExecuteSessionResult result)
        => _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));

    private void ResumeReturns(ExecuteSessionResult result)
        => _platform.ResumeAsync(Arg.Any<StepKind?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(result));

    private Task RunAsync() => new RunReplCommand().ExecuteAsync(_context, string.Empty, CancellationToken.None);

    private Task CommandAsync(IReplCommand command, string arguments = "") => command.ExecuteAsync(_context, arguments, CancellationToken.None);

    private int[] BreakpointLines() => [.. _context.Debugger.Breakpoints.Select(breakpoint => breakpoint.LineNumber)];

    // the platform refuses every condition that is not an expression the way the language server does: by the breakpoint's place in the request.
    private void RefuseCondition(string condition, string reason)
        => _platform.SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new HostDebugBreakpointsResult
            {
                Breakpoints = [.. call.Arg<IReadOnlyList<SessionBreakpointLine>>().Select(breakpoint =>
                    new HostBreakpoint(breakpoint.Line, false, breakpoint.Condition == condition ? reason : null))],
            }));

    // ---- breakpoints the platform can say are wrong ----

    [TestMethod]
    public async Task ABreakpoint_OnALineTheLoadedProgramHasNoStatementOn_IsDropped_OnceTheProgramHasRun()
    {
        _ = _context.Debugger.ToggleBreakpoint(10);
        _ = _context.Debugger.ToggleBreakpoint(20);
        _platform.SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugBreakpointsResult
            {
                Breakpoints = [new HostBreakpoint(1, true), new HostBreakpoint(2, false)],
                Judged = true,
            }));
        ExecuteReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed });

        await RunAsync();

        CollectionAssert.AreEqual(new[] { 10 }, BreakpointLines());
    }

    [TestMethod]
    public async Task ABreakpoint_IsKept_WhileThePlatformHasNotJudgedIt()
    {
        _ = _context.Debugger.ToggleBreakpoint(10);
        _ = _context.Debugger.ToggleBreakpoint(20);
        _platform.SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugBreakpointsResult
            {
                Breakpoints = [new HostBreakpoint(1, false), new HostBreakpoint(2, false)],
                Judged = false,
            }));
        ExecuteReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed });

        await RunAsync();

        CollectionAssert.AreEqual(new[] { 10, 20 }, BreakpointLines());
    }

    [TestMethod]
    public async Task ABreakpoint_IsNotDropped_BeforeTheProgramHasRun()
    {
        _ = _context.Debugger.ToggleBreakpoint(10);
        _platform.SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugBreakpointsResult { Breakpoints = [new HostBreakpoint(1, false)], Judged = true }));

        await CommandAsync(new BreakReplCommand(), "20");

        CollectionAssert.AreEqual(new[] { 10, 20 }, BreakpointLines(), "what the platform holds is the program it ran last, not this one");
    }

    // ---- a program that waits ----

    [TestMethod]
    public async Task ARun_ThatWaitsAtABreakpointWhoseConditionFailed_SaysWhereAndWhatTheConditionRaised()
    {
        ExecuteReturns(SuspendedAt(2) with { ConditionError = "Division by zero" });

        await RunAsync();

        _console.Received().WriteLine(string.Format(Resources.Repl_Break_InLine, 20));
        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Warning, string.Format(Resources.Repl_Breakpoint_ConditionFailed, "Division by zero"));
        Assert.AreEqual(ReplDebugState.Suspended, _context.Debugger.State);
    }

    [TestMethod]
    public async Task ARunTimeError_InsideGoSubs_SaysInItsStackTraceWhichGoSubsItCameFrom()
    {
        // source lines 1..3 are program lines 10..30: the error at 30, in the GoSub of 20, which the GoSub of 10 called.
        ExecuteReturns(new ExecuteSessionResult
        {
            Outcome = ExecutionOutcome.RuntimeError,
            ErrorLine = 3,
            ErrorCode = "VBR00011",
            ErrorMessage = "Division by zero",
            StackTrace = [new ExecuteStackFrame("Main", 3, 0, [2, 1])],
        });

        await RunAsync();

        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Arg.Any<string>(),
            Arg.Is<string>(detail => detail.Contains("Main 30 from 20 from 10")));
    }

    [TestMethod]
    public async Task ARun_ThatStops_WaitsAndSaysWhere()
    {
        ExecuteReturns(SuspendedAt(2));

        await RunAsync();

        _console.Received().WriteLine(string.Format(Resources.Repl_Break_InLine, 20));
        Assert.AreEqual(ReplDebugState.Suspended, _context.Debugger.State);
        Assert.AreEqual(20, _context.Debugger.StoppedAt);
        await _platform.Received(1).ExecuteAsync(Arg.Any<string>(), "Program", "Main", true, false, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ARun_ThatCompletes_IsNotWaiting()
    {
        ExecuteReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed });

        await RunAsync();

        Assert.AreEqual(ReplDebugState.Idle, _context.Debugger.State);
    }

    [TestMethod]
    public async Task ARun_TellsThePlatformWhereTheBreakpointsAre_AsLinesOfTheModule()
    {
        _context.Debugger.ToggleBreakpoint(30);
        ExecuteReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed });

        await RunAsync();

        // before the program runs, and again once it has, to hear which of them the loaded program can have.
        await _platform.Received(2).SetBreakpointsAsync("Program", Arg.Is<IReadOnlyList<SessionBreakpointLine>>(lines => lines.SequenceEqual(new[] { new SessionBreakpointLine { Line = 3 } })), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ARun_OnAPlatformWithNoDebugger_IsARunLikeAnyOther()
    {
        _platform.Provides<ProgramDebugging>().Returns(false);
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExecuteSessionResult { Outcome = ExecutionOutcome.Interrupted, ErrorLine = 2 }));

        await RunAsync();

        _console.Received().WriteLine(string.Format(Resources.Repl_Break_InLine, 20));
        Assert.AreEqual(ReplDebugState.Idle, _context.Debugger.State);
    }

    // ---- CONT ----

    [TestMethod]
    public async Task ACont_GoesOnFromWhereTheProgramStopped()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        ResumeReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed, Output = ["2"] });

        await CommandAsync(new ContReplCommand());

        await _platform.Received(1).ResumeAsync(null, Arg.Any<CancellationToken>());
        await _platform.DidNotReceiveWithAnyArgs().GotoAsync(default!, default);
        _console.Received().WriteLine("2");
        Assert.AreEqual(ReplDebugState.Idle, _context.Debugger.State);
    }

    [TestMethod]
    public async Task ACont_WithALine_MovesTheProgramThereFirst()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        _platform.GotoAsync("30", Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugGotoResult { Moved = true, Line = 3 }));
        ResumeReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed });

        await CommandAsync(new ContReplCommand(), "30");

        Received.InOrder(() =>
        {
            _platform.GotoAsync("30", Arg.Any<CancellationToken>());
            _platform.ResumeAsync(null, Arg.Any<CancellationToken>());
        });
    }

    [TestMethod]
    public async Task ACont_WithALineThatIsNotInTheProgram_IsAnUndefinedLine_AndTheProgramStillWaits()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();

        await CommandAsync(new ContReplCommand(), "35");

        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Resources.Repl_UndefinedLine, "35");
        await _platform.DidNotReceiveWithAnyArgs().ResumeAsync(default, default);
        Assert.AreEqual(ReplDebugState.Suspended, _context.Debugger.State);
    }

    [TestMethod]
    public async Task ACont_WhenThePlatformWillNotMoveTheProgram_DoesNotGoOn()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        _platform.GotoAsync("30", Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugGotoResult { Reason = "nope" }));

        await CommandAsync(new ContReplCommand(), "30");

        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Resources.Repl_CantContinue, "nope");
        await _platform.DidNotReceiveWithAnyArgs().ResumeAsync(default, default);
    }

    [TestMethod]
    public async Task ACont_WithNoProgramThatWaits_CannotContinue()
    {
        await CommandAsync(new ContReplCommand());

        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Resources.Repl_CantContinue, null);
        await _platform.DidNotReceiveWithAnyArgs().ResumeAsync(default, default);
    }

    [TestMethod]
    public async Task ACont_ThatTheProgramStopsAgain_SaysWhereAgain()
    {
        ExecuteReturns(SuspendedAt(1));
        await RunAsync();
        ResumeReturns(SuspendedAt(3));

        await CommandAsync(new ContReplCommand());

        _console.Received().WriteLine(string.Format(Resources.Repl_Break_InLine, 30));
        Assert.AreEqual(30, _context.Debugger.StoppedAt);
    }

    [TestMethod]
    public async Task ACont_ThatIsRefused_LeavesTheProgramWhereItWaited()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        ResumeReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Refused, ErrorMessage = "run it again" });

        await CommandAsync(new ContReplCommand());

        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Resources.Repl_CantContinue, "run it again");
        Assert.AreEqual(ReplDebugState.Suspended, _context.Debugger.State);
        Assert.AreEqual(20, _context.Debugger.StoppedAt);
    }

    // ---- STEP ----

    [TestMethod]
    [DataRow("", StepKind.Over)]
    [DataRow("over", StepKind.Over)]
    [DataRow("INTO", StepKind.Into)]
    [DataRow("out", StepKind.Out)]
    public async Task AStep_GoesAsFarAsItIsAskedTo(string arguments, StepKind expected)
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        ResumeReturns(SuspendedAt(3));

        await CommandAsync(new StepReplCommand(), arguments);

        await _platform.Received(1).ResumeAsync(expected, Arg.Any<CancellationToken>());
        _console.Received().WriteLine(string.Format(Resources.Repl_Break_InLine, 30));
    }

    [TestMethod]
    public async Task AStep_OfSomethingElse_IsASyntaxError()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();

        await CommandAsync(new StepReplCommand(), "sideways");

        await _platform.DidNotReceiveWithAnyArgs().ResumeAsync(default, default);
    }

    // ---- BREAK ----

    [TestMethod]
    public async Task ABreak_TogglesTheBreakpointOfALine()
    {
        await CommandAsync(new BreakReplCommand(), "30");
        CollectionAssert.AreEqual(new[] { 30 }, BreakpointLines());
        _console.Received().WriteLine(string.Format(Resources.Repl_Breakpoint_Set, 30));

        await CommandAsync(new BreakReplCommand(), "30");
        Assert.IsEmpty(_context.Debugger.Breakpoints);
        _console.Received().WriteLine(string.Format(Resources.Repl_Breakpoint_Cleared, 30));
    }

    [TestMethod]
    public async Task ABreak_OnALineThatIsNotInTheProgram_IsAnUndefinedLine()
    {
        await CommandAsync(new BreakReplCommand(), "99");

        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Resources.Repl_UndefinedLine, "99");
        Assert.IsEmpty(_context.Debugger.Breakpoints);
    }

    [TestMethod]
    public async Task ABreak_WithNoLine_ListsTheBreakpoints()
    {
        await CommandAsync(new BreakReplCommand());
        _console.Received().WriteLine(Resources.Repl_Breakpoint_None);

        await CommandAsync(new BreakReplCommand(), "10");
        await CommandAsync(new BreakReplCommand(), "30");
        _console.ClearReceivedCalls();
        await CommandAsync(new BreakReplCommand());

        CollectionAssert.AreEqual(new[] { "● 10", "● 30" }, ListedBreakpoints(), "one a line, marked as LIST marks it");
    }

    private static int TypeIndex(string name) => SemanticTokenLegend.TokenTypes.ToList().IndexOf(name);

    // the runs of the lines BREAK's listing wrote.
    private IReadOnlyList<ReplTextRun>[] ListedRuns()
        => [.. _console.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteLine) && call.GetArguments()[0] is IReadOnlyList<ReplTextRun>)
            .Select(call => (IReadOnlyList<ReplTextRun>)call.GetArguments()[0]!)];

    [TestMethod]
    public async Task ABreakListing_ColoursTheConditionsOfAllTheBreakpoints_WithOneRequest()
    {
        _platform.Provides<SessionSemanticTokens>().Returns(true);
        _platform.GetFragmentTokensAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<IReadOnlyList<ReplSemanticToken>>>(
            [
                [new ReplSemanticToken(0, 0, 1, TypeIndex("variable"), 0), new ReplSemanticToken(0, 4, 1, TypeIndex("number"), 0)],
                [new ReplSemanticToken(0, 0, 3, TypeIndex("variable"), SemanticTokenLegend.ReadOnly)],
            ]));
        await CommandAsync(new BreakReplCommand(), "10 X > 1");
        await CommandAsync(new BreakReplCommand(), "30 Max");
        _console.ClearReceivedCalls();

        await CommandAsync(new BreakReplCommand());

        await _platform.Received(1).GetFragmentTokensAsync(Arg.Any<string>(), "Program", Arg.Is<IReadOnlyList<string>>(sent => sent.SequenceEqual(new[] { "X > 1", "Max" })), Arg.Any<CancellationToken>());
        var first = ListedRuns()[0];
        CollectionAssert.AreEqual(new[] { "X", " > ", "1" }, first.Skip(4).Select(run => run.Text).ToArray());
        Assert.AreEqual(ReplTextStyle.Identifier, first[4].Style);
        Assert.AreEqual(ReplTextStyle.Number, first[^1].Style);
        Assert.AreEqual(ReplTextStyle.IdentifierConst, ListedRuns()[1][^1].Style, "a constant is the way a listing shows one");
    }

    [TestMethod]
    public async Task ABreakListing_IsPlain_WhereThePlatformDoesNotColourFragments()
    {
        await CommandAsync(new BreakReplCommand(), "10 X > 1");
        _console.ClearReceivedCalls();

        await CommandAsync(new BreakReplCommand());

        await _platform.DidNotReceiveWithAnyArgs().GetFragmentTokensAsync(default!, default!, default!, default);
        CollectionAssert.AreEqual(new[] { "◆ 10 X > 1" }, ListedBreakpoints(), "the glyph of a breakpoint that waits on a condition");
    }

    [TestMethod]
    public async Task ABreakListing_IsPlain_WhenThePlatformCannotAnswer()
    {
        _platform.Provides<SessionSemanticTokens>().Returns(true);
        _platform.GetFragmentTokensAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<IReadOnlyList<ReplSemanticToken>>>>(_ => throw new InvalidOperationException("no answer"));
        await CommandAsync(new BreakReplCommand(), "10 X > 1");
        _console.ClearReceivedCalls();

        await CommandAsync(new BreakReplCommand());

        CollectionAssert.AreEqual(new[] { "◆ 10 X > 1" }, ListedBreakpoints());
    }

    // the lines of runs the console was written: BREAK's listing of the breakpoints.
    private string[] ListedBreakpoints()
        => [.. _console.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteLine) && call.GetArguments()[0] is IReadOnlyList<ReplTextRun>)
            .Select(call => string.Concat(((IReadOnlyList<ReplTextRun>)call.GetArguments()[0]!).Select(run => run.Text)))];

    [TestMethod]
    public async Task ABreakClear_RemovesThemAll()
    {
        await CommandAsync(new BreakReplCommand(), "10");
        await CommandAsync(new BreakReplCommand(), "30");

        await CommandAsync(new BreakReplCommand(), "clear");

        Assert.IsEmpty(_context.Debugger.Breakpoints);
    }

    [TestMethod]
    public async Task ABreakpoint_OnALineThatWasDeleted_GoesWithIt()
    {
        await CommandAsync(new BreakReplCommand(), "30");
        _program.Delete(30);

        await _context.Debugger.PushBreakpointsAsync(_context, CancellationToken.None);

        Assert.IsEmpty(_context.Debugger.Breakpoints);
    }

    // ---- conditions and BREAK TOGGLE ----

    [TestMethod]
    public async Task ABreak_WithAnExpressionAfterTheLine_SetsABreakpointThatWaitsOnIt_AsItWasTyped()
    {
        await CommandAsync(new BreakReplCommand(), "30 X > 1  And  S = \"a  b\"");

        Assert.IsTrue(_context.Debugger.TryGetBreakpoint(30, out var breakpoint));
        Assert.AreEqual("X > 1  And  S = \"a  b\"", breakpoint.ConditionExpression, "the spaces in a string literal are the string's");
        await _platform.Received().SetBreakpointsAsync("Program", Arg.Is<IReadOnlyList<SessionBreakpointLine>>(sent =>
            sent.Single().Condition == breakpoint.ConditionExpression && sent.Single().IsEnabled), Arg.Any<CancellationToken>());
        _console.Received().WriteLine(string.Format(Resources.Repl_Breakpoint_Set, 30));
    }

    [TestMethod]
    public async Task ABreak_WithACondition_ReplacesTheBreakpointTheLineHad_RatherThanClearingIt()
    {
        await CommandAsync(new BreakReplCommand(), "30");
        await CommandAsync(new BreakReplCommand(), "30 X = 2");

        Assert.IsTrue(_context.Debugger.TryGetBreakpoint(30, out var breakpoint));
        Assert.AreEqual("X = 2", breakpoint.ConditionExpression);
    }

    [TestMethod]
    public async Task ABreak_WhoseConditionIsRefused_SetsNoBreakpoint_AndSaysWhy()
    {
        RefuseCondition("X >", "expected an expression");

        await CommandAsync(new BreakReplCommand(), "30 X >");

        Assert.IsEmpty(_context.Debugger.Breakpoints);
        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, string.Format(Resources.Repl_Breakpoint_ConditionRefused, 30, "expected an expression"));
        _console.DidNotReceive().WriteLine(string.Format(Resources.Repl_Breakpoint_Set, 30));
    }

    [TestMethod]
    public async Task ABreak_WhoseConditionIsRefused_LeavesTheLineWithTheBreakpointItHad()
    {
        await CommandAsync(new BreakReplCommand(), "30 X = 2");
        RefuseCondition("X >", "expected an expression");

        await CommandAsync(new BreakReplCommand(), "30 X >");

        Assert.IsTrue(_context.Debugger.TryGetBreakpoint(30, out var breakpoint));
        Assert.AreEqual("X = 2", breakpoint.ConditionExpression);
    }

    [TestMethod]
    public async Task ABreakToggle_DisablesTheBreakpointOfALine_AndEnablesItAgain()
    {
        await CommandAsync(new BreakReplCommand(), "30 X = 2");

        await CommandAsync(new BreakReplCommand(), "TOGGLE 30");
        Assert.IsTrue(_context.Debugger.TryGetBreakpoint(30, out var disabled));
        Assert.IsFalse(disabled.Enabled);
        Assert.AreEqual("X = 2", disabled.ConditionExpression, "a disabled breakpoint keeps its condition");
        _console.Received().WriteLine(string.Format(Resources.Repl_Breakpoint_Disabled, 30));
        await _platform.Received().SetBreakpointsAsync("Program", Arg.Is<IReadOnlyList<SessionBreakpointLine>>(sent => !sent.Single().IsEnabled), Arg.Any<CancellationToken>());

        await CommandAsync(new BreakReplCommand(), "toggle 30");
        Assert.IsTrue(_context.Debugger.TryGetBreakpoint(30, out var enabled));
        Assert.IsTrue(enabled.Enabled);
        _console.Received().WriteLine(string.Format(Resources.Repl_Breakpoint_Enabled, 30));
    }

    [TestMethod]
    public async Task ABreakToggle_OnALineWithNoBreakpoint_SetsOne()
    {
        await CommandAsync(new BreakReplCommand(), "TOGGLE 30");

        Assert.IsTrue(_context.Debugger.TryGetBreakpoint(30, out var breakpoint));
        Assert.IsTrue(breakpoint.Enabled);
        Assert.IsNull(breakpoint.ConditionExpression);
        _console.Received().WriteLine(string.Format(Resources.Repl_Breakpoint_Set, 30));
    }

    [TestMethod]
    public async Task ABreakToggle_OnALineThatIsNotInTheProgram_IsAnUndefinedLine()
    {
        await CommandAsync(new BreakReplCommand(), "TOGGLE 99");

        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Resources.Repl_UndefinedLine, "99");
        Assert.IsEmpty(_context.Debugger.Breakpoints);
    }

    [TestMethod]
    public async Task ABreak_WithNoLine_ListsTheConditionsAndWhichAreDisabled()
    {
        await CommandAsync(new BreakReplCommand(), "10");
        await CommandAsync(new BreakReplCommand(), "20 X > 1");
        await CommandAsync(new BreakReplCommand(), "TOGGLE 30");
        await CommandAsync(new BreakReplCommand(), "TOGGLE 30");
        _console.ClearReceivedCalls();

        await CommandAsync(new BreakReplCommand());

        // a condition makes the diamond, and a disabled breakpoint is the hollow one: the glyph says it.
        CollectionAssert.AreEqual(new[] { "● 10", "◆ 20 X > 1", "○ 30" }, ListedBreakpoints());
    }

    [TestMethod]
    public async Task TheListedBreakpoints_HaveTheirGlyphInTheMarksStyle_AndTheirLineInTheNumberStyle()
    {
        await CommandAsync(new BreakReplCommand(), "10");
        _console.ClearReceivedCalls();

        await CommandAsync(new BreakReplCommand());

        var runs = (IReadOnlyList<ReplTextRun>)_console.ReceivedCalls().Single(call => call.GetArguments()[0] is IReadOnlyList<ReplTextRun>).GetArguments()[0]!;
        Assert.AreEqual(ReplTextStyle.BreakpointGlyph, runs[0].Style);
        Assert.AreEqual(new ReplTextRun("10", ReplTextStyle.Number), runs[2]);
    }

    [TestMethod]
    public async Task ARun_DropsABreakpointWhoseConditionIsRefused_AndSaysWhy()
    {
        _context.Debugger.SetBreakpoint(new Breakpoint(30, ConditionExpression: "X >"));
        RefuseCondition("X >", "expected an expression");
        ExecuteReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed });

        await RunAsync();

        Assert.IsEmpty(_context.Debugger.Breakpoints);
        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, string.Format(Resources.Repl_Breakpoint_ConditionRefused, 30, "expected an expression"));
    }

    // ---- clearing the program ----

    [TestMethod]
    public async Task ANew_EndsTheProgramThatWaits_AndTakesTheBreakpointsWithTheProgram()
    {
        ExecuteReturns(SuspendedAt(2));
        await CommandAsync(new BreakReplCommand(), "30");
        await RunAsync();

        await CommandAsync(new NewReplCommand());

        await _platform.Received().DiscardAsync("Program", true, Arg.Any<CancellationToken>());
        Assert.AreEqual(ReplDebugState.Idle, _context.Debugger.State);
        Assert.IsEmpty(_context.Debugger.Breakpoints);
    }

    // ---- STACK and VARS ----

    [TestMethod]
    public async Task AStack_SaysWhereEachActivationIs_InTheLinesTheProgramWasTypedIn()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        _platform.GetStackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugStackResult
        {
            Frames = [new HostStackFrame(0, "Helper", "Other", 6, 0), new HostStackFrame(1, "Main", "Program", 2, 0)],
        }));

        await CommandAsync(new StackReplCommand());

        _console.Received().WriteLine("#0 Helper (Other line 7)");
        _console.Received().WriteLine("#1 Main 20");
    }

    [TestMethod]
    public async Task AStack_SaysHowAnActivationGotWhereItIs_WhenItIsInsideGoSubs()
    {
        ExecuteReturns(SuspendedAt(3));
        await RunAsync();
        _platform.GetStackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugStackResult
        {
            // waits before line 30, called by the GoSub of line 20, which the GoSub of line 10 called.
            Frames = [new HostStackFrame(0, "Main", "Program", 3, 0, [2, 1]), new HostStackFrame(1, "Caller", "Other", 4, 0)],
        }));

        await CommandAsync(new StackReplCommand());

        _console.Received().WriteLine("#0 Main 30 from 20 from 10");
        _console.Received().WriteLine("#1 Caller (Other line 5)");
    }

    [TestMethod]
    public async Task AStack_SaysWhichHandlerAnActivationIsRunning_BeforeHowItGotThere()
    {
        ExecuteReturns(SuspendedAt(3));
        await RunAsync();
        _platform.GetStackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugStackResult
        {
            Frames = [new HostStackFrame(0, "Main", "Program", 3, 0, [1], Handler: "900")],
        }));

        await CommandAsync(new StackReplCommand());

        _console.Received().WriteLine("#0 Main 30 in 900 from 10");
    }

    [TestMethod]
    public async Task AStack_OfAnActivationInNoGoSub_SaysNothingOfHowItGotThere()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        _platform.GetStackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugStackResult
        {
            Frames = [new HostStackFrame(0, "Main", "Program", 2, 0)],
        }));

        await CommandAsync(new StackReplCommand());

        _console.Received().WriteLine("#0 Main 20");
        _console.DidNotReceive().WriteLine(Arg.Is<string>(line => line.Contains("from")));
    }

    [TestMethod]
    public async Task AStack_OfNoProgramThatWaits_SaysSo()
    {
        await CommandAsync(new StackReplCommand());
        await CommandAsync(new VarsReplCommand());

        _console.Received(2).WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Warning, Resources.Repl_NotStopped, null);
        await _platform.DidNotReceiveWithAnyArgs().GetStackAsync(default);
    }

    [TestMethod]
    public async Task AVars_ShowsTheModuleVariablesThenTheLocals_InTheOrderTheyAreDeclaredIn_WithTheirValuesLinedUp()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        _platform.GetVariablesAsync(0, HostVariableScope.Locals, 0, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugVariablesResult
        {
            Variables = [new HostVariable("k", "7", "Long")],
        }));
        _platform.GetVariablesAsync(0, HostVariableScope.Module, 0, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugVariablesResult
        {
            Variables = [new HostVariable("X", "42", "Variant/Long"), new HostVariable("Name", "\"hi\"", "String")],
        }));

        await CommandAsync(new VarsReplCommand());

        Received.InOrder(() =>
        {
            _console.WriteLine("X    = 42  (Variant/Long)");
            _console.WriteLine("Name = \"hi\"  (String)");
            _console.WriteLine("k    = 7  (Long)");
        });
    }

    // ---- FRAME ----

    [TestMethod]
    public async Task AFrame_SelectsTheActivationVarsIsAbout()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        _platform.GetStackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugStackResult
        {
            Frames = [new HostStackFrame(0, "Helper", "Program", 2, 0), new HostStackFrame(1, "Main", "Program", 1, 0)],
        }));
        _platform.GetVariablesAsync(1, Arg.Any<HostVariableScope>(), Arg.Is(0), Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugVariablesResult()));

        await CommandAsync(new FrameReplCommand(), "1");
        await CommandAsync(new VarsReplCommand());

        Assert.AreEqual(1, _context.Debugger.SelectedFrame);
        _console.Received().WriteLine("#1 Main");
        await _platform.Received().GetVariablesAsync(1, HostVariableScope.Module, 0, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AFrame_ThatIsNotOnTheStack_IsASyntaxError_AndTheSelectionStays()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        _platform.GetStackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugStackResult { Frames = [new HostStackFrame(0, "Main", "Program", 1, 0)] }));

        await CommandAsync(new FrameReplCommand(), "3");

        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Resources.Repl_SyntaxError, "3");
        Assert.AreEqual(0, _context.Debugger.SelectedFrame);
    }

    [TestMethod]
    public async Task TheSelectedFrame_IsTheInnermostAgain_WhenTheProgramGoesOn()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        _context.Debugger.SelectedFrame = 2;
        ResumeReturns(SuspendedAt(3));

        await CommandAsync(new ContReplCommand());

        Assert.AreEqual(0, _context.Debugger.SelectedFrame);
    }

    [TestMethod]
    public async Task AVars_ShowsThePartsOfAnArray_UnderIt()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        _platform.GetVariablesAsync(0, HostVariableScope.Locals, 0, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugVariablesResult
        {
            Variables = [new HostVariable("arr", string.Empty, "Long(1 To 2)", Reference: 3)],
        }));
        _platform.GetVariablesAsync(0, HostVariableScope.Module, 0, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugVariablesResult()));
        _platform.GetVariablesAsync(Arg.Any<int>(), Arg.Any<HostVariableScope>(), 3, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugVariablesResult
        {
            Variables = [new HostVariable("(1)", "5", "Long"), new HostVariable("(2)", "6", "Long")],
        }));

        await CommandAsync(new VarsReplCommand());

        Received.InOrder(() =>
        {
            _console.WriteLine("arr  (Long(1 To 2))");
            _console.WriteLine("  (1) = 5  (Long)");
            _console.WriteLine("  (2) = 6  (Long)");
        });
    }

    [TestMethod]
    public async Task AVars_WithAnActivation_AsksForThatOne()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        _platform.GetVariablesAsync(Arg.Any<int>(), Arg.Any<HostVariableScope>(), Arg.Is(0), Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugVariablesResult()));

        await CommandAsync(new VarsReplCommand(), "1");

        await _platform.Received(1).GetVariablesAsync(1, HostVariableScope.Locals, 0, Arg.Any<CancellationToken>());
        await _platform.Received(1).GetVariablesAsync(1, HostVariableScope.Module, 0, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AVars_OfSomethingThatIsNotAnActivation_IsASyntaxError()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();

        await CommandAsync(new VarsReplCommand(), "up");

        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Resources.Repl_SyntaxError, "up");
        await _platform.DidNotReceiveWithAnyArgs().GetVariablesAsync(default, default, default, default);
    }

    [TestMethod]
    public async Task ARun_WhileTheProgramWaits_EndsItAndStartsOverFromTheTop()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();

        await RunAsync();

        await _platform.Received().DiscardAsync("Program", true, Arg.Any<CancellationToken>());
        await _platform.Received(2).ExecuteAsync(Arg.Any<string>(), "Program", "Main", true, false, Arg.Any<CancellationToken>());
    }

    // ---- a statement typed while the program waits ----

    private void ExecuteInFrameReturns(HostDebugEvaluateResult result)
        => _platform.ExecuteInFrameAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(result));

    [TestMethod]
    public async Task AStatement_TypedWhileTheProgramWaits_RunsInTheActivationItWaitsIn_AndTheProgramWaitsStill()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        ExecuteInFrameReturns(new HostDebugEvaluateResult { Success = true, Output = ["1"] });

        await ReplExecution.ExecuteAsync(_context, "src", ReplProgram.ImmediateEntryPointName, CancellationToken.None, statement: "?v");

        await _platform.Received(1).ExecuteInFrameAsync(0, "?v", Arg.Any<CancellationToken>());
        await _platform.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), "Immediate", Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        _console.Received().WriteLine("1");
        Assert.AreEqual(ReplDebugState.Suspended, _context.Debugger.State);
        Assert.AreEqual(20, _context.Debugger.StoppedAt);
    }

    [TestMethod]
    public async Task AStatement_TypedWhileTheProgramWaits_RunsInTheActivationFrameSelected()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        _context.Debugger.SelectedFrame = 1;
        ExecuteInFrameReturns(new HostDebugEvaluateResult { Success = true });

        await ReplExecution.ExecuteAsync(_context, "src", ReplProgram.ImmediateEntryPointName, CancellationToken.None, statement: "X = 2");

        await _platform.Received(1).ExecuteInFrameAsync(1, "X = 2", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AStatement_ThatCannotRunWhereTheProgramWaits_SaysWhy_AndTheProgramWaitsStill()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        ExecuteInFrameReturns(new HostDebugEvaluateResult { Error = "'nowhere' is not defined" });

        await ReplExecution.ExecuteAsync(_context, "src", ReplProgram.ImmediateEntryPointName, CancellationToken.None, statement: "?nowhere");

        _console.Received().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, "'nowhere' is not defined");
        Assert.AreEqual(ReplDebugState.Suspended, _context.Debugger.State);
    }

    [TestMethod]
    public async Task AnEnd_TypedWhileTheProgramWaits_EndsTheProgram()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        ExecuteInFrameReturns(new HostDebugEvaluateResult { Error = "ended", EndedTheProgram = true });

        await ReplExecution.ExecuteAsync(_context, "src", ReplProgram.ImmediateEntryPointName, CancellationToken.None, statement: "END");

        Assert.AreEqual(ReplDebugState.Idle, _context.Debugger.State);
    }

    [TestMethod]
    public async Task AStatement_TypedWhenNoProgramWaits_IsARunLikeAnyOther()
    {
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed }));

        await ReplExecution.ExecuteAsync(_context, "src", ReplProgram.ImmediateEntryPointName, CancellationToken.None, statement: "?v");

        await _platform.Received(1).ExecuteAsync("src", "Program", "Immediate", Arg.Any<CancellationToken>());
        await _platform.DidNotReceiveWithAnyArgs().ExecuteInFrameAsync(default, default!, default);
    }

    // ---- LIST ----

    private (string Text, ReplTextRun Margin, ReplLineStyle Style) Listed(int number)
    {
        var calls = _console.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine)).ToArray();
        foreach (var call in calls)
        {
            var arguments = call.GetArguments();
            var runs = (IReadOnlyList<ReplTextRun>)arguments[1]!;
            var text = string.Concat(runs.Select(run => run.Text));
            if (text.TrimStart().StartsWith(number + " "))
            {
                // the breakpoint's lane is the first of the margin, at the edge.
                return (text, ((IReadOnlyList<ReplTextRun>)arguments[0]!)[0], (ReplLineStyle)arguments[2]!);
            }
        }

        throw new AssertFailedException($"line {number} was not listed");
    }

    private IAppThemeService GetMockThemeService()
    {
        var themeService = Substitute.For<IAppThemeService>();
        var theme = new AppTheme(new ThemeDocument
        {
            Debug = new()
            {
                BreakpointGlyph = "red",
            }
        });
        themeService.Theme.Returns(theme);
        return themeService;
    }

    [TestMethod]
    public async Task AListing_HasAMarginOfOneCharacter_AndAMarkInItForABreakpoint()
    {
        await CommandAsync(new BreakReplCommand(), "20");
        await CommandAsync(new ListReplCommand(GetMockThemeService()));

        var plain = Listed(10);
        Assert.AreEqual(" ", plain.Margin.Text);
        Assert.AreEqual(ReplLineStyle.Plain, plain.Style);

        var marked = Listed(20);
        Assert.AreEqual("●", marked.Margin.Text);
        Assert.AreEqual(ReplTextStyle.BreakpointGlyph, marked.Margin.Style);
        Assert.AreEqual(ReplLineStyle.Breakpoint, marked.Style);
    }

    [TestMethod]
    public async Task ADisabledBreakpoint_IsItsMarkInTheMargin_AndTheLineIsNotMarked()
    {
        await CommandAsync(new BreakReplCommand(), "20");
        await CommandAsync(new BreakReplCommand(), "TOGGLE 20");
        await CommandAsync(new BreakReplCommand(), "10 X > 1");
        await CommandAsync(new BreakReplCommand(), "TOGGLE 10");
        await CommandAsync(new ListReplCommand(GetMockThemeService()));

        foreach (var number in new[] { 10, 20 })
        {
            var line = Listed(number);
            Assert.AreNotEqual(" ", line.Margin.Text, "the mark of the disabled breakpoint is in the margin");
            Assert.AreEqual(ReplLineStyle.Plain, line.Style, "the program goes past a disabled breakpoint: the line is not marked");
        }
    }

    [TestMethod]
    public async Task AListing_MarksTheStatementTheProgramWaitsBefore_AsAWhole()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();

        await CommandAsync(new ListReplCommand(GetMockThemeService()));

        Assert.AreEqual(ReplLineStyle.CurrentStatement, Listed(20).Style);
        Assert.AreEqual(ReplLineStyle.Plain, Listed(10).Style);
    }

    [TestMethod]
    public async Task AListing_OfTheLineTheProgramWaitsBeforeThatHasABreakpoint_ShowsBoth()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        await CommandAsync(new BreakReplCommand(), "20");

        await CommandAsync(new ListReplCommand(GetMockThemeService()));

        var line = Listed(20);
        Assert.AreEqual("●", line.Margin.Text);
        Assert.AreEqual(ReplLineStyle.CurrentStatement, line.Style, "the statement that is about to run is what the line is marked as; the mark in the margin is the breakpoint");
    }

    [TestMethod]
    public async Task AListing_ThatTheProgramNoLongerWaitsAt_HasNoCurrentStatement()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        ResumeReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed });
        await CommandAsync(new ContReplCommand());

        await CommandAsync(new ListReplCommand(GetMockThemeService()));

        Assert.AreEqual(ReplLineStyle.Plain, Listed(20).Style);
    }

    // ---- what is written ----

    private static string Render(AppTheme theme, Action<ReplConsole> write)
    {
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.Yes, ColorSystem = ColorSystemSupport.TrueColor, Out = new AnsiConsoleOutput(output) });
        var themes = Substitute.For<IAppThemeService>();
        themes.Theme.Returns(theme);
        write(new ReplConsole(Substitute.For<IConsoleMessageWriter>(), console, themes));
        return output.ToString();
    }

    private static AppTheme Dark() => new(new AppThemeLoaderService(new MockFileSystem()).LoadBuiltInDefault());

    [TestMethod]
    public void ALineWithABreakpoint_IsWhiteOnRed_ToTheEndOfTheLine_AndTheMarkIsOutsideIt()
    {
        var output = Render(Dark(), console => console.WriteListingLine(
            [new ReplTextRun("●", ReplTextStyle.BreakpointGlyph)], [new ReplTextRun(" 20 STOP", ReplTextStyle.Plain)], ReplLineStyle.Breakpoint));

        StringAssert.Contains(output, "●");
        StringAssert.Contains(output, "48;2;181;26;23", "the background of the breakpoint line");
        StringAssert.Contains(output, "38;2;255;255;255", "and its foreground");
        Assert.IsLessThan(output.IndexOf("48;2;181;26;23", StringComparison.Ordinal), output.IndexOf("●", StringComparison.Ordinal), "the mark comes before the marking");
    }

    [TestMethod]
    public void ALineThatIsPlain_HasNoBackground()
    {
        var output = Render(Dark(), console => console.WriteListingLine(
            [new ReplTextRun(" ", ReplTextStyle.Plain)], [new ReplTextRun(" 10 X = 1", ReplTextStyle.Plain)], ReplLineStyle.Plain));

        StringAssert.Contains(output, "10 X = 1");
        Assert.DoesNotContain("48;2;", output);
    }

    // syntax colours on a marking's background are hard to read: a marked line is written in the marking's foreground, as VBE draws it.
    [TestMethod]
    [DataRow(ReplLineStyle.CurrentStatement, "48;2;216;176;100", "38;2;0;0;0", DisplayName = "the current statement: black on #d8b064")]
    [DataRow(ReplLineStyle.Breakpoint, "48;2;181;26;23", "38;2;255;255;255", DisplayName = "a breakpoint: white on #b51a17")]
    public void AMarkedLine_IsWrittenInTheMarkingsColours_NotInItsSyntaxColours(ReplLineStyle style, string background, string foreground)
    {
        var output = Render(Dark(), console => console.WriteListingLine(
            [new ReplTextRun(" ", ReplTextStyle.Plain)],
            [new ReplTextRun(" 20 ", ReplTextStyle.Plain), new ReplTextRun("STOP", ReplTextStyle.Keyword)], style));

        StringAssert.Contains(output, background);
        StringAssert.Contains(output, foreground);
        Assert.IsFalse(output.Contains("38;2;62;97;255", StringComparison.Ordinal), "the keyword's blue is not written on the marking");
    }

    // ---- edits made while the program waits ----

    private static readonly HostDebugEdit[] Pending =
    [
        new() { Name = "Program.Callee", Kind = HostDebugEditKind.Restart, Reason = "the signature of a procedure the program is in changed" },
        new() { Name = "Program.Main", Kind = HostDebugEditKind.NotYet, Reason = "the code of a procedure the program is in is not applied to a program that waits yet" },
        new() { Name = "Program", Kind = HostDebugEditKind.DoesNotCompile, Reason = "'y' is not defined" },
    ];

    private void EditStatusReturns(bool suspended, params HostDebugEdit[] edits)
        => _platform.GetEditStatusAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HostDebugEditStatusResult { Suspended = suspended, Edits = [.. edits] }));

    private async Task WaitAtTheStopAsync()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
    }

    private void SaysTheEditsAreNotApplied(string headline)
    {
        _console.Received(1).WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, headline, Resources.Repl_EditsNotApplied);
        foreach (var edit in Pending)
        {
            _console.Received(1).WriteLine($"  {edit.Name}: {edit.Reason}");
        }

        _console.Received(1).WriteLine(Resources.Repl_RunToStartAgain);
        Assert.AreEqual(ReplDebugState.Suspended, _context.Debugger.State, "the program waits where it did");
        Assert.AreEqual(20, _context.Debugger.StoppedAt);
    }

    [TestMethod]
    public async Task ACont_RefusedForTheEditsMadeWhileTheProgramWaited_NamesEachWithWhy_AndSaysRunStartsAgain()
    {
        await WaitAtTheStopAsync();
        ResumeReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Refused, ErrorMessage = "Program.Callee, Program.Main, Program" });
        EditStatusReturns(suspended: true, Pending);

        await CommandAsync(new ContReplCommand());

        SaysTheEditsAreNotApplied(Resources.Repl_CantContinue);
        await _platform.Received(1).GetEditStatusAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("INTO")]
    [DataRow("OUT")]
    public async Task AStep_RefusedForTheEditsMadeWhileTheProgramWaited_NamesEachWithWhy(string arguments)
    {
        await WaitAtTheStopAsync();
        ResumeReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Refused, ErrorMessage = "Program.Callee, Program.Main, Program" });
        EditStatusReturns(suspended: true, Pending);

        await CommandAsync(new StepReplCommand(), arguments);

        SaysTheEditsAreNotApplied(Resources.Repl_CantContinue);
    }

    [TestMethod]
    public async Task AContFromALine_RefusedForTheEditsMadeWhileTheProgramWaited_NamesEachWithWhy_AndDoesNotGoOn()
    {
        await WaitAtTheStopAsync();
        _platform.GotoAsync("30", Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HostDebugGotoResult { Moved = false, Reason = "Program.Callee changed" }));
        EditStatusReturns(suspended: true, Pending);

        await CommandAsync(new ContReplCommand(), "30");

        SaysTheEditsAreNotApplied(Resources.Repl_CantContinue);
        await _platform.DidNotReceiveWithAnyArgs().ResumeAsync(default, default);
    }

    [TestMethod]
    public async Task ACont_RefusedForAnythingButEdits_SaysWhatThePlatformSaid()
    {
        await WaitAtTheStopAsync();
        ResumeReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Refused, ErrorMessage = "the program is running" });
        EditStatusReturns(suspended: true);

        await CommandAsync(new ContReplCommand());

        _console.Received(1).WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Resources.Repl_CantContinue, "the program is running");
        _console.DidNotReceive().WriteLine(Resources.Repl_RunToStartAgain);
    }

    [TestMethod]
    public async Task ACont_RefusedByAServerThatCannotSayWhatIsNotApplied_SaysWhatTheRefusalSaid()
    {
        await WaitAtTheStopAsync();
        ResumeReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Refused, ErrorMessage = "Program.Callee changed" });
        _platform.GetEditStatusAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HostDebugEditStatusResult>(new OmniSharp.Extensions.JsonRpc.Server.MethodNotSupportedException(null, "rdcore/session/debug/editStatus")));

        await CommandAsync(new ContReplCommand());

        _console.Received(1).WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Resources.Repl_CantContinue, "Program.Callee changed");
    }

    [TestMethod]
    public async Task ACont_WhoseQuestionOfTheEditsFailsOtherwise_IsNotSaidAsARefusal()
    {
        // a broken relay, a wire error or a host fault is a failure of the request, as any other the shell makes, and not hidden behind the refusal's words.
        await WaitAtTheStopAsync();
        ResumeReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Refused, ErrorMessage = "Program.Callee changed" });
        _platform.GetEditStatusAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HostDebugEditStatusResult>(new InvalidOperationException("the relay is broken")));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CommandAsync(new ContReplCommand()));
        Assert.AreEqual(ReplDebugState.Suspended, _context.Debugger.State, "the program waits where it did, whatever asking failed");
        _console.DidNotReceive().WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, Resources.Repl_CantContinue, "Program.Callee changed");
    }

    [TestMethod]
    public async Task ACont_ThatGoesOn_AsksNothingOfTheEdits()
    {
        await WaitAtTheStopAsync();
        ResumeReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed });

        await CommandAsync(new ContReplCommand());

        await _platform.DidNotReceive().GetEditStatusAsync(Arg.Any<CancellationToken>());
        Assert.AreEqual(ReplDebugState.Idle, _context.Debugger.State);
    }

    [TestMethod]
    public async Task Edits_OfAProgramThatWaits_AreEachNamedWithWhy()
    {
        await WaitAtTheStopAsync();
        EditStatusReturns(suspended: true, Pending);

        await CommandAsync(new EditsReplCommand());

        _console.Received(1).WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Warning, Resources.Repl_EditsNotApplied);
        foreach (var edit in Pending)
        {
            _console.Received(1).WriteLine($"  {edit.Name}: {edit.Reason}");
        }

        _console.Received(1).WriteLine(Resources.Repl_RunToStartAgain);
        Assert.AreEqual(ReplDebugState.Suspended, _context.Debugger.State, "EDITS goes on with nothing");
    }

    [TestMethod]
    public async Task Edits_WhenEveryEditApplies_SaysNoneWaits()
    {
        await WaitAtTheStopAsync();
        EditStatusReturns(suspended: true);

        await CommandAsync(new EditsReplCommand());

        _console.Received(1).WriteLine(Resources.Repl_Edits_None);
    }

    [TestMethod]
    public async Task Edits_WhenNoProgramWaits_SaysSo()
    {
        EditStatusReturns(suspended: false);

        await CommandAsync(new EditsReplCommand());

        _console.Received(1).WriteLine(Resources.Repl_Edits_NoProgram);
    }

    [TestMethod]
    public async Task Edits_OfAPlatformThatDebugsNothing_SaysNoProgramWaits_AndAsksNothing()
    {
        _platform.Provides<ProgramDebugging>().Returns(false);

        await CommandAsync(new EditsReplCommand());

        _console.Received(1).WriteLine(Resources.Repl_Edits_NoProgram);
        await _platform.DidNotReceive().GetEditStatusAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ARefusal_IsSaidInTheSessionsLanguage()
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("fr-CA");
        try
        {
            await WaitAtTheStopAsync();
            ResumeReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Refused, ErrorMessage = "Program.Callee" });
            EditStatusReturns(suspended: true, Pending);

            await CommandAsync(new ContReplCommand());

            _console.Received(1).WriteMessage(RDCore.SDK.ConsoleIO.Model.MessageKind.Error, "IMPOSSIBLE DE POURSUIVRE",
                "Des modifications faites pendant que le programme attendait ne lui sont pas appliquées :");
            _console.Received(1).WriteLine("RUN pour recommencer.");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = culture;
        }
    }

    [TestMethod]
    public void Edits_IsAVerbOfTheShell_WithItsHelp()
    {
        var command = new EditsReplCommand();

        Assert.AreEqual("EDITS", command.Name);
        Assert.IsFalse(string.IsNullOrWhiteSpace(command.Summary));
    }
}
