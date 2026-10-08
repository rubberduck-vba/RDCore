using NSubstitute;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Protocol;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The variables a program makes are the program's, as they are in BASIC: <c>NEW</c> clears them with the program, <c>LOAD</c> when it replaces it and
/// <c>RUN</c> before it runs it. A line typed at the prompt does not, so what an earlier line assigned is still there for the next.
/// </summary>
[TestClass]
public sealed class ReplClearProgramTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-repl-clear-ws");

    private readonly MockFileSystem _files = new(new Dictionary<string, MockFileData>(), Root);
    private readonly ReplProgram _program = new();
    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();
    private readonly ReplCommandContext _context;

    public ReplClearProgramTests()
    {
        _platform.Provides<SessionDiscard>().Returns(true);
        _platform.Provides<SessionExecute>().Returns(true);
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExecuteSessionResult { Outcome = ExecutionOutcome.Completed }));
        _context = new ReplCommandContext(_program, _console, _platform, new ReplDocument(_program, _platform), []);
    }

    private Task<ReplCommandResult> RunAsync() => new RunReplCommand().ExecuteAsync(_context, string.Empty, CancellationToken.None);

    private Task<ReplCommandResult> NewAsync() => new NewReplCommand().ExecuteAsync(_context, string.Empty, CancellationToken.None);

    private Task<ReplCommandResult> LoadAsync(string arguments) => new LoadReplCommand(_files).ExecuteAsync(_context, arguments, CancellationToken.None);

    [TestMethod]
    public async Task Run_ClearsTheVariables_BeforeItRunsTheProgram()
    {
        _program.Store(10, "X = X + 1");

        await RunAsync();

        Received.InOrder(() =>
        {
            _platform.DiscardAsync(ReplProgram.ModuleName, Arg.Any<CancellationToken>());
            _platform.ExecuteAsync(Arg.Any<string>(), ReplProgram.ModuleName, ReplProgram.EntryPointName, Arg.Any<CancellationToken>());
        });
    }

    [TestMethod]
    public async Task Run_OfAnEmptyProgram_ClearsAndRunsNothing()
    {
        await RunAsync();

        await _platform.DidNotReceiveWithAnyArgs().DiscardAsync(default!, default);
        await _platform.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default!, default);
    }

    [TestMethod]
    public async Task New_ClearsTheVariablesTheProgramMade_AndTheProgram()
    {
        _program.Store(10, "X = 1");

        await NewAsync();

        Assert.IsTrue(_program.IsEmpty);
        await _platform.Received(1).DiscardAsync(ReplProgram.ModuleName, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Load_ClearsTheVariablesOfTheProgramItReplaces()
    {
        _files.AddFile(Path.Combine(Root, "hello.rdc"), new MockFileData("10 X = 1\r\n"));

        await LoadAsync("hello");

        await _platform.Received(1).DiscardAsync(ReplProgram.ModuleName, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Load_OfAFileThatIsNotThere_LeavesTheVariablesAlone()
    {
        await LoadAsync("nothing");

        await _platform.DidNotReceiveWithAnyArgs().DiscardAsync(default!, default);
    }

    [TestMethod]
    public async Task Load_OfAFileThatIsNotAProgram_LeavesTheProgramAndItsVariablesAlone()
    {
        _program.Store(5, "kept");
        _files.AddFile(Path.Combine(Root, "notes.rdc"), new MockFileData("Sub Foo()\r\n"));

        await LoadAsync("notes");

        await _platform.DidNotReceiveWithAnyArgs().DiscardAsync(default!, default);
    }

    [TestMethod]
    public async Task APlatformThatCannotDiscard_StillRunsTheProgram_AndIsNotAsked()
    {
        _platform.Provides<SessionDiscard>().Returns(false);
        _program.Store(10, "X = 1");

        await RunAsync();

        await _platform.DidNotReceiveWithAnyArgs().DiscardAsync(default!, default);
        await _platform.Received(1).ExecuteAsync(Arg.Any<string>(), ReplProgram.ModuleName, ReplProgram.EntryPointName, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ALineTypedAtThePrompt_DoesNotClearTheVariables()
    {
        _program.Store(10, "X = 1");

        await ReplExecution.ExecuteAsync(_context, _program.ToImmediateModuleSource("PRINT X"), ReplProgram.ImmediateEntryPointName, CancellationToken.None);

        await _platform.DidNotReceiveWithAnyArgs().DiscardAsync(default!, default);
        await _platform.Received(1).ExecuteAsync(Arg.Any<string>(), ReplProgram.ModuleName, ReplProgram.ImmediateEntryPointName, Arg.Any<CancellationToken>());
    }
}
