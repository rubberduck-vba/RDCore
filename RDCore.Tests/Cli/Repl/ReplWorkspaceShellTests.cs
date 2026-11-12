using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Console;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.ConsoleIO.Model;
using RDCore.External.Interaction;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// A shell attached to a workspace (<c>rdc --workspace</c>) runs each line in the workspace's project: the module the line runs in is
/// <see cref="ReplProgram.ImmediateModuleName"/>, which declares nothing but the line, so a name the line uses is the project's. It has no numbered program, and
/// refuses a numbered line and every command that works on the program. Without a workspace, the shell keeps its numbered program.
/// </summary>
[TestClass]
public sealed class ReplWorkspaceShellTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-repl-workspace-shell");

    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();
    private readonly MockFileSystem _files = new(new Dictionary<string, MockFileData>(), Root);

    public ReplWorkspaceShellTests()
    {
        _platform.Provides<SessionExecute>().Returns(true);
        _platform.Provides<SessionDiscard>().Returns(true);
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExecuteResult { Outcome = ExecutionOutcome.Completed }));
    }

    // the shell, run over lines piped to it, as `rdc --workspace` runs it (attached) or `rdc` does (with a scratch workspace of its own).
    private async Task<ReplProgram> RunAsync(bool attachedToWorkspace, params string[] lines)
    {
        var program = new ReplProgram { AttachedToWorkspace = attachedToWorkspace };
        var keys = new ScriptedKeys(interactive: false).Lines(lines);
        var output = new StringWriter();
        IReplCommand[] commands =
        [
            Quiet(ReplCommandNames.Splash), Quiet(ReplCommandNames.Memory),
            new ListReplCommand(), new RunReplCommand(), new NewReplCommand(), new LoadReplCommand(_files), new SaveReplCommand(_files), new AnalyzeReplCommand(),
            new BreakReplCommand(), new FoldReplCommand(), new DuckyReplCommand(new ConsoleUserInteraction(keys, output, consoleWidth: () => 80)),
            new ContReplCommand(),
        ];
        var shell = new ReplShell(
            program, _console, _platform, new ReplDocument(program, _platform), new ReplCommandDispatcher(commands), NullLogger<ReplShell>.Instance,
            new ConsolePromptEditor(keys, output, () => 80));
        await shell.RunAsync(CancellationToken.None);
        return program;
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

    private void AssertRefused() => _console.Received().WriteMessage(
        MessageKind.Error, RDCore.CLI.Resources.Repl_NoProgramInWorkspace, RDCore.CLI.Resources.Repl_NoProgramInWorkspace_Verbose);

    [TestMethod]
    [DataRow("Main", DisplayName = "a procedure of the workspace")]
    [DataRow("Program.Main", DisplayName = "a procedure of the workspace, qualified by its module")]
    [DataRow("Immediate", DisplayName = "a procedure of the workspace named Immediate")]
    public async Task ALineInAWorkspace_RunsInTheImmediateModule_WhichDeclaresNothingButTheLine(string line)
    {
        await RunAsync(attachedToWorkspace: true, line);

        await _platform.Received(1).ExecuteAsync(
            $"Public Sub {ReplProgram.WorkspaceImmediateEntryPointName}()\r\n{line}\r\nEnd Sub\r\n", ReplProgram.ImmediateModuleName,
            ReplProgram.WorkspaceImmediateEntryPointName, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ALineWithoutAWorkspace_RunsInTheProgram_WithTheProgramsProcedure()
    {
        await RunAsync(attachedToWorkspace: false, "10 PRINT 1", "Main");

        await _platform.Received(1).ExecuteAsync(
            Arg.Is<string>(source => source.StartsWith($"Public Sub {ReplProgram.EntryPointName}()\r\n10 PRINT 1\r\nEnd Sub\r\n", StringComparison.Ordinal)
                && source.Contains($"Public Sub {ReplProgram.ImmediateEntryPointName}()\r\nMain\r\n", StringComparison.Ordinal)),
            ReplProgram.ModuleName, ReplProgram.ImmediateEntryPointName, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("10 PRINT 1", DisplayName = "a line stored")]
    [DataRow("10", DisplayName = "a line deleted")]
    public async Task ANumberedLineInAWorkspace_IsRefused(string line)
    {
        var program = await RunAsync(attachedToWorkspace: true, line);

        AssertRefused();
        Assert.IsTrue(program.IsEmpty);
    }

    // in a workspace, "the program" could be any of its modules: the command is refused, and nothing of the session is touched.
    [TestMethod]
    [DataRow("LIST")]
    [DataRow("RUN")]
    [DataRow("NEW")]
    [DataRow("LOAD game")]
    [DataRow("SAVE game")]
    [DataRow("ANALYZE")]
    [DataRow("FOLD 10")]
    [DataRow("DUCKY")]
    public async Task ACommandOnTheProgramInAWorkspace_IsRefused(string line)
    {
        await RunAsync(attachedToWorkspace: true, line);

        AssertRefused();
        Assert.AreEqual(0, _platform.ReceivedCalls().Count(call => call.GetMethodInfo().Name != nameof(IReplPlatformClient.Provides)),
            string.Join(", ", _platform.ReceivedCalls().Select(call => call.GetMethodInfo().Name)));
        Assert.IsFalse(_files.AllFiles.Any(), "nothing is written");
    }

    // CONT goes on in a workspace; only CONT to a line, which is one of the numbered program's, is refused.
    [TestMethod]
    public async Task ContinuingFromALineInAWorkspace_IsRefused()
    {
        var program = new ReplProgram { AttachedToWorkspace = true };
        _platform.Provides<ProgramDebugging>().Returns(true);
        var context = new ReplCommandContext(program, _console, _platform, new ReplDocument(program, _platform), []);
        context.Debugger.Suspended(null);

        await new ContReplCommand().ExecuteAsync(context, "10", CancellationToken.None);

        AssertRefused();
        await _platform.DidNotReceive().GotoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ACommandOnTheProgramWithoutAWorkspace_IsNotRefused()
    {
        await RunAsync(attachedToWorkspace: false, "10 PRINT 1", "LIST");

        _console.DidNotReceive().WriteMessage(MessageKind.Error, RDCore.CLI.Resources.Repl_NoProgramInWorkspace, Arg.Any<string?>());
    }
}
