using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.Themes;
using RDCore.SDK.Client;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The line at the prompt is completed in the module it will be part of once it is entered: the program with a numbered line stored at its number, or the module
/// an immediate statement runs as.
/// </summary>
[TestClass]
public sealed class ReplCompletionTests
{
    private static ReplProgram Program()
    {
        var program = new ReplProgram();
        program.Store(10, "Dim C As New Collection");
        program.Store(30, "Print C.Count");
        return program;
    }

    private static string LineAt(string source, int line) => source.Split("\r\n")[line];

    [TestMethod]
    public void ANumberedLine_IsCompletedWhereItWillBe_InTheProgram()
    {
        var (source, line, character) = Program().ToCompletionSource("20 C.", 5)!.Value;

        Assert.AreEqual("20 C.", LineAt(source, line), "between lines 10 and 30");
        Assert.AreEqual(2, line);
        Assert.AreEqual(5, character);
        StringAssert.Contains(source, "30 Print C.Count");
    }

    [TestMethod]
    public void ANumberedLineThatReplacesOne_IsCompletedInItsPlace()
    {
        var (source, line, _) = Program().ToCompletionSource("30   C.", 7)!.Value;

        Assert.AreEqual("30 C.", LineAt(source, line));
        Assert.IsFalse(source.Contains("Print C.Count"), "the line typed replaces the one of its number");
    }

    [TestMethod]
    public void AnImmediateStatement_IsCompletedInTheProcedureItRunsAs()
    {
        var (source, line, character) = Program().ToCompletionSource("  C.Co", 6)!.Value;

        Assert.AreEqual("C.Co", LineAt(source, line));
        Assert.AreEqual(4, character);
        Assert.AreEqual($"Public Sub {ReplProgram.ImmediateEntryPointName}()", LineAt(source, line - 1));
    }

    [TestMethod]
    public void AQuestionMark_IsThePrintItExpandsTo()
    {
        var (source, line, character) = Program().ToCompletionSource("?  C.", 5)!.Value;

        Assert.AreEqual("Print C.", LineAt(source, line));
        Assert.AreEqual(8, character);
    }

    [TestMethod]
    public void NothingIsCompleted_InALineNumber()
        => Assert.IsNull(Program().ToCompletionSource("20", 2));

    [TestMethod]
    public async Task TheFirstWordOfALine_IsOfferedTheCommands_AndTheNames()
    {
        var platform = Substitute.For<IReplPlatformClient>();
        platform.Provides<SessionCompletion>().Returns(true);
        platform.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new CompletionList(new CompletionItem { Label = "Run" }, new CompletionItem { Label = "C" }));
        var commands = Substitute.For<IReplCommandDispatcher>();
        var run = Substitute.For<IReplCommand>();
        run.Name.Returns("RUN");
        run.Summary.Returns("Runs the program.");
        commands.Commands.Returns([run]);

        var completion = new ReplCompletion(Program(), platform, commands);
        var first = await completion.CompleteAsync("R", 1, CancellationToken.None);
        var later = await completion.CompleteAsync("x = R", 5, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "RUN", "C" }, first.Select(item => item.Label).ToArray(), "a command and a name of the same name are one item");
        CollectionAssert.AreEqual(new[] { "Run", "C" }, later.Select(item => item.Label).ToArray(), "a command is only ever the first word");
    }

    // the shell's own commands, with a language server that would answer with VBA names were it asked.
    private static (ReplCompletion Completion, IReplPlatformClient Platform) WithCommands()
    {
        var platform = Substitute.For<IReplPlatformClient>();
        platform.Provides<SessionCompletion>().Returns(true);
        platform.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new CompletionList(new CompletionItem { Label = "I" }));

        var themes = Substitute.For<IAppThemeService>();
        themes.Theme.Returns(new AppTheme(new AppThemeLoaderService(new MockFileSystem()).LoadBuiltInDefault()));
        themes.ThemeNames.Returns(["zebra", "amber"]);
        var load = Substitute.For<IReplCommand>();
        load.Name.Returns("LOAD");
        load.Aliases.Returns([]);

        var dispatcher = new ReplCommandDispatcher([new BreakReplCommand(), new ThemeReplCommand(themes, new ScriptedKeys()), load]);
        return (new ReplCompletion(Program(), platform, dispatcher), platform);
    }

    private static async Task<string[]> LabelsAsync(ReplCompletion completion, string line)
        => [.. (await completion.CompleteAsync(line, line.Length, CancellationToken.None)).Select(item => item.Label)];

    [TestMethod]
    public async Task TheArgumentsOfACommand_AreNotVba_AndAreOfferedNothing_UnlessItSaysWhatTheyCanBe()
    {
        var (completion, platform) = WithCommands();

        Assert.AreEqual(0, (await LabelsAsync(completion, @"LOAD c:\dev\fi")).Length);
        Assert.AreEqual(0, (await LabelsAsync(completion, "load ")).Length);
        await platform.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, default, default, default);
    }

    [TestMethod]
    public async Task Break_IsOfferedItsWords_AndTheLinesOfTheProgram()
    {
        var (completion, _) = WithCommands();

        CollectionAssert.AreEqual(new[] { "TOGGLE", "CLEAR", "10", "30" }, await LabelsAsync(completion, "BREAK "));
        CollectionAssert.AreEqual(new[] { "TOGGLE", "CLEAR", "10", "30" }, await LabelsAsync(completion, "break 1"));
        CollectionAssert.AreEqual(new[] { "10", "30" }, await LabelsAsync(completion, "BREAK TOGGLE "));
        Assert.AreEqual(0, (await LabelsAsync(completion, "BREAK 30 C.")).Length, "a condition is the program's VBA, and no word of BREAK's");
    }

    [TestMethod]
    public async Task Theme_IsOfferedList_AndTheThemes()
    {
        var (completion, _) = WithCommands();

        CollectionAssert.AreEqual(new[] { "LIST", "amber", "zebra" }, await LabelsAsync(completion, "THEME "));
        Assert.AreEqual(0, (await LabelsAsync(completion, "THEME amber ")).Length);
    }

    [TestMethod]
    public async Task ALanguageServerWithoutTheCapability_IsNotAsked()
    {
        var platform = Substitute.For<IReplPlatformClient>();
        var completion = new ReplCompletion(Program(), platform, Substitute.For<IReplCommandDispatcher>());

        Assert.AreEqual(0, (await completion.CompleteAsync("x = C.", 6, CancellationToken.None)).Count);
        await platform.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, default, default, default);
    }
}
