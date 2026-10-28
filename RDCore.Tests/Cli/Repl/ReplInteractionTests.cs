using NSubstitute;
using RDCore.CLI.App.Console;
using RDCore.CLI.App.Repl;
using RDCore.CLI.App.Repl.Commands;
using RDCore.CLI.Themes;
using RDCore.External.Interaction;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.StdLib;
using System.IO.Abstractions.TestingHelpers;
using static RDCore.Tests.Cli.Repl.ScriptedKeys;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The dialogs of a program run from the shell, asked at its console: a <c>MsgBox</c> is a menu of its buttons, an <c>InputBox</c> a line to type on, and
/// <c>THEME LIST</c> is a menu of the same make.
/// </summary>
[TestClass]
public sealed class ReplInteractionTests
{
    private static (ReplUserInteraction Interaction, StringWriter Output) Shell(ScriptedKeys keys)
    {
        var output = new StringWriter();
        return (new ReplUserInteraction(keys, output), output);
    }

    // ---- MsgBox ----

    [TestMethod]
    public void AMessage_IsItsTitleAndPrompt_ThenItsButtons_TheDefaultOneSelected()
    {
        var (interaction, output) = Shell(new ScriptedKeys().Press(Key(ConsoleKey.Enter)));

        var answer = interaction.ShowMessage(new MessageBoxRequest("Line one\r\nLine two", "Project1", VBMsgBoxStyle.VBYesNoCancel | VBMsgBoxStyle.VBExclamation | VBMsgBoxStyle.VBDefaultButton2));

        Assert.AreEqual(VBMsgBoxResult.VBNo, answer);
        var text = output.ToString();
        StringAssert.Contains(text, "═ Project1 ═");
        StringAssert.Contains(text, "║  ⚠️ Line one");
        StringAssert.Contains(text, "║     Line two");
        StringAssert.Contains(text, "[ No ]");
    }

    [TestMethod]
    public void AMessage_IsABox_CentredOnTheConsole_ItsEdgesLinedUp()
    {
        var output = new StringWriter();
        var interaction = new ReplUserInteraction(new ScriptedKeys(interactive: false), output, consoleWidth: () => 100);

        interaction.ShowMessage(new MessageBoxRequest("Saved.", "Project1", VBMsgBoxStyle.VBOKOnly | VBMsgBoxStyle.VBInformation));

        var lines = output.ToString().ReplaceLineEndings("\n").Split('\n').Where(line => line.Trim().Length > 0).ToArray();
        Assert.IsTrue(lines.First().TrimStart().StartsWith('╔') && lines.Last().TrimStart().StartsWith('╚'), "the box is drawn top to bottom");
        Assert.AreEqual(1, lines.Select(ConsoleText.WidthOf).Distinct().Count(), "every line of the box ends at its right edge");

        var margin = lines[0].Length - lines[0].TrimStart().Length;
        Assert.IsTrue(margin > 0);
        Assert.AreEqual(margin, 100 - ConsoleText.WidthOf(lines[0]), 1, "the box is centred");
    }

    [TestMethod]
    public void AMessageTooLongForALine_IsWrapped_InsideTheBox()
    {
        var output = new StringWriter();
        var interaction = new ReplUserInteraction(new ScriptedKeys(interactive: false), output, consoleWidth: () => 60);

        interaction.ShowMessage(new MessageBoxRequest(string.Join(' ', Enumerable.Repeat("duck", 40)), "t", VBMsgBoxStyle.VBOKOnly));

        var lines = output.ToString().ReplaceLineEndings("\n").Split('\n').Where(line => line.Trim().Length > 0).ToArray();
        Assert.IsTrue(lines.Count(line => line.Contains("duck")) > 1);
        Assert.IsTrue(lines.All(line => ConsoleText.WidthOf(line) <= 60), "nothing goes past the edge of the console");
        Assert.AreEqual(1, lines.Select(ConsoleText.WidthOf).Distinct().Count());
    }

