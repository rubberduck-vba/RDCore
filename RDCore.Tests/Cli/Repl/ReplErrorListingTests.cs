using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Console;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.ConsoleIO.Model;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// Before a run-time error the shell's program raises is said, the lines around the statement that raised it are listed as a break lists them: two above and
/// two below, as many as the program has, with that statement marked as the current one and the breakpoints marked in the margin.
/// </summary>
[TestClass]
public sealed class ReplErrorListingTests
{
    private static readonly string[] Program = ["10 N = 1", "20 N = N + 1", "30 PRINT N", "40 N = N / 0", "50 PRINT N", "60 PRINT 2"];

    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();

    public ReplErrorListingTests()
    {
        _platform.Provides<SessionExecute>().Returns(true);
        _platform.Provides<ProgramDebugging>().Returns(true);
        _platform.SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new DebugBreakpointsResult
            {
                Breakpoints = [.. call.ArgAt<IReadOnlyList<SessionBreakpointLine>>(1).Select(line => new DebugBreakpoint(line.Line, true))],
            }));
    }

    // what the shell runs as it starts, which says nothing here.
    private static IReplCommand Quiet(string name)
    {
        var command = Substitute.For<IReplCommand>();
        command.Name.Returns(name);
        command.Aliases.Returns([]);
        command.ExecuteAsync(Arg.Any<ReplCommandContext>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ReplCommandResult.Continue);
        return command;
    }

    // the platform raises a division by zero at the line of the program that starts with this number, in the source it was sent.
    private void RaisesAt(int number)
        => _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ExecuteResult
            {
                Outcome = ExecutionOutcome.RuntimeError,
                ErrorNumber = 11,
                ErrorCode = "VBR00011",
                ErrorTitle = "Run-time error",
                ErrorMessage = "Division by zero",
                ErrorLine = Array.FindIndex(call.ArgAt<string>(0).Split("\r\n"), line => line.StartsWith($"{number} ", StringComparison.Ordinal)),
            }));

    // the shell, run over lines piped to it, as `rdc` runs it.
    private async Task RunAsync(params string[] lines)
    {
        var program = new ReplProgram();
        var keys = new ScriptedKeys(interactive: false).Lines([.. Program, .. lines]);
        var shell = new ReplShell(
            program, _console, _platform, new ReplDocument(program, _platform),
            new ReplCommandDispatcher([Quiet(ReplCommandNames.Splash), Quiet(ReplCommandNames.Memory), new RunReplCommand(), new BreakReplCommand()]),
            NullLogger<ReplShell>.Instance, new ConsolePromptEditor(keys, new StringWriter(), () => 80));
        await shell.RunAsync(CancellationToken.None);
    }

    // the lines listed, in order: the text of each, its margin, and how it was marked.
    private (string Text, IReadOnlyList<ReplTextRun> Margin, ReplLineStyle Style)[] Listed()
        => [.. _console.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine))
            .Select(call => (
                string.Concat(((IReadOnlyList<ReplTextRun>)call.GetArguments()[1]!).Select(run => run.Text)).Trim(),
                (IReadOnlyList<ReplTextRun>)call.GetArguments()[0]!,
                (ReplLineStyle)call.GetArguments()[2]!))];

    [TestMethod]
    public async Task ARunTimeError_ListsTheLinesAroundTheStatementThatRaisedIt_WithItMarkedAsTheCurrentStatement_ThenIsSaid()
    {
        RaisesAt(40);

        await RunAsync("RUN");

        var calls = _console.ReceivedCalls().Select(call => call.GetMethodInfo().Name).ToList();
        var said = _console.ReceivedCalls().ToList().FindIndex(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteMessage)
            && Equals(call.GetArguments()[1], string.Format(RDCore.CLI.Resources.Repl_RuntimeError_InLine, "RUN-TIME ERROR", 40)));
        Assert.IsTrue(said >= 0, "the error is said");
        Assert.IsTrue(calls.LastIndexOf(nameof(IReplConsole.WriteListingLine)) is var listedLast && listedLast >= 0 && listedLast < said, "the listing comes first, then the error");

        var listed = Listed();
        CollectionAssert.AreEqual(new[] { "20 N = N + 1", "30 PRINT N", "40 N = N / 0", "50 PRINT N", "60 PRINT 2" }, listed.Select(line => line.Text).ToArray());
        CollectionAssert.AreEqual(
            new[] { ReplLineStyle.Plain, ReplLineStyle.Plain, ReplLineStyle.CurrentStatement, ReplLineStyle.Plain, ReplLineStyle.Plain },
            listed.Select(line => line.Style).ToArray());
    }

    [TestMethod]
    [DataRow(10, new[] { "10 N = 1", "20 N = N + 1", "30 PRINT N" }, DisplayName = "at the first line")]
    [DataRow(20, new[] { "10 N = 1", "20 N = N + 1", "30 PRINT N", "40 N = N / 0" }, DisplayName = "at the second line")]
    [DataRow(60, new[] { "40 N = N / 0", "50 PRINT N", "60 PRINT 2" }, DisplayName = "at the last line")]
    public async Task TheLinesListedAroundARunTimeError_AreTheOnesTheProgramHas(int number, string[] expected)
    {
        RaisesAt(number);

        await RunAsync("RUN");

        var listed = Listed();
        CollectionAssert.AreEqual(expected, listed.Select(line => line.Text).ToArray());
        Assert.AreEqual($"{number}", listed.Single(line => line.Style is ReplLineStyle.CurrentStatement).Text.Split(' ')[0]);
    }

    [TestMethod]
    public async Task TheListingAroundARunTimeError_MarksTheBreakpointsOfTheProgram()
    {
        RaisesAt(40);

        await RunAsync("BREAK 50", "BREAK 40", "RUN");

        var listed = Listed();
        var breakpoint = listed.Single(line => line.Text.StartsWith("50 ", StringComparison.Ordinal));
        Assert.AreEqual(ReplLineStyle.Breakpoint, breakpoint.Style);
        Assert.IsTrue(breakpoint.Margin.Any(run => run.Style is ReplTextStyle.BreakpointGlyph && run.Text.Trim().Length > 0), string.Join(" | ", breakpoint.Margin));

        var raised = listed.Single(line => line.Text.StartsWith("40 ", StringComparison.Ordinal));
        Assert.AreEqual(ReplLineStyle.CurrentStatement, raised.Style);
        Assert.IsTrue(raised.Margin.Any(run => run.Style is ReplTextStyle.BreakpointGlyph && run.Text.Trim().Length > 0), string.Join(" | ", raised.Margin));
    }

    [TestMethod]
    public async Task ARunTimeErrorTheProgramRaises_WhenALineTypedAtThePromptCallsIt_ListsTheLinesAroundIt()
    {
        RaisesAt(40);

        await RunAsync("Main");

        Assert.AreEqual("40 N = N / 0", Listed().Single(line => line.Style is ReplLineStyle.CurrentStatement).Text);
    }

    [TestMethod]
    public async Task ARunTimeErrorTheTypedLineRaisesItself_ListsNothing_AsABreakInItListsNothing()
    {
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ExecuteResult
            {
                Outcome = ExecutionOutcome.RuntimeError,
                ErrorTitle = "Run-time error",
                ErrorMessage = "Division by zero",
                ErrorLine = Array.IndexOf(call.ArgAt<string>(0).Split("\r\n"), "PRINT 1 / 0"),
            }));

        await RunAsync("PRINT 1 / 0");

        _console.Received().WriteMessage(MessageKind.Error, string.Format(RDCore.CLI.Resources.Repl_RuntimeError, "RUN-TIME ERROR"), Arg.Any<string?>());
        Assert.AreEqual(0, Listed().Length);
    }
}
