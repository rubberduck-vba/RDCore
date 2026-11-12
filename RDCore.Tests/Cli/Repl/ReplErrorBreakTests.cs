using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Console;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.ConsoleIO.Model;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The shell runs under the debugger waiting at the errors nothing handles, with a workspace and without one. A program that waits at one is listed as a break is,
/// around the statement that raised it, and the error is said after the listing as a run it ended says it; the program waits, to be gone on with or ended.
/// </summary>
[TestClass]
public sealed class ReplErrorBreakTests
{
    private static readonly string[] Program = ["10 N = 1", "20 N = N + 1", "30 PRINT N", "40 N = N / 0", "50 PRINT N", "60 PRINT 2"];
    private static readonly Uri OtherDocument = new(Path.Combine(Path.GetTempPath(), "rdcore-repl-error-break", "Other.bas"));

    // a file of its own for the row of a run that ended, which runs alongside the row of a program that waits.
    private static readonly Uri OtherEndedDocument = new(Path.Combine(Path.GetTempPath(), "rdcore-repl-error-break-ended", "Other.bas"));

    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();

    public ReplErrorBreakTests()
    {
        _platform.Provides<SessionExecute>().Returns(true);
        _platform.Provides<ProgramDebugging>().Returns(true);
        _platform.SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DebugBreakpointsResult()));
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExecuteResult { Outcome = ExecutionOutcome.Completed }));
        _platform.ResumeAsync(Arg.Any<StepKind?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ExecuteResult { Outcome = ExecutionOutcome.Completed }));
        _platform.GetStackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new DebugStackResult()));
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

    // the program waits at a division by zero, at the line of the program that starts with this number in the source it was sent.
    private void WaitsAtAnErrorAt(int number)
        => _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(DivisionByZero(
                Array.FindIndex(call.ArgAt<string>(0).Split("\r\n"), line => line.StartsWith($"{number} ", StringComparison.Ordinal)))));

    private static ExecuteResult DivisionByZero(int line) => new()
    {
        Outcome = ExecutionOutcome.Suspended,
        ErrorNumber = 11,
        ErrorCode = "VBR00011",
        ErrorTitle = "Run-time error",
        ErrorMessage = "Division by zero",
        ErrorLine = line,
    };

    // the shell, run over lines piped to it, as `rdc` runs it.
    private async Task RunAsync(bool attachedToWorkspace, params string[] lines)
    {
        var program = new ReplProgram { AttachedToWorkspace = attachedToWorkspace };
        var keys = new ScriptedKeys(interactive: false).Lines(attachedToWorkspace ? lines : [.. Program, .. lines]);
        var shell = new ReplShell(
            program, _console, _platform, new ReplDocument(program, _platform),
            new ReplCommandDispatcher([Quiet(ReplCommandNames.Splash), Quiet(ReplCommandNames.Memory), new RunReplCommand(), new ContReplCommand()]),
            NullLogger<ReplShell>.Instance, new ConsolePromptEditor(keys, new StringWriter(), () => 80));
        await shell.RunAsync(CancellationToken.None);
    }

    private (string Text, ReplLineStyle Style)[] Listed()
        => [.. _console.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine))
            .Select(call => (
                string.Concat(((IReadOnlyList<ReplTextRun>)call.GetArguments()[1]!).Select(run => run.Text)).Trim(),
                (ReplLineStyle)call.GetArguments()[2]!))];

    // where in what the console was given the error is said, and where the listing ended.
    private (int Said, int ListedLast) Order(string title)
    {
        var calls = _console.ReceivedCalls().ToList();
        var said = calls.FindIndex(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteMessage) && Equals(call.GetArguments()[1], title));
        return (said, calls.FindLastIndex(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine)));
    }

    [TestMethod]
    [DataRow(true, DisplayName = "with a workspace")]
    [DataRow(false, DisplayName = "without a workspace")]
    public async Task TheShell_RunsWaitingAtTheErrorsNothingHandles(bool attachedToWorkspace)
    {
        var told = false;
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                told = _platform.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IReplPlatformClient.SetErrorBreakAsync)
                    && Equals(call.GetArguments()[0], ErrorBreakMode.Unhandled));
                return Task.FromResult(new ExecuteResult { Outcome = ExecutionOutcome.Completed });
            });

        await RunAsync(attachedToWorkspace, "Debug.Print 1");

        Assert.IsTrue(told, "the mode is said before the line runs");
    }

    [TestMethod]
    public async Task AProgramThatWaitsAtAnError_ListsTheLinesAroundTheStatementThatRaisedIt_ThenSaysTheError()
    {
        WaitsAtAnErrorAt(40);

        await RunAsync(attachedToWorkspace: false, "RUN");

        var listed = Listed();
        CollectionAssert.AreEqual(new[] { "20 N = N + 1", "30 PRINT N", "40 N = N / 0", "50 PRINT N", "60 PRINT 2" }, listed.Select(line => line.Text).ToArray());
        Assert.AreEqual("40 N = N / 0", listed.Single(line => line.Style is ReplLineStyle.CurrentStatement).Text);
        var (said, listedLast) = Order(string.Format(RDCore.CLI.Resources.Repl_RuntimeError_InLine, "RUN-TIME ERROR", 40));
        Assert.IsTrue(said >= 0 && listedLast < said, $"listed last at {listedLast}, said at {said}");
        _console.DidNotReceive().WriteLine(string.Format(RDCore.CLI.Resources.Repl_Break_InLine, 40));
    }

    [TestMethod]
    public async Task AProgramThatWaitsAtAnError_IsGoneOnWith_ByCont()
    {
        WaitsAtAnErrorAt(40);

        await RunAsync(attachedToWorkspace: false, "RUN", "CONT");

        await _platform.Received(1).ResumeAsync(null, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AProgramThatWaitsAtAnError_IsEndedByAnEnd_AndCannotBeGoneOnWithAfterIt()
    {
        WaitsAtAnErrorAt(40);
        _platform.ExecuteInFrameAsync(Arg.Any<int>(), "END", Arg.Any<CancellationToken>()).Returns(Task.FromResult(new DebugEvaluateResult { EndedTheProgram = true }));

        await RunAsync(attachedToWorkspace: false, "RUN", "END", "CONT");

        await _platform.Received(1).ExecuteInFrameAsync(Arg.Any<int>(), "END", Arg.Any<CancellationToken>());
        await _platform.DidNotReceive().ResumeAsync(Arg.Any<StepKind?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AProgramThatWaitsAtAnErrorInAModuleOfTheWorkspace_ListsTheModulesLines_ThenSaysTheErrorByTheModulesLine()
    {
        var path = OtherDocument.LocalPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, string.Join("\r\n", "Option Explicit", "", "Public Sub Divide()", "    Dim X As Double", "    X = 1 / 0", "End Sub"));
        _platform.FindModuleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Uri?>(null));
        _platform.FindModuleAsync("Other", Arg.Any<CancellationToken>()).Returns(Task.FromResult<Uri?>(OtherDocument));
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DivisionByZero(4)));
        _platform.GetStackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new DebugStackResult
        {
            Frames =
            [
                new DebugStackFrame(0, "Divide", "Other", 4, 4),
                new DebugStackFrame(1, ReplProgram.WorkspaceImmediateEntryPointName, ReplProgram.ImmediateModuleName, 1, 0),
            ],
        }));

        await RunAsync(attachedToWorkspace: true, "Divide");

        var listed = Listed();
        CollectionAssert.AreEqual(
            new[] { "3 Public Sub Divide()", "4     Dim X As Double", "5     X = 1 / 0", "6 End Sub" }, listed.Select(line => line.Text).ToArray());
        Assert.AreEqual("5     X = 1 / 0", listed.Single(line => line.Style is ReplLineStyle.CurrentStatement).Text);
        var (said, listedLast) = Order(string.Format(RDCore.CLI.Resources.Repl_RuntimeError_InModuleLine, "RUN-TIME ERROR", "Other", 5));
        Assert.IsTrue(said >= 0 && listedLast < said, $"listed last at {listedLast}, said at {said}");
        _console.DidNotReceive().WriteLine(string.Format(RDCore.CLI.Resources.Repl_Break_InModuleLine, "Other", 5));
    }

    [TestMethod]
    public async Task ARunAnErrorInAModuleOfTheWorkspaceEnded_ListsTheModulesLines_ThenSaysTheErrorAndEachActivationByTheModulesLines()
    {
        var path = OtherEndedDocument.LocalPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, string.Join("\r\n", "Option Explicit", "", "Public Sub Divide()", "    Dim X As Double", "    X = 1 / 0", "End Sub"));
        _platform.FindModuleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Uri?>(null));
        _platform.FindModuleAsync("Other", Arg.Any<CancellationToken>()).Returns(Task.FromResult<Uri?>(OtherEndedDocument));
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DivisionByZero(4) with
            {
                Outcome = ExecutionOutcome.RuntimeError,
                ErrorModule = "Other",
                StackTrace =
                [
                    new ExecuteStackFrame("Divide", 4, 4, null) { Module = "Other" },
                    new ExecuteStackFrame(ReplProgram.WorkspaceImmediateEntryPointName, 1, 0, null) { Module = ReplProgram.ImmediateModuleName },
                ],
            }));

        await RunAsync(attachedToWorkspace: true, "Divide");

        var listed = Listed();
        CollectionAssert.AreEqual(
            new[] { "3 Public Sub Divide()", "4     Dim X As Double", "5     X = 1 / 0", "6 End Sub" }, listed.Select(line => line.Text).ToArray());
        var title = string.Format(RDCore.CLI.Resources.Repl_RuntimeError_InModuleLine, "RUN-TIME ERROR", "Other", 5);
        var (said, listedLast) = Order(title);
        Assert.IsTrue(said >= 0 && listedLast < said, $"listed last at {listedLast}, said at {said}");
        _console.Received().WriteMessage(MessageKind.Error, title, Arg.Is<string?>(detail
            => detail!.Contains("  Divide (Other line 5)") && detail.Contains($"  {ReplProgram.WorkspaceImmediateEntryPointName}") && !detail.Contains("_Immediate line")));
    }

    [TestMethod]
    public async Task ARunAnErrorInAModuleOfTheWorkspaceEnded_SaysEachActivationByTheModulesLines_InTheSessionsLanguage()
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("fr-CA");
        try
        {
            _platform.FindModuleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Uri?>(null));
            _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(DivisionByZero(4) with
                {
                    Outcome = ExecutionOutcome.RuntimeError,
                    ErrorModule = "Other",
                    StackTrace = [new ExecuteStackFrame("Divide", 4, 4, null) { Module = "Other" }],
                }));

            await RunAsync(attachedToWorkspace: true, "Divide");

            _console.Received().WriteMessage(MessageKind.Error, "RUN-TIME ERROR DANS Other À LA LIGNE 5", Arg.Is<string?>(detail
                => detail!.Contains("  Divide (Other à la ligne 5)") && !detail.Contains("Other line")));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = culture;
        }
    }

    [TestMethod]
    public async Task ARunAnErrorInTheShellsProgramEnded_SaysItByTheProgramsLineNumber()
    {
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var line = Array.FindIndex(call.ArgAt<string>(0).Split("\r\n"), text => text.StartsWith("40 ", StringComparison.Ordinal));
                return Task.FromResult(DivisionByZero(line) with
                {
                    Outcome = ExecutionOutcome.RuntimeError,
                    ErrorModule = ReplProgram.ModuleName,
                    StackTrace = [new ExecuteStackFrame(ReplProgram.EntryPointName, line, 0, null) { Module = ReplProgram.ModuleName }],
                });
            });

        await RunAsync(attachedToWorkspace: false, "RUN");

        _console.Received().WriteMessage(MessageKind.Error, string.Format(RDCore.CLI.Resources.Repl_RuntimeError_InLine, "RUN-TIME ERROR", 40),
            Arg.Is<string?>(detail => detail!.Contains($"  {ReplProgram.EntryPointName} 40") && !detail.Contains($"({ReplProgram.ModuleName} line")));
    }

    [TestMethod]
    public async Task AStopThatIsNotAnError_IsStillSaidAsABreak()
    {
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ExecuteResult
            {
                Outcome = ExecutionOutcome.Suspended,
                ErrorLine = Array.FindIndex(call.ArgAt<string>(0).Split("\r\n"), line => line.StartsWith("30 ", StringComparison.Ordinal)),
            }));

        await RunAsync(attachedToWorkspace: false, "RUN");

        _console.Received().WriteLine(string.Format(RDCore.CLI.Resources.Repl_Break_InLine, 30));
        _console.DidNotReceive().WriteMessage(MessageKind.Error, Arg.Any<string>(), Arg.Any<string?>());
    }
}