    [TestMethod]
    public void WithAnsi_TheMessageAndTheRightEdge_ArePutAtTheirColumns_HoweverWideTheIconIsDrawn()
    {
        var output = new StringWriter();
        var interaction = new ReplUserInteraction(new ScriptedKeys(interactive: false), output, ansi: true, consoleWidth: () => 80);

        interaction.ShowMessage(new MessageBoxRequest("Line one\r\nLine two", "t", VBMsgBoxStyle.VBOKOnly | VBMsgBoxStyle.VBExclamation));

        var rows = output.ToString().ReplaceLineEndings("\n").Split('\n').Where(line => line.Contains("Line ")).ToArray();
        Assert.AreEqual(2, rows.Length);

        // the column the text is put at is the same under the icon as beside it, and so is the one of the right edge.
        var columns = rows.Select(row => System.Text.RegularExpressions.Regex.Matches(row, "\u001b\\[(\\d+)G").Select(match => match.Groups[1].Value).ToArray()).ToArray();
        CollectionAssert.AreEqual(columns[0], columns[1]);
        StringAssert.Contains(rows[0], "⚠️\u001b[");
        Assert.AreEqual(2, columns[0].Length, "the text, then the right edge");
    }

    [TestMethod]
    public void WithAnsi_TheWholeBoxIsDrawn_BeforeTheButtonsAreChosen()
    {
        var output = new StringWriter();
        var interaction = new ReplUserInteraction(new ScriptedKeys().Press(Key(ConsoleKey.Enter)), output, ansi: true);

        interaction.ShowMessage(new MessageBoxRequest("x", "t", VBMsgBoxStyle.VBOKOnly));

        var text = output.ToString();
        Assert.IsTrue(text.IndexOf('╝') < text.IndexOf("[ OK ]", StringComparison.Ordinal), "the bottom edge is there before the buttons are drawn in it");
        StringAssert.Contains(text, "\u001b[2A", "the cursor goes back up to the row of buttons");
        StringAssert.Contains(text, "\u001b[1B", "and past the box once one is chosen");
    }

    [TestMethod]
    public void TheButtonChosen_IsTheAnswer()
    {
        var (interaction, _) = Shell(new ScriptedKeys().Press(Key(ConsoleKey.RightArrow), Key(ConsoleKey.RightArrow), Key(ConsoleKey.Enter)));
        Assert.AreEqual(VBMsgBoxResult.VBIgnore, interaction.ShowMessage(new MessageBoxRequest("x", "t", VBMsgBoxStyle.VBAbortRetryIgnore)));
    }

    [TestMethod]
    public void AButton_IsChosenByItsKey()
    {
        var (interaction, _) = Shell(new ScriptedKeys().Press(Char('r')));
        Assert.AreEqual(VBMsgBoxResult.VBRetry, interaction.ShowMessage(new MessageBoxRequest("x", "t", VBMsgBoxStyle.VBRetryCancel | VBMsgBoxStyle.VBDefaultButton2)));
    }

    [TestMethod]
    [DataRow(VBMsgBoxStyle.VBOKCancel, VBMsgBoxResult.VBCancel)]
    [DataRow(VBMsgBoxStyle.VBYesNoCancel, VBMsgBoxResult.VBCancel)]
    [DataRow(VBMsgBoxStyle.VBOKOnly, VBMsgBoxResult.VBOK)]
    public void Escape_ClosesTheMessage_AsItsCloseButtonWould(VBMsgBoxStyle style, VBMsgBoxResult expected)
    {
        var (interaction, _) = Shell(new ScriptedKeys().Press(Key(ConsoleKey.Escape)));
        Assert.AreEqual(expected, interaction.ShowMessage(new MessageBoxRequest("x", "t", style)));
    }

    [TestMethod]
    public void Escape_DoesNothing_ToAMessageThatCannotBeClosedButByChoosing()
    {
        var (interaction, _) = Shell(new ScriptedKeys().Press(Key(ConsoleKey.Escape), Char('n')));
        Assert.AreEqual(VBMsgBoxResult.VBNo, interaction.ShowMessage(new MessageBoxRequest("x", "t", VBMsgBoxStyle.VBYesNo)));
    }

