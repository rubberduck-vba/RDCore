using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.App.Console;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.SDK.Client;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// A line typed at the prompt runs under the debugger, as <c>RUN</c> runs the program, with a workspace and without one. A break in a module of the workspace
/// lists the lines around it as <c>LIST</c> lists the program, highlighted, with the line the program waits before marked; then it is said by the module's line.
/// </summary>
[TestClass]
public sealed class ReplLineDebuggingTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-repl-line-debugging");
    private static readonly string OtherPath = Path.Combine(Root, "Other.bas");

    private static readonly string[] Other =
    [
        "Attribute VB_Name = \"Other\"",
        "Option Explicit",
        "Public Sub Halt()",
        "    Debug.Print \"halting\"",
        "    Stop",
        "    Debug.Print \"resumed\"",
        "End Sub",
    ];

    private readonly IReplConsole _console = Substitute.For<IReplConsole>();
    private readonly IReplPlatformClient _platform = Substitute.For<IReplPlatformClient>();

    public ReplLineDebuggingTests()
    {
        _platform.Provides<SessionExecute>().Returns(true);
        _platform.Provides<ProgramDebugging>().Returns(true);
        _platform.SetBreakpointsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SessionBreakpointLine>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DebugBreakpointsResult()));
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

    [TestMethod]
    [DataRow(true, DisplayName = "with a workspace")]
    [DataRow(false, DisplayName = "without a workspace")]
    public async Task ALineTypedAtThePrompt_RunsUnderTheDebugger(bool attachedToWorkspace)
    {
        var program = new ReplProgram { AttachedToWorkspace = attachedToWorkspace };
        var keys = new ScriptedKeys(interactive: false).Lines("Main");
        var output = new StringWriter();
        var shell = new ReplShell(
            program, _console, _platform, new ReplDocument(program, _platform),
            new ReplCommandDispatcher([Quiet(ReplCommandNames.Splash), Quiet(ReplCommandNames.Memory)]), NullLogger<ReplShell>.Instance,
            new ConsolePromptEditor(keys, output, () => 80));

        await shell.RunAsync(CancellationToken.None);

        await _platform.Received(1).ExecuteAsync(
            program.ToImmediateModuleSource("Main"), program.Module, program.ImmediateEntryPoint, debug: true, immediate: false, Arg.Any<CancellationToken>());
    }

    // a line of a shell attached to a workspace calls Other.Halt, which stops at its Stop: the fifth line of the module.
    private async Task<ReplCommandContext> BreakInOtherAsync(IReplPlatformClient platform)
    {
        var program = new ReplProgram { AttachedToWorkspace = true };
        platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExecuteResult { Outcome = ExecutionOutcome.Suspended, ErrorLine = 4 }));
        platform.GetStackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new DebugStackResult
        {
            Frames =
            [
                new DebugStackFrame(0, "Halt", "Other", 4, 4),
                new DebugStackFrame(1, ReplProgram.WorkspaceImmediateEntryPointName, ReplProgram.ImmediateModuleName, 1, 0),
            ],
        }));
        platform.FindModuleAsync("Other", Arg.Any<CancellationToken>()).Returns(Task.FromResult<Uri?>(new Uri(OtherPath)));
        var context = new ReplCommandContext(program, _console, platform, new ReplDocument(program, platform), [])
        {
            Files = new MockFileSystem(new Dictionary<string, MockFileData> { [OtherPath] = new(string.Join("\r\n", Other)) }),
        };

        await ReplExecution.ExecuteAsync(context, program.ToImmediateModuleSource("Halt"), program.ImmediateEntryPoint, CancellationToken.None, debug: true,
            statement: "Halt");
        return context;
    }

    // the lines the listing wrote: the text of each, and how it was marked.
    private (string Text, ReplLineStyle Style, IReadOnlyList<ReplTextRun> Runs)[] Listed()
        => [.. _console.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine))
            .Select(call => (IReadOnlyList<ReplTextRun>)call.GetArguments()[1]!)
            .Zip(_console.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine))
                .Select(call => (ReplLineStyle)call.GetArguments()[2]!))
            .Select(line => (string.Concat(line.First.Select(run => run.Text)).Trim(), line.Second, line.First))];

    [TestMethod]
    public async Task ABreakInAModuleOfTheWorkspace_IsSaidByTheModulesLine()
    {
        var context = await BreakInOtherAsync(_platform);

        _console.Received().WriteLine(string.Format(RDCore.CLI.Resources.Repl_Break_InModuleLine, "Other", 5));
        Assert.AreEqual(ReplDebugState.Suspended, context.Debugger.State);
    }

    [TestMethod]
    public async Task ABreakInAModuleOfTheWorkspace_ListsTheLinesAroundIt_WithTheLineItWaitsBeforeMarked()
    {
        await BreakInOtherAsync(_platform);

        var listed = Listed();
        CollectionAssert.AreEqual(
            new[] { "3 Public Sub Halt()", "4     Debug.Print \"halting\"", "5     Stop", "6     Debug.Print \"resumed\"", "7 End Sub" },
            listed.Select(line => line.Text).ToArray());
        CollectionAssert.AreEqual(
            new[] { ReplLineStyle.Plain, ReplLineStyle.Plain, ReplLineStyle.CurrentStatement, ReplLineStyle.Plain, ReplLineStyle.Plain },
            listed.Select(line => line.Style).ToArray());
    }

    [TestMethod]
    public async Task ABreakInAModuleOfTheWorkspace_ListsTheLines_BeforeItSaysWhereItWaits()
    {
        await BreakInOtherAsync(_platform);

        var calls = _console.ReceivedCalls().ToList();
        var said = calls.FindIndex(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteLine)
            && call.GetArguments() is [string text] && text == string.Format(RDCore.CLI.Resources.Repl_Break_InModuleLine, "Other", 5));
        var listed = calls.FindLastIndex(call => call.GetMethodInfo().Name == nameof(IReplConsole.WriteListingLine));
        Assert.IsTrue(listed >= 0 && said > listed, $"listed last at {listed}, said at {said}");
    }

    [TestMethod]
    public async Task ABreakInAModuleOfTheWorkspace_ListsItsLines_HighlightedByTheTokensOfTheModule()
    {
        _platform.GetSemanticTokensAsync(new Uri(OtherPath), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ReplSemanticToken>>(
        [
            new ReplSemanticToken(2, 0, 6, TypeIndex("keyword"), 0),
            new ReplSemanticToken(4, 4, 4, TypeIndex("keyword"), 0),
            new ReplSemanticToken(5, 16, 9, TypeIndex("string"), 0),
        ]));

        await BreakInOtherAsync(_platform);

        var listed = Listed();
        var stop = listed.Single(line => line.Style is ReplLineStyle.CurrentStatement).Runs;
        Assert.IsTrue(stop.Contains(new ReplTextRun("Stop", ReplTextStyle.Keyword)), string.Join(" | ", stop));
        Assert.IsTrue(listed[0].Runs.Contains(new ReplTextRun("Public", ReplTextStyle.Keyword)), string.Join(" | ", listed[0].Runs));
        Assert.IsTrue(listed[3].Runs.Contains(new ReplTextRun("\"resumed\"", ReplTextStyle.String)), string.Join(" | ", listed[3].Runs));
    }

    [TestMethod]
    public async Task ABreakInAModuleTheLanguageServerCannotFind_IsSaid_AndListsNothing()
    {
        _platform.FindModuleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Uri?>(null));
        var program = new ReplProgram { AttachedToWorkspace = true };
        _platform.ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExecuteResult { Outcome = ExecutionOutcome.Suspended, ErrorLine = 4 }));
        _platform.GetStackAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new DebugStackResult { Frames = [new DebugStackFrame(0, "Halt", "Other", 4, 4)] }));
        var context = new ReplCommandContext(program, _console, _platform, new ReplDocument(program, _platform), []);

        await ReplExecution.ExecuteAsync(context, program.ToImmediateModuleSource("Halt"), program.ImmediateEntryPoint, CancellationToken.None, debug: true,
            statement: "Halt");

        _console.Received().WriteLine(string.Format(RDCore.CLI.Resources.Repl_Break_InModuleLine, "Other", 5));
        Assert.AreEqual(0, Listed().Length);
    }

    private static int TypeIndex(string name) => SemanticTokenLegend.TokenTypes.ToList().IndexOf(name);
}
