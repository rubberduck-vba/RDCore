using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.CLI.App.Console;
using static RDCore.Tests.Cli.Repl.ScriptedKeys;

namespace RDCore.Tests.Cli.Repl;

/// <summary>
/// The line of the shell's prompt as an editor has it: edited in place, the lines entered before a key away, and a completion list that drops down under the word
/// being typed.
/// </summary>
[TestClass]
public sealed class ConsolePromptEditorTests
{
    private static CompletionItem Item(string label, CompletionItemKind kind = CompletionItemKind.Variable, string? detail = null)
        => new() { Label = label, Kind = kind, Detail = detail, SortText = label.ToLowerInvariant() };

    private static readonly CompletionItem[] Members = [Item("Count", CompletionItemKind.Property, "Property Count() As Long"), Item("Add", CompletionItemKind.Method), Item("Item"), Item("Remove", CompletionItemKind.Method)];

    // the members after a dot, and nothing as a word starts: what is asked is recorded.
    private sealed class Source
    {
        public List<(string Line, int Cursor)> Asked { get; } = [];

        public IReadOnlyList<CompletionItem> Names { get; init; } = [];

        public Task<IReadOnlyList<CompletionItem>> CompleteAsync(string line, int cursor, CancellationToken token)
        {
            Asked.Add((line, cursor));
            return Task.FromResult(line[..cursor].EndsWith('.') ? Members : Names);
        }
    }

    private static (ConsolePromptEditor Editor, StringWriter Output, Source Source) Editor(ScriptedKeys keys, Source? source = null)
    {
        var output = new StringWriter();
        source ??= new Source();
        return (new ConsolePromptEditor(keys, output, () => 80, source.CompleteAsync), output, source);
    }

    private static async Task<string?> EnterAsync(ScriptedKeys keys, Source? source = null)
        => await Editor(keys, source).Editor.ReadLineAsync(CancellationToken.None);

    [TestMethod]
    public async Task WhatIsTyped_IsTheLine_AndItIsEditedWhereTheCursorIs()
        => Assert.AreEqual("x = 12", await EnterAsync(new ScriptedKeys()
            .Type("x = 2").Press(Key(ConsoleKey.LeftArrow)).Type("1").Press(Key(ConsoleKey.End), Key(ConsoleKey.Backspace)).Type("2")
            .Press(Key(ConsoleKey.Enter))));

    [TestMethod]
    public async Task EscapeClearsTheLine()
        => Assert.AreEqual("y", await EnterAsync(new ScriptedKeys().Type("100 ").Press(Key(ConsoleKey.Escape)).Type("y").Press(Key(ConsoleKey.Enter))));

    [TestMethod]
    public async Task UpAndDown_GoThroughTheLinesEnteredBefore_AndBackToWhatWasBeingTyped()
    {
        var keys = new ScriptedKeys()
            .Type("one").Press(Key(ConsoleKey.Enter))
            .Type("two").Press(Key(ConsoleKey.Enter))
            .Type("dr").Press(Key(ConsoleKey.UpArrow), Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter))
            .Type("dr").Press(Key(ConsoleKey.UpArrow), Key(ConsoleKey.DownArrow)).Type("aft").Press(Key(ConsoleKey.Enter));
        var editor = new ConsolePromptEditor(keys, new StringWriter(), () => 80) { CompleteWords = false };

