using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.Themes;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Platform.Protocol;
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

    // the shell's own commands, with a language server that would answer with VBA names were it asked of code, and answers a command with what it was asked.
    private static (ReplCompletion Completion, IReplPlatformClient Platform) WithCommands()
    {
        var platform = Substitute.For<IReplPlatformClient>();
        platform.Provides<SessionCompletion>().Returns(true);
        platform.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new CompletionList(new CompletionItem { Label = "I" }));
        platform.CompleteCommandAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SessionCommandCompletion>(), Arg.Any<CancellationToken>())
            .Returns(call => new CompletionList(new CompletionItem { Label = call.Arg<SessionCommandCompletion>().Verb }));
        platform.RegisterCommandsAsync(Arg.Any<IReadOnlyList<VerbDescriptor>>(), Arg.Any<CancellationToken>()).Returns(new SessionCommandsResult());

        var themes = Substitute.For<IAppThemeService>();
        themes.Theme.Returns(new AppTheme(new AppThemeLoaderService(new MockFileSystem()).LoadBuiltInDefault()));
        themes.ThemeNames.Returns(["zebra", "amber"]);
        var load = Substitute.For<IReplCommand>();
        load.Name.Returns("LOAD");
        load.Aliases.Returns([]);

        var dispatcher = new ReplCommandDispatcher([new BreakReplCommand(), new ThemeReplCommand(themes, new ScriptedKeys()), load]);
        return (new ReplCompletion(Program(), platform, dispatcher), platform);
    }

    [TestMethod]
    public async Task TheDescribedVerbs_AreRegisteredOnce_AndAVerbAgainOnlyWhenItsDescriptorChanges()
    {
        var platform = Substitute.For<IReplPlatformClient>();
        platform.Provides<SessionCompletion>().Returns(true);
        platform.CompleteCommandAsync(default!, default!, default!, default).ReturnsForAnyArgs(new CompletionList());
        var registered = new List<string[]>();
        platform.RegisterCommandsAsync(Arg.Any<IReadOnlyList<VerbDescriptor>>(), Arg.Any<CancellationToken>())
            .Returns(call => { registered.Add([.. call.Arg<IReadOnlyList<VerbDescriptor>>().Select(verb => verb.Name)]); return new SessionCommandsResult(); });

        var themes = Substitute.For<IAppThemeService>();
        var dark = new AppTheme(new AppThemeLoaderService(new MockFileSystem()).LoadBuiltInDefault());
        themes.Theme.Returns(dark);
        themes.ThemeNames.Returns([dark.Name, "light"]);
        var completion = new ReplCompletion(Program(), platform, new ReplCommandDispatcher([new BreakReplCommand(), new ThemeReplCommand(themes, new ScriptedKeys())]));

        await completion.CompleteAsync("BREAK ", 6, CancellationToken.None);
        await completion.CompleteAsync("THEME ", 6, CancellationToken.None);
        await completion.CompleteAsync("BREAK 1", 7, CancellationToken.None);

        Assert.AreEqual(1, registered.Count, "once");
        CollectionAssert.AreEquivalent(new[] { "BREAK", "THEME" }, registered[0]);

        // a theme is added: THEME's choices are not what they were, and it alone is registered again.
        themes.ThemeNames.Returns([dark.Name, "light", "amber"]);
        await completion.CompleteAsync("THEME ", 6, CancellationToken.None);
        Assert.AreEqual(2, registered.Count);
        CollectionAssert.AreEqual(new[] { "THEME" }, registered[1]);
    }

    private static async Task<string[]> LabelsAsync(ReplCompletion completion, string line)
        => [.. (await completion.CompleteAsync(line, line.Length, CancellationToken.None)).Select(item => item.Label)];

    [TestMethod]
    public async Task TheArgumentsOfACommand_AreNotVba_AndAreOfferedNothing_UnlessItDescribesThem()
    {
        var (completion, platform) = WithCommands();

        Assert.AreEqual(0, (await LabelsAsync(completion, @"LOAD c:\dev\fi")).Length);
        Assert.AreEqual(0, (await LabelsAsync(completion, "load ")).Length);
        await platform.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, default, default, default);
        await platform.DidNotReceiveWithAnyArgs().CompleteCommandAsync(default!, default!, default!, default);
    }

    [TestMethod]
    public async Task ACommandThatDescribesItsArguments_IsCompletedByTheLanguageServer_InTheProgramItActsOn()
    {
        var (completion, platform) = WithCommands();

        CollectionAssert.AreEqual(new[] { "break" }, await LabelsAsync(completion, "break 30 C."));
        await platform.Received(1).CompleteCommandAsync(
            Program().ToModuleSource(),
            ReplProgram.ModuleName,
            Arg.Is<SessionCommandCompletion>(command => command.Arguments == " 30 C." && command.Verb == "break"),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void Break_DescribesItsWords_ItsLines_AndItsCondition()
    {
        var forms = new BreakReplCommand().Descriptor.Forms;

        CollectionAssert.AreEqual(new[] { "CLEAR" }, forms[0].Single().Values.Select(value => value.Value).ToArray());
        CollectionAssert.AreEqual(new[] { VerbArgumentKind.Keyword, VerbArgumentKind.LineNumber }, forms[1].Select(argument => argument.Kind).ToArray());
        CollectionAssert.AreEqual(new[] { VerbArgumentKind.LineNumber, VerbArgumentKind.Expression }, forms[2].Select(argument => argument.Kind).ToArray());
        Assert.AreEqual(forms[2][0].Name, forms[2][1].EvaluatedAt, "the condition is evaluated at its line, where the program waits");
    }

    [TestMethod]
    public void Theme_DescribesList_AndTheThemesThereAre()
    {
        var themes = Substitute.For<IAppThemeService>();
        var current = new AppTheme(new AppThemeLoaderService(new MockFileSystem()).LoadBuiltInDefault());
        themes.Theme.Returns(current);
        themes.ThemeNames.Returns(["zebra", current.Name]);

        var forms = new ThemeReplCommand(themes, new ScriptedKeys()).Descriptor.Forms;

        Assert.AreEqual("LIST", forms[0].Single().Values.Single().Value);
        var choices = forms[1].Single().Values;
        CollectionAssert.AreEqual(new[] { current.Name, "zebra" }.Order(StringComparer.OrdinalIgnoreCase).ToArray(), choices.Select(value => value.Value).ToArray());
        Assert.AreEqual("🎨", choices.Single(value => value.Value == current.Name).Description, "the current theme is marked");
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
