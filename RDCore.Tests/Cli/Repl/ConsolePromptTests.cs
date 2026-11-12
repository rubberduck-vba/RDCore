using RDCore.External.Interaction;
using static RDCore.Tests.Cli.Repl.ScriptedKeys;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The shell's reusable console prompts: a row of choices (<see cref="ConsoleMenu"/>), and a line that starts with a text of its own (<see cref="ConsoleLineEditor"/>).
/// </summary>
[TestClass]
public sealed class ConsolePromptTests
{
    // ---- ConsoleMenu ----

    private static ConsoleMenu Menu(int selected = 0, bool canDismiss = true) => new(["&Yes", "&No", "&Cancel"], selected, canDismiss);

    [TestMethod]
    public void TheArrowsAndTab_MoveTheSelection_AroundTheEnds()
    {
        var menu = Menu();
        Assert.AreEqual(ConsoleMenuOutcomeKind.Moved, menu.Press(Key(ConsoleKey.RightArrow)).Kind);
        Assert.AreEqual(1, menu.Selected);
        menu.Press(Key(ConsoleKey.Tab));
        menu.Press(Key(ConsoleKey.Tab));
        Assert.AreEqual(0, menu.Selected, "past the last is the first");
        menu.Press(Key(ConsoleKey.Tab, shift: true));
        Assert.AreEqual(2, menu.Selected, "before the first is the last");
        menu.Press(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(1, menu.Selected);
        menu.Press(Key(ConsoleKey.Home));
        Assert.AreEqual(0, menu.Selected);
        menu.Press(Key(ConsoleKey.End));
        Assert.AreEqual(2, menu.Selected);
    }

    [TestMethod]
    public void EnterAndSpace_ChooseTheSelectedItem()
    {
        Assert.AreEqual(new ConsoleMenuOutcome(ConsoleMenuOutcomeKind.Chosen, 1), Menu(selected: 1).Press(Key(ConsoleKey.Enter)));
        Assert.AreEqual(new ConsoleMenuOutcome(ConsoleMenuOutcomeKind.Chosen, 2), Menu(selected: 2).Press(Key(ConsoleKey.Spacebar)));
    }

    [TestMethod]
    public void TheKeyOfAnItem_ChoosesItAtOnce_WhateverItsCase()
    {
        var menu = Menu();
        Assert.AreEqual(new ConsoleMenuOutcome(ConsoleMenuOutcomeKind.Chosen, 1), menu.Press(Char('n')));
        Assert.AreEqual(1, menu.Selected);
        Assert.AreEqual(new ConsoleMenuOutcome(ConsoleMenuOutcomeKind.Chosen, 2), menu.Press(Char('C')));
        Assert.AreEqual(ConsoleMenuOutcomeKind.Ignored, menu.Press(Char('z')).Kind);
    }

    [TestMethod]
    public void AKeyThatTwoItemsShare_ChoosesNeither()
        => Assert.AreEqual(ConsoleMenuOutcomeKind.Ignored, new ConsoleMenu(["dark", "dusk", "light"]).Press(Char('d')).Kind);

    [TestMethod]
    public void Escape_Dismisses_OnlyAMenuThatCanBeDismissed()
    {
        Assert.AreEqual(ConsoleMenuOutcomeKind.Dismissed, Menu().Press(Key(ConsoleKey.Escape)).Kind);
        Assert.AreEqual(ConsoleMenuOutcomeKind.Ignored, Menu(canDismiss: false).Press(Key(ConsoleKey.Escape)).Kind);
    }

    [TestMethod]
    public void AnItemsKey_IsTheOneAfterItsAmpersand_OrItsFirstLetter()
    {
        Assert.AreEqual('X', ConsoleMenu.KeyOf("E&xit"));
        Assert.AreEqual('A', ConsoleMenu.KeyOf("abyss"));
        Assert.AreEqual('S', ConsoleMenu.KeyOf("R&&D &Save"));
        Assert.AreEqual("R&D Save", ConsoleMenu.TextOf("R&&D &Save"));
    }

    [TestMethod]
    public void TheSelectedItem_IsBetweenBrackets_AndTheOthersDoNotMove()
    {
        var menu = Menu();
        Assert.AreEqual("[ Yes ]   No     Cancel  ", menu.Render());
        menu.Press(Key(ConsoleKey.RightArrow));
        Assert.AreEqual("  Yes   [ No ]   Cancel  ", menu.Render());
        Assert.AreEqual(menu.Render().Length, Menu().Render().Length);
    }

    [TestMethod]
    public void Run_DrawsTheMenuOverItself_UntilAChoiceIsMade()
    {
        var output = new StringWriter();
        var chosen = Menu().Run(new ScriptedKeys().Press(Key(ConsoleKey.RightArrow), Key(ConsoleKey.Enter)), output, "> ", CancellationToken.None);

        Assert.AreEqual(1, chosen);
        Assert.AreEqual(3, output.ToString().Count(character => character == '\r'), "drawn when it opened, when it moved, and when it was chosen");
        StringAssert.EndsWith(output.ToString(), "> " + "  Yes   [ No ]   Cancel  ");
    }

    [TestMethod]
    public void Run_IsDismissed_ByEscape()
        => Assert.IsNull(Menu().Run(new ScriptedKeys().Press(Key(ConsoleKey.Escape)), new StringWriter(), string.Empty, CancellationToken.None));

    [TestMethod]
    public void Run_StopsWaiting_WhenItIsCancelled()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => Menu().Run(new ScriptedKeys().Press(Key(ConsoleKey.Enter)), new StringWriter(), string.Empty, stop.Token));
    }

    // ---- ConsoleLineEditor ----

    private static ConsoleLineEditor Edit(string initial, ScriptedKeys keys, out string? confirmed)
    {
        var editor = new ConsoleLineEditor(initial);
        confirmed = editor.Run(keys, new StringWriter(), "> ", ansi: false, CancellationToken.None);
        return editor;
    }

    [TestMethod]
    public void TheInitialText_StartsSelected_AndEnterKeepsIt()
    {
        var editor = new ConsoleLineEditor("default");
        Assert.IsTrue(editor.IsSelected);
        Assert.AreEqual("default", editor.Text);

        Edit("default", new ScriptedKeys().Press(Key(ConsoleKey.Enter)), out var confirmed);
        Assert.AreEqual("default", confirmed);
    }

    [TestMethod]
    public void Typing_ReplacesTheSelectedText()
    {
        Edit("default", new ScriptedKeys().Type("new").Press(Key(ConsoleKey.Enter)), out var confirmed);
        Assert.AreEqual("new", confirmed);
    }

    [TestMethod]
    public void AnArrow_GivesUpTheSelection_AndTypingInsertsAtTheCursor()
    {
        Edit("abc", new ScriptedKeys().Press(Key(ConsoleKey.LeftArrow)).Type("x").Press(Key(ConsoleKey.End)).Type("y").Press(Key(ConsoleKey.Enter)), out var confirmed);
        Assert.AreEqual("xabcy", confirmed, "Left over a selection goes to its start");
    }

    [TestMethod]
    public void BackspaceAndDelete_EraseTheSelection_OrACharacter()
    {
        Edit("abc", new ScriptedKeys().Press(Key(ConsoleKey.Backspace), Key(ConsoleKey.Enter)), out var erased);
        Assert.AreEqual(string.Empty, erased);

        Edit("abc", new ScriptedKeys().Press(Key(ConsoleKey.End), Key(ConsoleKey.Backspace), Key(ConsoleKey.Home), Key(ConsoleKey.Delete), Key(ConsoleKey.Enter)), out var trimmed);
        Assert.AreEqual("b", trimmed);
    }

    [TestMethod]
    public void ControlA_SelectsTheWholeTextAgain()
    {
        Edit("abc", new ScriptedKeys().Press(Key(ConsoleKey.End), Key(ConsoleKey.A, control: true)).Type("z").Press(Key(ConsoleKey.Enter)), out var confirmed);
        Assert.AreEqual("z", confirmed);
    }

    [TestMethod]
    public void Escape_AbandonsTheLine_WhichIsNotAnEmptyOne()
    {
        Edit("abc", new ScriptedKeys().Press(Key(ConsoleKey.Escape)), out var abandoned);
        Edit("abc", new ScriptedKeys().Press(Key(ConsoleKey.Backspace), Key(ConsoleKey.Enter)), out var empty);

        Assert.IsNull(abandoned);
        Assert.AreEqual(string.Empty, empty);
    }

    [TestMethod]
    public void TheSelection_IsShownInReverseVideo_WhenTheConsoleUnderstandsAnsi()
    {
        var ansi = new StringWriter();
        new ConsoleLineEditor("abc").Run(new ScriptedKeys().Press(Key(ConsoleKey.Escape)), ansi, "> ", ansi: true, CancellationToken.None);
        var plain = new StringWriter();
        new ConsoleLineEditor("abc").Run(new ScriptedKeys().Press(Key(ConsoleKey.Escape)), plain, "> ", ansi: false, CancellationToken.None);

        StringAssert.Contains(ansi.ToString(), "\u001b[7mabc\u001b[27m");
        Assert.IsFalse(plain.ToString().Contains('\u001b'));
        StringAssert.Contains(plain.ToString(), "> abc");
    }
}