    [TestMethod]
    public void FromAScript_AButtonIsNamedOnALine_AndAnEmptyLineIsTheDefault()
    {
        var request = new MessageBoxRequest("x", "t", VBMsgBoxStyle.VBYesNoCancel);
        Assert.AreEqual(VBMsgBoxResult.VBNo, Shell(new ScriptedKeys(interactive: false).Lines("maybe", "no")).Interaction.ShowMessage(request));
        Assert.AreEqual(VBMsgBoxResult.VBCancel, Shell(new ScriptedKeys(interactive: false).Lines("C")).Interaction.ShowMessage(request));
        Assert.AreEqual(VBMsgBoxResult.VBYes, Shell(new ScriptedKeys(interactive: false).Lines("")).Interaction.ShowMessage(request));
        Assert.AreEqual(VBMsgBoxResult.VBCancel, Shell(new ScriptedKeys(interactive: false)).Interaction.ShowMessage(request), "the end of the script closes it as Esc would");
    }

    // ---- InputBox ----

    [TestMethod]
    public void AnInputBox_StartsWithItsDefault_AndEnterConfirmsIt()
    {
        var (interaction, output) = Shell(new ScriptedKeys().Press(Key(ConsoleKey.Enter)));
        Assert.AreEqual("42", interaction.Prompt(new InputBoxRequest("How many?", "Project1", "42")));
        StringAssert.Contains(output.ToString(), "═ Project1 ═");
        StringAssert.Contains(output.ToString(), "║  How many?");
        StringAssert.Contains(output.ToString(), "║  > 42");
    }

    [TestMethod]
    public void TheLineOfAnInputBox_StaysInsideTheBox_HoweverLongWhatIsTyped()
    {
        var output = new StringWriter();
        var interaction = new ReplUserInteraction(new ScriptedKeys().Type(new string('x', 200)).Press(Key(ConsoleKey.Enter)), output, consoleWidth: () => 80);

        Assert.AreEqual(new string('x', 200), interaction.Prompt(new InputBoxRequest("Name?", "t")));

        // each time the line is drawn, it is drawn from the start of the row to its right edge, and never past it.
        foreach (var drawn in output.ToString().Split('\r').Where(part => part.Contains("> ")))
        {
            var row = drawn.Split('\b')[0];
            StringAssert.EndsWith(row, "  ║");
            Assert.IsTrue(ConsoleText.WidthOf(row) <= 80);
        }
    }

    [TestMethod]
    public void WhatIsTyped_ReplacesTheDefault()
    {
        var (interaction, _) = Shell(new ScriptedKeys().Type("Duck").Press(Key(ConsoleKey.Enter)));
        Assert.AreEqual("Duck", interaction.Prompt(new InputBoxRequest("Name?", "t", "you")));
    }

    [TestMethod]
    public void Escape_CancelsTheInputBox_WhichIsNotConfirmingItEmpty()
    {
        Assert.IsNull(Shell(new ScriptedKeys().Press(Key(ConsoleKey.Escape))).Interaction.Prompt(new InputBoxRequest("x", "t", "default")));
        Assert.AreEqual(string.Empty, Shell(new ScriptedKeys().Press(Key(ConsoleKey.Enter))).Interaction.Prompt(new InputBoxRequest("x", "t")));
    }

    [TestMethod]
    public void FromAScript_TheLineIsTheText_AndTheEndOfTheScriptCancels()
    {
        Assert.AreEqual("typed", Shell(new ScriptedKeys(interactive: false).Lines("typed")).Interaction.Prompt(new InputBoxRequest("x", "t")));
        Assert.IsNull(Shell(new ScriptedKeys(interactive: false)).Interaction.Prompt(new InputBoxRequest("x", "t")));
    }

    // ---- the handlers ----

    [TestMethod]
    public async Task TheMessageBoxHandler_WritesWhatWasPrintedFirst_ThenAnswers()
    {
        var console = Substitute.For<IReplConsole>();
        var (interaction, _) = Shell(new ScriptedKeys().Press(Char('n')));

        var answer = await new ReplMessageBoxHandler(interaction, console).Handle(
            new SessionMessageBoxParams { Prompt = "Sure?", Title = "t", Style = (int)VBMsgBoxStyle.VBYesNo, Output = ["one", "two"] }, CancellationToken.None);

        Assert.AreEqual(new MessageBoxResult { Handled = true, Result = (int)VBMsgBoxResult.VBNo }, answer);
        Received.InOrder(() =>
        {
            console.WriteLine("one");
            console.WriteLine("two");
        });
    }

