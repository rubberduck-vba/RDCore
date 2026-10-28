using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI.App.Console;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.Themes;
using RDCore.SDK.Client;
using RDCore.SDK.Platform.Commands;
using RDCore.SDK.Platform.Protocol;
using System.IO.Abstractions.TestingHelpers;
using static RDCore.Tests.Cli.Repl.ScriptedKeys;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The shell's own commands are completed as they are typed, without <kbd>Ctrl</kbd>+<kbd>Space</kbd>: the editor, the shell's completion and the commands'
/// descriptors together, over a language server that answers a verb's arguments.
/// </summary>
[TestClass]
public sealed class ReplCompleteAsYouTypeTests
{
    private static (ConsolePromptEditor Editor, List<string> Asked) Editor(ScriptedKeys keys)
    {
        var asked = new List<string>();
        var platform = Substitute.For<IReplPlatformClient>();
        platform.Provides<SessionCompletion>().Returns(true);
        platform.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new CompletionList());
        platform.RegisterCommandsAsync(Arg.Any<IReadOnlyList<VerbDescriptor>>(), Arg.Any<CancellationToken>()).Returns(new SessionCommandsResult());
        platform.CompleteCommandAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SessionCommandCompletion>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var command = call.Arg<SessionCommandCompletion>();
                asked.Add(command.Verb + command.Arguments);
                return new CompletionList(new CompletionItem { Label = "CLEAR" }, new CompletionItem { Label = "TOGGLE" }, new CompletionItem { Label = "zebra" });
            });

        var themes = Substitute.For<IAppThemeService>();
        themes.Theme.Returns(new AppTheme(new AppThemeLoaderService(new MockFileSystem()).LoadBuiltInDefault()));
        themes.ThemeNames.Returns(["zebra", "amber"]);
        var dispatcher = new ReplCommandDispatcher([new BreakReplCommand(), new ThemeReplCommand(themes, new ScriptedKeys())]);
        var completion = new ReplCompletion(new ReplProgram(), platform, dispatcher);
        return (new ConsolePromptEditor(keys, new StringWriter(), () => 80, completion.CompleteAsync) { ListAfterSpace = completion.ListsAfterSpace }, asked);
    }

    [TestMethod]
    public async Task TheWordAfterAVerb_IsCompletedAsItIsTyped()
    {
        var (editor, asked) = Editor(new ScriptedKeys().Type("BREAK C").Press(Key(ConsoleKey.Tab), Key(ConsoleKey.Enter)));

        Assert.AreEqual("BREAK CLEAR", await editor.ReadLineAsync(CancellationToken.None));
        CollectionAssert.AreEqual(new[] { "BREAK " }, asked, "the options come with the space, and the letter narrows them");
    }

    [TestMethod]
    public async Task TheArgumentOfAnyDescribedVerb_IsCompletedAsItIsTyped()
    {
        var (editor, asked) = Editor(new ScriptedKeys().Type("THEME z").Press(Key(ConsoleKey.Tab), Key(ConsoleKey.Enter)));

        Assert.AreEqual("THEME zebra", await editor.ReadLineAsync(CancellationToken.None));
        CollectionAssert.AreEqual(new[] { "THEME " }, asked);
    }

    [TestMethod]
    public async Task TheArgumentIsAskedFor_WhenTheSpaceAfterTheVerbIsTyped_NotOnlyWhenALetterIs()
    {
        var (editor, asked) = Editor(new ScriptedKeys().Type("BREAK ").Press(Key(ConsoleKey.Tab), Key(ConsoleKey.Enter)));

        Assert.AreEqual("BREAK CLEAR", await editor.ReadLineAsync(CancellationToken.None), "the choices are there as the verb is left, to be taken from");
        CollectionAssert.Contains(asked, "BREAK ");
    }

    [TestMethod]
    public async Task TheListOpenedBySpace_IsNarrowedAsTheArgumentIsTyped_WithoutAskingAgain()
    {
        var (editor, asked) = Editor(new ScriptedKeys().Type("BREAK t").Press(Key(ConsoleKey.Tab), Key(ConsoleKey.Enter)));

        Assert.AreEqual("BREAK TOGGLE", await editor.ReadLineAsync(CancellationToken.None));
        Assert.AreEqual(1, asked.Count, "the options came with the space, and the letter only narrows them");
    }

    [TestMethod]
    public async Task ASpaceInALineThatIsNoCommands_AsksForNothing()
    {
        var (editor, asked) = Editor(new ScriptedKeys().Type("x = 1 ").Press(Key(ConsoleKey.Enter)));

        Assert.AreEqual("x = 1 ", await editor.ReadLineAsync(CancellationToken.None));
        Assert.AreEqual(0, asked.Count);
    }

    [TestMethod]
    public async Task ASpaceInANumberedLine_AsksForNothingOfACommand()
    {
        var (editor, asked) = Editor(new ScriptedKeys().Type("10 BREAK ").Press(Key(ConsoleKey.Enter)));

        Assert.AreEqual("10 BREAK ", await editor.ReadLineAsync(CancellationToken.None));
        Assert.AreEqual(0, asked.Count);
    }
}