        Assert.AreEqual("one", await editor.ReadLineAsync(CancellationToken.None));
        Assert.AreEqual("two", await editor.ReadLineAsync(CancellationToken.None));
        Assert.AreEqual("one", await editor.ReadLineAsync(CancellationToken.None));
        Assert.AreEqual("draft", await editor.ReadLineAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task ADot_AsksForTheMembers_AndTabPutsTheSelectedOneInPlace()
    {
        var source = new Source();
        var line = await EnterAsync(new ScriptedKeys().Type("c.").Press(Key(ConsoleKey.DownArrow), Key(ConsoleKey.Tab), Key(ConsoleKey.Enter)), source);

        Assert.AreEqual("c.Count", line, "the members in the order they rank in: Add, Count, ...");
        CollectionAssert.Contains(source.Asked, ("c.", 2));
    }

    [TestMethod]
    public async Task TheListIsNarrowed_AsTheWordIsTyped_AndEnterPutsTheNameInPlace()
        => Assert.AreEqual("c.Remove", await EnterAsync(new ScriptedKeys().Type("c.re").Press(Key(ConsoleKey.Enter), Key(ConsoleKey.Enter))));

    [TestMethod]
    public async Task EnterEntersTheLine_WhenTheWordIsTheNameAlready()
        => Assert.AreEqual("c.Count", await EnterAsync(new ScriptedKeys().Type("c.Count").Press(Key(ConsoleKey.Enter))));

    [TestMethod]
    public async Task ANameIsPutInPlaceOfTheWholeWord_AndInItsOwnCase()
        => Assert.AreEqual("c.Count = 1", await EnterAsync(new ScriptedKeys()
            .Type("c.cnt = 1").Press(Key(ConsoleKey.Home), Key(ConsoleKey.RightArrow), Key(ConsoleKey.RightArrow), Key(ConsoleKey.RightArrow))
            .Press(Key(ConsoleKey.Spacebar, control: true), Key(ConsoleKey.Tab), Key(ConsoleKey.Enter)),
            new Source { Names = [Item("Count")] }));

    [TestMethod]
    public async Task Escape_ClosesTheList_AndLeavesTheLineAsItIs()
        => Assert.AreEqual("c.", await EnterAsync(new ScriptedKeys().Type("c.").Press(Key(ConsoleKey.Escape), Key(ConsoleKey.Enter))));

    [TestMethod]
    public async Task TypingWhatANameDoesNotHaveInIt_ClosesTheList_AndIsTyped()
        => Assert.AreEqual("c.Co(", await EnterAsync(new ScriptedKeys().Type("c.Co(").Press(Key(ConsoleKey.Tab), Key(ConsoleKey.Enter))));

    [TestMethod]
    public async Task AWordThatStarts_AsksForTheNames_OnlyOnce()
    {
        var source = new Source { Names = [Item("MsgBox", CompletionItemKind.Function), Item("Mid", CompletionItemKind.Function)] };
        var line = await EnterAsync(new ScriptedKeys().Type("Msg").Press(Key(ConsoleKey.Tab), Key(ConsoleKey.Enter)), source);

        Assert.AreEqual("MsgBox", line);
        Assert.AreEqual(1, source.Asked.Count, "the list is narrowed as the word is typed, not asked for again");
    }

    [TestMethod]
    public async Task AListThatCannotBeHad_IsNoList_AndTypingGoesOn()
    {
        var keys = new ScriptedKeys().Type("c.x").Press(Key(ConsoleKey.Enter));
        var editor = new ConsolePromptEditor(keys, new StringWriter(), () => 80, (_, _, _) => throw new InvalidOperationException("the server is gone"));

        Assert.AreEqual("c.x", await editor.ReadLineAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task TheList_IsDrawnUnderTheLine_InABox_WithWhatTheSelectedItemIs()
    {
        var (editor, output, _) = Editor(new ScriptedKeys().Type("c.").Press(Key(ConsoleKey.DownArrow), Key(ConsoleKey.Escape), Key(ConsoleKey.Enter)));
        await editor.ReadLineAsync(CancellationToken.None);

        var text = output.ToString();
        StringAssert.Contains(text, "┌");
        StringAssert.Contains(text, "p Count");
        StringAssert.Contains(text, " 2/4 ");
        StringAssert.Contains(text, "Property Count() As Long");
        StringAssert.Contains(text, "\u001b[7m", "the selected item is in reverse video");
    }

    [TestMethod]
    public async Task TheDocumentationOfTheSelectedItem_IsWrappedUnderTheList()
    {
        var documentation = string.Join(' ', Enumerable.Repeat("Returns a Variant (String) containing a specified number of characters.", 3));
        var item = Item("Mid", CompletionItemKind.Function, "Function Mid(String, Start As Long, Optional Length) As Variant") with { Documentation = documentation };
        var output = new StringWriter();
        var editor = new ConsolePromptEditor(new ScriptedKeys().Type("M").Press(Key(ConsoleKey.Escape), Key(ConsoleKey.Enter)), output, () => 60,
            (_, _, _) => Task.FromResult<IReadOnlyList<CompletionItem>>([item]));

        await editor.ReadLineAsync(CancellationToken.None);

        var rows = output.ToString().Split("\u001b[1B\r").Select(row => System.Text.RegularExpressions.Regex.Replace(row, "\u001b\\[[0-9;]*[A-Za-z]", string.Empty)).ToArray();
        var described = rows.Where(row => row.Contains("characters") || row.Contains("Returns") || row.Contains("specified")).ToArray();
        Assert.IsTrue(described.Length > 1, "the documentation takes more than one line");
        Assert.IsTrue(rows.All(row => row.TrimEnd().Length <= 60), "no line of it goes past the edge of the console");
        Assert.IsTrue(rows.Any(row => row.Contains("Function Mid(")), "the signature comes first");
    }

    [TestMethod]
    public async Task FromAScript_TheLineIsReadAsItIs()
        => Assert.AreEqual("10 PRINT 1", await EnterAsync(new ScriptedKeys(interactive: false).Lines("10 PRINT 1")));
}