    [TestMethod]
    public async Task TheInputBoxHandler_SaysWhetherItWasCancelled()
    {
        var console = Substitute.For<IReplConsole>();
        var cancelled = await new ReplInputBoxHandler(Shell(new ScriptedKeys().Press(Key(ConsoleKey.Escape))).Interaction, console)
            .Handle(new SessionInputBoxParams { Prompt = "x", Default = "d" }, CancellationToken.None);
        var typed = await new ReplInputBoxHandler(Shell(new ScriptedKeys().Type("hi").Press(Key(ConsoleKey.Enter))).Interaction, console)
            .Handle(new SessionInputBoxParams { Prompt = "x" }, CancellationToken.None);

        Assert.AreEqual(new InputBoxResult { Handled = true, Cancelled = true }, cancelled);
        Assert.AreEqual(new InputBoxResult { Handled = true, Value = "hi" }, typed);
    }

    // ---- THEME LIST ----

    private static (ThemeReplCommand Command, IAppThemeService Themes, ReplCommandContext Context, StringWriter Output, string Current) Themes(ScriptedKeys keys)
    {
        var current = new AppTheme(new AppThemeLoaderService(new MockFileSystem()).LoadBuiltInDefault());
        var themes = Substitute.For<IAppThemeService>();
        themes.Theme.Returns(current);
        themes.ThemeNames.Returns(["zzz-last", current.Name, "aaa-first"]);
        var console = Substitute.For<IReplConsole>();
        var output = new StringWriter();
        var context = new ReplCommandContext(new ReplProgram(), console, Substitute.For<IReplPlatformClient>(), new ReplDocument(new ReplProgram(), Substitute.For<IReplPlatformClient>()), []);
        return (new ThemeReplCommand(themes, keys, output), themes, context, output, current.Name);
    }

    [TestMethod]
    public async Task ThemeList_IsAMenu_InOrderOfTheirNames_WithTheCurrentThemeSelected()
    {
        var (command, themes, context, output, current) = Themes(new ScriptedKeys().Press(Key(ConsoleKey.Escape)));
        await command.ExecuteAsync(context, "LIST", CancellationToken.None);

        StringAssert.Contains(output.ToString(), $"  aaa-first   [ {current} ]   zzz-last  ");
        themes.DidNotReceive().SetTheme(Arg.Any<string>());
    }

    [TestMethod]
    public async Task ThemeList_AppliesTheThemeChosen()
    {
        var (command, themes, context, _, _) = Themes(new ScriptedKeys().Press(Key(ConsoleKey.RightArrow), Key(ConsoleKey.Enter)));
        await command.ExecuteAsync(context, "list", CancellationToken.None);
        themes.Received().SetTheme("zzz-last");
    }

    [TestMethod]
    public async Task ThemeList_ChoosingTheCurrentTheme_ChangesNothing()
    {
        var (command, themes, context, _, _) = Themes(new ScriptedKeys().Press(Key(ConsoleKey.Enter)));
        await command.ExecuteAsync(context, "LIST", CancellationToken.None);
        themes.DidNotReceive().SetTheme(Arg.Any<string>());
    }

    [TestMethod]
    public async Task ThemeList_FromAScript_IsTheList_WithTheCurrentThemeMarked()
    {
        var (command, themes, context, _, current) = Themes(new ScriptedKeys(interactive: false));
        await command.ExecuteAsync(context, "LIST", CancellationToken.None);

        context.Console.Received().WriteLine($"   🎨\t {current}");
        context.Console.Received().WriteLine("\t aaa-first");
        themes.DidNotReceive().SetTheme(Arg.Any<string>());
    }

    [TestMethod]
    public async Task ThemeByName_StillSetsIt()
    {
        var (command, themes, context, _, _) = Themes(new ScriptedKeys());
        await command.ExecuteAsync(context, "AAA-FIRST", CancellationToken.None);
        themes.Received().SetTheme("aaa-first");
    }
}
