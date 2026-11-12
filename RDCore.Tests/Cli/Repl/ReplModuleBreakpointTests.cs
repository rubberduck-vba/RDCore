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
/// <c>BREAK</c> in a shell attached to a workspace: a breakpoint is on a line of one of the workspace's modules, by its name and the line's number in it. The
/// platform is told the breakpoints of each module, by the zero-based lines of the module, before the line typed at the prompt runs. A module the workspace does
/// not have is refused, and so is a line no statement of the module begins on.
/// </summary>
[TestClass]
public sealed class ReplModuleBreakpointTests
{
    private static readonly Uri OtherDocument = new(Path.Combine(Path.GetTempPath(), "rdcore-repl-module-breakpoints", "Other.bas"));

    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();

    public ReplModuleBreakpointTests()
    {
        _platform.Provides<SessionExecute>().Returns(true);
        _platform.Provides<ProgramDebugging>().Returns(true);
        _platform.FindModuleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Uri?>(null));
        _platform.FindModuleAsync(Arg.Is<string>(name => string.Equals(name, "Other", StringComparison.OrdinalIgnoreCase)), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Uri?>(OtherDocument));

        // the platform answers for every breakpoint it is told, each verified, and holds no code to judge them by unless a row says it does.
        _platform.SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new DebugBreakpointsResult
            {
                Breakpoints = [.. call.ArgAt<IReadOnlyList<SessionBreakpointLine>>(1).Select(line => new DebugBreakpoint(line.Line, true))],
            }));
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExecuteResult { Outcome = ExecutionOutcome.Completed }));
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

    // the shell, run over lines piped to it, as `rdc --workspace` runs it, or `rdc` does.
    private async Task RunAsync(bool attachedToWorkspace, params string[] lines)
    {
        var program = new ReplProgram { AttachedToWorkspace = attachedToWorkspace };
        var keys = new ScriptedKeys(interactive: false).Lines(lines);
        var shell = new ReplShell(
            program, _console, _platform, new ReplDocument(program, _platform),
            new ReplCommandDispatcher([Quiet(ReplCommandNames.Splash), Quiet(ReplCommandNames.Memory), new BreakReplCommand()]), NullLogger<ReplShell>.Instance,
            new ConsolePromptEditor(keys, new StringWriter(), () => 80));
        await shell.RunAsync(CancellationToken.None);
    }

    private Task RunAsync(params string[] lines) => RunAsync(attachedToWorkspace: true, lines);

    // the lines of the module the platform was told last, zero-based, and whether each is enabled.
    private (int Line, bool Enabled)[] ToldOf(string module)
        => [.. _platform.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IReplPlatformClient.SetBreakpointsAsync) && Equals(call.GetArguments()[0], module))
            .Select(call => (IReadOnlyList<SessionBreakpointLine>)call.GetArguments()[1]!)
            .Last()
            .Select(line => (line.Line, line.IsEnabled))];

    // the breakpoints BREAK listed, a line each: its text, without its glyph.
    private string[] Listed()
        => [.. _console.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteLine) && call.GetArguments()[0] is IReadOnlyList<ReplTextRun>)
            .Select(call => string.Concat(((IReadOnlyList<ReplTextRun>)call.GetArguments()[0]!).Skip(1).Select(run => run.Text)).Trim())];

    [TestMethod]
    public async Task ABreakpointInAModule_IsSet_AndThePlatformIsToldItByTheModulesZeroBasedLine()
    {
        await RunAsync("BREAK Other 5");

        _console.Received().WriteLine(string.Format(RDCore.CLI.Resources.Repl_Breakpoint_Set, "Other 5"));
        CollectionAssert.AreEqual(new[] { (4, true) }, ToldOf("Other"));
    }

    [TestMethod]
    public async Task ALineTypedAtThePrompt_RunsUnderTheDebugger_WithTheBreakpointsOfTheModulesToldFirst()
    {
        (int Line, bool Enabled)[]? toldWhenItRan = null;
        _platform.ExecuteAsync(Arg.Any<string>(), ReplProgram.ImmediateModuleName, ReplProgram.WorkspaceImmediateEntryPointName, true, false, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                toldWhenItRan = ToldOf("Other");
                return Task.FromResult(new ExecuteResult { Outcome = ExecutionOutcome.Completed });
            });

        _platform.ClearReceivedCalls();
        await RunAsync("BREAK Other 5", "Halt");

        CollectionAssert.AreEqual(new[] { (4, true) }, toldWhenItRan, "the line ran, with the breakpoint told");
    }

    [TestMethod]
    public async Task ABreakpointInAModule_PausesTheTypedLine_AndTheListingMarksTheBreakpointsOfTheModule()
    {
        var path = OtherDocument.LocalPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllLinesAsync(path, ["Option Explicit", "", "Public Sub Halt()", "    Debug.Print \"halting\"", "    Debug.Print \"here\"", "    Debug.Print \"resumed\"", "End Sub"]);
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExecuteResult { Outcome = ExecutionOutcome.Suspended, ErrorLine = 4 }));
        _platform.GetStackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new DebugStackResult
        {
            Frames =
            [
                new DebugStackFrame(0, "Halt", "Other", 4, 4),
                new DebugStackFrame(1, ReplProgram.WorkspaceImmediateEntryPointName, ReplProgram.ImmediateModuleName, 1, 0),
            ],
        }));

        try
        {
            await RunAsync("BREAK Other 4", "BREAK Other 5", "Halt");
        }
        finally
        {
            File.Delete(path);
        }

        _console.Received().WriteLine(string.Format(RDCore.CLI.Resources.Repl_Break_InModuleLine, "Other", 5));
        var listed = _console.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine))
            .Select(call => (Margin: (IReadOnlyList<ReplTextRun>)call.GetArguments()[0]!, Runs: (IReadOnlyList<ReplTextRun>)call.GetArguments()[1]!, Style: (ReplLineStyle)call.GetArguments()[2]!))
            .ToDictionary(line => string.Concat(line.Runs.Select(run => run.Text)).Trim());

        var marked = listed.Single(line => line.Key.StartsWith("4 ", StringComparison.Ordinal)).Value;
        Assert.AreEqual(ReplLineStyle.Breakpoint, marked.Style);
        Assert.IsTrue(marked.Margin.Any(run => run.Style is ReplTextStyle.BreakpointGlyph && run.Text.Trim().Length > 0), string.Join(" | ", marked.Margin));

        var current = listed.Single(line => line.Key.StartsWith("5 ", StringComparison.Ordinal)).Value;
        Assert.AreEqual(ReplLineStyle.CurrentStatement, current.Style);
        Assert.IsTrue(current.Margin.Any(run => run.Style is ReplTextStyle.BreakpointGlyph && run.Text.Trim().Length > 0), string.Join(" | ", current.Margin));

        var plain = listed.Single(line => line.Key.StartsWith("6 ", StringComparison.Ordinal)).Value;
        Assert.AreEqual(ReplLineStyle.Plain, plain.Style);
    }

    [TestMethod]
    public async Task BreakAlone_ListsTheBreakpointsByModuleAndLine_AndBreakClear_RemovesThem_TellingThePlatform()
    {
        await RunAsync("BREAK Other 5", "BREAK Other 7 X > 1", "BREAK");
        CollectionAssert.AreEqual(new[] { "Other 5", "Other 7 X > 1" }, Listed());

        _console.ClearReceivedCalls();
        await RunAsync("BREAK Other 5", "BREAK CLEAR", "BREAK");

        Assert.AreEqual(0, ToldOf("Other").Length, "the platform is told the module has none");
        _console.Received(2).WriteLine(RDCore.CLI.Resources.Repl_Breakpoint_None);
    }

    [TestMethod]
    public async Task BreakToggle_DisablesTheBreakpointOfALineOfAModule_AndEnablesItAgain()
    {
        await RunAsync("BREAK Other 5", "BREAK TOGGLE Other 5");
        _console.Received().WriteLine(string.Format(RDCore.CLI.Resources.Repl_Breakpoint_Disabled, "Other 5"));
        CollectionAssert.AreEqual(new[] { (4, false) }, ToldOf("Other"));

        _console.ClearReceivedCalls();
        _platform.ClearReceivedCalls();
        await RunAsync("BREAK Other 5", "BREAK TOGGLE Other 5", "BREAK TOGGLE Other 5");
        _console.Received().WriteLine(string.Format(RDCore.CLI.Resources.Repl_Breakpoint_Enabled, "Other 5"));
        CollectionAssert.AreEqual(new[] { (4, true) }, ToldOf("Other"));
    }

    [TestMethod]
    public async Task BreakOnALineThatHasOne_ClearsIt()
    {
        await RunAsync("BREAK Other 5", "BREAK Other 5");

        _console.Received().WriteLine(string.Format(RDCore.CLI.Resources.Repl_Breakpoint_Cleared, "Other 5"));
        Assert.AreEqual(0, ToldOf("Other").Length);
    }

    [TestMethod]
    public async Task ABreakpointInAModuleTheWorkspaceDoesNotHave_IsRefused()
    {
        await RunAsync("BREAK Nowhere 5", "BREAK");

        _console.Received().WriteMessage(MessageKind.Error, RDCore.CLI.Resources.Repl_UndefinedModule, "Nowhere");
        await _platform.DidNotReceive().SetBreakpointsAsync("Nowhere", Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>());
        _console.Received().WriteLine(RDCore.CLI.Resources.Repl_Breakpoint_None);
    }

    [TestMethod]
    public async Task ABreakpointOnALineNoStatementBeginsOn_IsRefused_OnceThePlatformHoldsTheModulesCode()
    {
        _platform.SetBreakpointsAsync("Other", Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new DebugBreakpointsResult
            {
                Breakpoints = [.. call.ArgAt<IReadOnlyList<SessionBreakpointLine>>(1).Select(line => new DebugBreakpoint(line.Line, line.Line != 1))],
                Judged = true,
            }));

        await RunAsync("BREAK Other 2", "BREAK");

        _console.Received().WriteMessage(MessageKind.Error, string.Format(RDCore.CLI.Resources.Repl_Breakpoint_NoStatement, "Other 2"));
        _console.DidNotReceive().WriteLine(string.Format(RDCore.CLI.Resources.Repl_Breakpoint_Set, "Other 2"));
        _console.Received().WriteLine(RDCore.CLI.Resources.Repl_Breakpoint_None);
    }

    [TestMethod]
    public async Task ABreakpointOnALineNoStatementBeginsOn_IsRefused_AfterTheLineThatLoadedTheModulesCodeRan()
    {
        var ran = false;
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ran = true;
                return Task.FromResult(new ExecuteResult { Outcome = ExecutionOutcome.Completed });
            });
        _platform.SetBreakpointsAsync("Other", Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new DebugBreakpointsResult
            {
                Breakpoints = [.. call.ArgAt<IReadOnlyList<SessionBreakpointLine>>(1).Select(line => new DebugBreakpoint(line.Line, !ran || line.Line != 1))],
                Judged = ran,
            }));

        await RunAsync("BREAK Other 2", "Hello", "BREAK");

        _console.Received().WriteLine(string.Format(RDCore.CLI.Resources.Repl_Breakpoint_Set, "Other 2"));
        _console.Received().WriteMessage(MessageKind.Error, string.Format(RDCore.CLI.Resources.Repl_Breakpoint_NoStatement, "Other 2"));
        _console.Received().WriteLine(RDCore.CLI.Resources.Repl_Breakpoint_None);
    }

    [TestMethod]
    [DataRow("BREAK 10", DisplayName = "a line number, with no module")]
    [DataRow("BREAK Other", DisplayName = "a module, with no line")]
    [DataRow("BREAK Other x", DisplayName = "a module, and no number")]
    [DataRow("BREAK Other 0", DisplayName = "a line before the first")]
    [DataRow("BREAK TOGGLE Other 5 X > 1", DisplayName = "a toggle with a condition")]
    public async Task ABreakpointThatIsNotAModuleAndALine_IsASyntaxError(string line)
    {
        await RunAsync(line);

        _console.Received().WriteMessage(MessageKind.Error, RDCore.CLI.Resources.Repl_SyntaxError, line["BREAK ".Length..]);
        await _platform.DidNotReceive().SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task WithoutAWorkspace_BreakIsOnALineOfTheProgram_AsItWas()
    {
        await RunAsync(attachedToWorkspace: false, "10 PRINT 1", "BREAK 10");

        _console.Received().WriteLine(string.Format(RDCore.CLI.Resources.Repl_Breakpoint_Set, 10));
        await _platform.DidNotReceive().FindModuleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
