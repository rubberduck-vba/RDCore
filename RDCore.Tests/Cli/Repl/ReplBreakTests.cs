using NSubstitute;
using RDCore.CLI;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// A program that was stopped - by a <c>STOP</c>, or by a break at the keyboard - says where, as BASIC has always done, in the lines the program was typed in.
/// </summary>
[TestClass]
public sealed class ReplBreakTests
{
    private readonly ReplProgram _program = new();
    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();
    private readonly ReplCommandContext _context;

    public ReplBreakTests()
    {
        _platform.Provides<SessionDiscard>().Returns(true);
        _platform.Provides<SessionExecute>().Returns(true);
        _context = new ReplCommandContext(_program, _console, _platform, new ReplDocument(_program, _platform), []);
    }

    private async Task RunAsync(ExecuteSessionResult result)
    {
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(result));
        await new RunReplCommand().ExecuteAsync(_context, string.Empty, CancellationToken.None);
    }

    [TestMethod]
    public async Task ABreak_SaysTheLineItStoppedAt_NotTheLineOfTheGeneratedSource()
    {
        _program.Store(10, "X = 1");
        _program.Store(20, "STOP");

        // the module's first line is the Sub statement, so the second line of the program is the third line of its source: index 2.
        await RunAsync(new ExecuteSessionResult { Outcome = ExecutionOutcome.Interrupted, ErrorLine = 2 });

        _console.Received().WriteLine(string.Format(Resources.Repl_Break_InLine, 20));
    }

    [TestMethod]
    public async Task ABreak_ThatCouldNotBePlaced_IsABreakAllTheSame()
    {
        _program.Store(10, "X = 1");

        await RunAsync(new ExecuteSessionResult { Outcome = ExecutionOutcome.Interrupted });

        _console.Received().WriteLine(Resources.Repl_Break);
    }

    [TestMethod]
    public async Task AnEnd_SaysNothing_AsBasicDoesNot()
    {
        _program.Store(10, "END");

        await RunAsync(new ExecuteSessionResult { Outcome = ExecutionOutcome.Halted });

        _console.DidNotReceiveWithAnyArgs().WriteLine(default(string)!);
        _console.DidNotReceiveWithAnyArgs().WriteMessage(default, default!, default!);
    }
}
