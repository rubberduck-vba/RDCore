using System.Text;

namespace RDCore.CLI.App.Console;

/// <summary>
/// What a key did to a <see cref="ConsoleLineEditor"/>.
/// </summary>
public enum ConsoleLineEditorOutcome
{
    /// <summary>The line was edited, or the cursor moved - or nothing happened at all.</summary>
    Editing,

    /// <summary>The line was confirmed: <kbd>Enter</kbd>.</summary>
    Confirmed,

    /// <summary>The line was abandoned: <kbd>Esc</kbd>.</summary>
    Cancelled,
}

/// <summary>
/// A line of text typed at the console, which starts with a text of its own - selected, as the text box of a dialog starts with its default text selected, so that
/// typing replaces it and <kbd>Enter</kbd> keeps it.
/// </summary>
/// <remarks>
/// <para>
/// A character is typed at the cursor, or over the whole text while it is selected. <kbd>Backspace</kbd> and <kbd>Delete</kbd> erase the character before and
/// after the cursor, or the whole text while it is selected. The arrow keys, <kbd>Home</kbd> and <kbd>End</kbd> move the cursor, and give up the selection;
/// <kbd>Ctrl</kbd>+<kbd>A</kbd> selects the whole text again. <kbd>Enter</kbd> confirms the line, and <kbd>Esc</kbd> abandons it.
/// </para>
/// <para>
/// What a key does (<see cref="Press"/>) is apart from how the line is drawn and from where the keys come from (<see cref="Run"/>).
/// </para>
/// </remarks>
/// <param name="initial">The text the line starts with.</param>
/// <param name="selected">Whether it starts selected.</param>
public sealed class ConsoleLineEditor(string initial, bool selected = true)
{
    private const string ReverseVideo = "\u001b[7m";
    private const string NormalVideo = "\u001b[27m";

    private readonly StringBuilder _text = new(initial);

    /// <summary>The text of the line.</summary>
    public string Text => _text.ToString();

    /// <summary>Where the cursor is: the index in <see cref="Text"/> a character is typed at.</summary>
    public int Cursor { get; private set; } = initial.Length;

    /// <summary>Whether the whole text is selected.</summary>
    public bool IsSelected { get; private set; } = selected && initial.Length > 0;

    /// <summary>
    /// What a key does to the line.
    /// </summary>
    /// <param name="key">The key.</param>
    public ConsoleLineEditorOutcome Press(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                return ConsoleLineEditorOutcome.Confirmed;
            case ConsoleKey.Escape:
                return ConsoleLineEditorOutcome.Cancelled;
            case ConsoleKey.Backspace:
                if (!EraseSelection() && Cursor > 0)
                {
                    _text.Remove(--Cursor, 1);
                }

                return ConsoleLineEditorOutcome.Editing;
            case ConsoleKey.Delete:
                if (!EraseSelection() && Cursor < _text.Length)
                {
                    _text.Remove(Cursor, 1);
                }

                return ConsoleLineEditorOutcome.Editing;
            case ConsoleKey.LeftArrow:
                Cursor = IsSelected ? 0 : Math.Max(0, Cursor - 1);
                IsSelected = false;
                return ConsoleLineEditorOutcome.Editing;
            case ConsoleKey.RightArrow:
                Cursor = IsSelected ? _text.Length : Math.Min(_text.Length, Cursor + 1);
                IsSelected = false;
                return ConsoleLineEditorOutcome.Editing;
            case ConsoleKey.Home:
                (Cursor, IsSelected) = (0, false);
                return ConsoleLineEditorOutcome.Editing;
            case ConsoleKey.End:
                (Cursor, IsSelected) = (_text.Length, false);
                return ConsoleLineEditorOutcome.Editing;
            case ConsoleKey.A when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                (Cursor, IsSelected) = (_text.Length, _text.Length > 0);
                return ConsoleLineEditorOutcome.Editing;
        }

        if (key.KeyChar is var typed and not '\0' && !char.IsControl(typed))
        {
            EraseSelection();
            _text.Insert(Cursor++, typed);
        }

        return ConsoleLineEditorOutcome.Editing;
    }

    private bool EraseSelection()
    {
        if (!IsSelected)
        {
            return false;
        }

        _text.Clear();
        (Cursor, IsSelected) = (0, false);
        return true;
    }

    /// <summary>
    /// Draws the line after <paramref name="prompt"/> at the start of the current line, and reads keys until it is confirmed or abandoned. The line is drawn again
    /// over itself after each key, with the cursor where a character would be typed.
    /// </summary>
    /// <param name="keys">Where the keys come from.</param>
    /// <param name="output">Where the line is drawn.</param>
    /// <param name="prompt">What the line starts with.</param>
    /// <param name="ansi">Whether the output understands ANSI escape sequences, which show the selection in reverse video; without them it is not shown, and is there all the same.</param>
    /// <param name="token">Stops waiting for a key.</param>
    /// <param name="width">
    /// The characters of the field the text is shown in, which scrolls to keep the cursor in it when the text is longer; <c>0</c> - the default - shows the whole
    /// text, however long.
    /// </param>
    /// <param name="suffix">What the line ends with, after the field: the right edge of the box the line is in, say.</param>
    /// <returns>The text confirmed, or <see langword="null"/> when the line was abandoned.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="token"/> was cancelled before the line was confirmed.</exception>
    public string? Run(IConsoleKeys keys, TextWriter output, string prompt, bool ansi, CancellationToken token, int width = 0, string suffix = "")
    {
        var drawn = 0;
        var start = 0;
        void Draw()
        {
            var (shown, cursor) = width > 0 ? Window(ref start, width) : (Text, Cursor);
            output.Write('\r');
            output.Write(prompt);
            output.Write(IsSelected && ansi ? $"{ReverseVideo}{shown}{NormalVideo}" : shown);

            // a field is filled to its width; what was drawn before and is longer than the line now is blanked. Then the cursor goes back to where a character
            // is typed.
            var field = Math.Max(width, shown.Length);
            var length = prompt.Length + field + suffix.Length;
            var blank = Math.Max(0, drawn - length);
            output.Write(new string(' ', field - shown.Length));
            output.Write(suffix);
            output.Write(new string(' ', blank));
            output.Write(new string('\b', blank + suffix.Length + field - cursor));
            output.Flush();
            drawn = length;
        }

        Draw();
        while (true)
        {
            switch (Press(keys.ReadKey(token)))
            {
                case ConsoleLineEditorOutcome.Confirmed:
                    // the line as it was confirmed, without the mark of a selection that is not one any more.
                    (Cursor, IsSelected) = (_text.Length, false);
                    Draw();
                    return Text;
                case ConsoleLineEditorOutcome.Cancelled:
                    return null;
                default:
                    Draw();
                    break;
            }
        }
    }

    // the part of the text a field of `width` characters shows, from `start` on, and where the cursor is in it: the field scrolls as little as it takes to keep the
    // cursor in it, and it is filled as far as the text goes.
    private (string Shown, int Cursor) Window(ref int start, int width)
    {
        var text = Text;
        start = Math.Clamp(start, Math.Max(0, Cursor - (width - 1)), Cursor);
        start = Math.Min(start, Math.Max(0, text.Length - (width - 1)));
        return (text.Substring(start, Math.Min(width, text.Length - start)), Cursor - start);
    }
}
