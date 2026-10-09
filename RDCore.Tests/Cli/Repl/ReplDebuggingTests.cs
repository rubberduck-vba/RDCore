using NSubstitute;
using RDCore.CLI;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.Themes;
using RDCore.SDK.Client;
using RDCore.SDK.ConsoleIO;
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
        _platform.SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>())
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

    // ---- a program that waits ----

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
        _context.Debugger.Toggle(30);
        ExecuteReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed });

        await RunAsync();

        await _platform.Received(1).SetBreakpointsAsync("Program", Arg.Is<IReadOnlyList<int>>(lines => lines.SequenceEqual(new[] { 3 })), Arg.Any<CancellationToken>());
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
        CollectionAssert.AreEqual(new[] { 30 }, _context.Debugger.Breakpoints.ToArray());
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
        await CommandAsync(new BreakReplCommand());

        _console.Received().WriteLine(string.Format(Resources.Repl_Breakpoint_List, "10, 30"));
    }

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

    [TestMethod]
    public async Task AStatement_TypedWhileTheProgramWaits_RunsAlongsideIt_AndTheProgramWaitsStill()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        ExecuteReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed, Output = ["1"] });

        await ReplExecution.ExecuteAsync(_context, "src", ReplProgram.ImmediateEntryPointName, CancellationToken.None, immediate: true);

        await _platform.Received(1).ExecuteAsync("src", "Program", "Immediate", false, true, Arg.Any<CancellationToken>());
        _console.Received().WriteLine("1");
        Assert.AreEqual(ReplDebugState.Suspended, _context.Debugger.State);
        Assert.AreEqual(20, _context.Debugger.StoppedAt);
    }

    [TestMethod]
    public async Task AnEnd_TypedWhileTheProgramWaits_EndsTheProgram()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        ExecuteReturns(new ExecuteSessionResult { Outcome = ExecutionOutcome.Halted });

        await ReplExecution.ExecuteAsync(_context, "src", ReplProgram.ImmediateEntryPointName, CancellationToken.None, immediate: true);

        Assert.AreEqual(ReplDebugState.Idle, _context.Debugger.State);
    }

    [TestMethod]
    public async Task AStatement_TypedWhenNoProgramWaits_IsARunLikeAnyOther()
    {
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed }));

        await ReplExecution.ExecuteAsync(_context, "src", ReplProgram.ImmediateEntryPointName, CancellationToken.None, immediate: true);

        await _platform.Received(1).ExecuteAsync("src", "Program", "Immediate", Arg.Any<CancellationToken>());
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
                return (text, (ReplTextRun)arguments[0]!, (ReplLineStyle)arguments[2]!);
            }
        }

        throw new AssertFailedException($"line {number} was not listed");
    }

    [TestMethod]
    public async Task AListing_HasAMarginOfOneCharacter_AndAMarkInItForABreakpoint()
    {
        await CommandAsync(new BreakReplCommand(), "20");

        await CommandAsync(new ListReplCommand());

        var plain = Listed(10);
        Assert.AreEqual(" ", plain.Margin.Text);
        Assert.AreEqual(ReplLineStyle.Plain, plain.Style);

        var marked = Listed(20);
        Assert.AreEqual("●", marked.Margin.Text);
        Assert.AreEqual(ReplTextStyle.BreakpointGlyph, marked.Margin.Style);
        Assert.AreEqual(ReplLineStyle.Breakpoint, marked.Style);
    }

    [TestMethod]
    public async Task AListing_MarksTheStatementTheProgramWaitsBefore_AsAWhole()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();

        await CommandAsync(new ListReplCommand());

        Assert.AreEqual(ReplLineStyle.CurrentStatement, Listed(20).Style);
        Assert.AreEqual(ReplLineStyle.Plain, Listed(10).Style);
    }

    [TestMethod]
    public async Task AListing_OfTheLineTheProgramWaitsBeforeThatHasABreakpoint_ShowsBoth()
    {
        ExecuteReturns(SuspendedAt(2));
        await RunAsync();
        await CommandAsync(new BreakReplCommand(), "20");

        await CommandAsync(new ListReplCommand());

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

        await CommandAsync(new ListReplCommand());

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
            new ReplTextRun("●", ReplTextStyle.BreakpointGlyph), [new ReplTextRun(" 20 STOP", ReplTextStyle.Plain)], ReplLineStyle.Breakpoint));

        StringAssert.Contains(output, "●");
        StringAssert.Contains(output, "48;2;181;26;23", "the background of the breakpoint line");
        StringAssert.Contains(output, "38;2;255;255;255", "and its foreground");
        Assert.IsLessThan(output.IndexOf("48;2;181;26;23", StringComparison.Ordinal), output.IndexOf("●", StringComparison.Ordinal), "the mark comes before the marking");
    }

    [TestMethod]
    public void ALineThatIsPlain_HasNoBackground()
    {
        var output = Render(Dark(), console => console.WriteListingLine(
            new ReplTextRun(" ", ReplTextStyle.Plain), [new ReplTextRun(" 10 X = 1", ReplTextStyle.Plain)], ReplLineStyle.Plain));

        StringAssert.Contains(output, "10 X = 1");
        Assert.DoesNotContain("48;2;", output);
    }

    [TestMethod]
    public void TheColoursOfAMarkedLine_KeepTheBackgroundOfTheMarking()
    {
        var output = Render(Dark(), console => console.WriteListingLine(
            new ReplTextRun(" ", ReplTextStyle.Plain),
            [new ReplTextRun(" 20 ", ReplTextStyle.Plain), new ReplTextRun("STOP", ReplTextStyle.Keyword)], ReplLineStyle.CurrentStatement));

        // #d8b064 is the background of the current statement, and the keyword is blue on it.
        StringAssert.Contains(output, "48;2;216;176;100");
        StringAssert.Contains(output, "38;2;62;97;255");
    }
}
