namespace RDCore.CLI.App.Console;

/// <summary>
/// What a key did to a <see cref="ConsoleMenu"/>.
/// </summary>
public enum ConsoleMenuOutcomeKind
{
    /// <summary>Nothing: the key means nothing to the menu.</summary>
    Ignored,

    /// <summary>Another item is selected.</summary>
    Moved,

    /// <summary>An item was chosen, and the menu is closed.</summary>
    Chosen,

    /// <summary>The menu was closed without a choice: <kbd>Esc</kbd>.</summary>
    Dismissed,
}

/// <summary>
/// What a key did to a <see cref="ConsoleMenu"/>, and which item it chose.
/// </summary>
/// <param name="Kind">What the key did.</param>
/// <param name="Index">The item chosen, for <see cref="ConsoleMenuOutcomeKind.Chosen"/>.</param>
public readonly record struct ConsoleMenuOutcome(ConsoleMenuOutcomeKind Kind, int Index = -1);

/// <summary>
/// A row of choices at the console, one of them selected, chosen with the keyboard - the buttons of a dialog, as a line of text.
/// </summary>
/// <remarks>
/// <para>
/// The arrow keys and <kbd>Tab</kbd> (<kbd>Shift</kbd>+<kbd>Tab</kbd> backwards) move the selection, wrapping around; <kbd>Home</kbd> and <kbd>End</kbd> go to the first
/// and the last item; <kbd>Enter</kbd> and the space bar choose the selected one. An item names its own key with a <c>&amp;</c> before it (<c>"&amp;Yes"</c>),
/// as a button of a Windows dialog does, and that key chooses it at once - its first letter, when it names none. <kbd>Esc</kbd> closes the menu without a choice, when
/// it can be closed that way at all.
/// </para>
/// <para>
/// What a key does (<see cref="Press"/>) is apart from how the menu is drawn (<see cref="Render"/>) and from where the keys come from (<see cref="Run"/>), so that the
/// same menu answers a dialog, and anything else the shell needs a choice for.
/// </para>
/// </remarks>
public sealed class ConsoleMenu
{
    /// <summary>
    /// Creates a menu.
    /// </summary>
    /// <param name="items">The items, each with a <c>&amp;</c> before its key if it names one.</param>
    /// <param name="selected">The item selected when the menu opens: an index of <paramref name="items"/>.</param>
    /// <param name="canDismiss">Whether <kbd>Esc</kbd> closes the menu without a choice.</param>
    public ConsoleMenu(IReadOnlyList<string> items, int selected = 0, bool canDismiss = true)
    {
        ArgumentOutOfRangeException.ThrowIfZero(items.Count, nameof(items));
        Items = items;
        Selected = Math.Clamp(selected, 0, items.Count - 1);
        CanDismiss = canDismiss;
    }

    /// <summary>The items, as they were given.</summary>
    public IReadOnlyList<string> Items { get; }

    /// <summary>The selected item.</summary>
    public int Selected { get; private set; }

    /// <summary>Whether <kbd>Esc</kbd> closes the menu without a choice.</summary>
    public bool CanDismiss { get; }

    /// <summary>The text of an item, without the <c>&amp;</c> that marks its key (<c>&amp;&amp;</c> is a <c>&amp;</c>).</summary>
    public static string TextOf(string item) => item.Replace("&&", "\0").Replace("&", string.Empty).Replace('\0', '&');

    /// <summary>The key of an item: the character after its <c>&amp;</c>, or its first letter or digit.</summary>
    public static char? KeyOf(string item)
    {
        for (var index = 0; index < item.Length - 1; index++)
        {
            if (item[index] == '&')
            {
                if (item[index + 1] != '&')
                {
                    return char.ToUpperInvariant(item[index + 1]);
                }

                index++;
            }
        }

        return TextOf(item).FirstOrDefault(char.IsLetterOrDigit) is var first and not '\0' ? char.ToUpperInvariant(first) : null;
    }

    /// <summary>
    /// What a key does to the menu.
    /// </summary>
    /// <param name="key">The key.</param>
    public ConsoleMenuOutcome Press(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow or ConsoleKey.UpArrow:
                return Move(-1);
            case ConsoleKey.Tab when key.Modifiers.HasFlag(ConsoleModifiers.Shift):
                return Move(-1);
            case ConsoleKey.RightArrow or ConsoleKey.DownArrow or ConsoleKey.Tab:
                return Move(+1);
            case ConsoleKey.Home:
                return MoveTo(0);
            case ConsoleKey.End:
                return MoveTo(Items.Count - 1);
            case ConsoleKey.Enter or ConsoleKey.Spacebar:
                return new(ConsoleMenuOutcomeKind.Chosen, Selected);
            case ConsoleKey.Escape:
                return CanDismiss ? new(ConsoleMenuOutcomeKind.Dismissed) : default;
        }

        if (key.KeyChar is var typed and not '\0' && !char.IsControl(typed))
        {
            var matches = Enumerable.Range(0, Items.Count).Where(index => KeyOf(Items[index]) == char.ToUpperInvariant(typed)).ToArray();
            if (matches is [var only])
            {
                Selected = only;
                return new(ConsoleMenuOutcomeKind.Chosen, only);
            }
        }

        return default;
    }

    private ConsoleMenuOutcome Move(int by) => MoveTo((Selected + by + Items.Count) % Items.Count);

    private ConsoleMenuOutcome MoveTo(int index)
    {
        if (index == Selected)
        {
            return default;
        }

        Selected = index;
        return new(ConsoleMenuOutcomeKind.Moved);
    }

    /// <summary>
    /// The menu as a line of text: each item between brackets when it is the selected one, and between spaces when it is not, so that the selection moving does
    /// not move the items.
    /// </summary>
    public string Render()
        => string.Join(" ", Items.Select((item, index) => index == Selected ? $"[ {TextOf(item)} ]" : $"  {TextOf(item)}  "));

    /// <summary>
    /// Draws the menu at the start of the current line, and reads keys until an item is chosen or the menu is dismissed. The line is drawn again over itself each
    /// time the selection moves; the cursor is left at the end of it.
    /// </summary>
    /// <param name="keys">Where the keys come from.</param>
    /// <param name="output">Where the menu is drawn.</param>
    /// <param name="indent">What the line starts with.</param>
    /// <param name="token">Stops waiting for a key.</param>
    /// <param name="suffix">What the line ends with, after the items: the right edge of the box the menu is in, say.</param>
    /// <returns>The index of the item chosen, or <see langword="null"/> when the menu was dismissed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="token"/> was cancelled before a choice was made.</exception>
    public int? Run(IConsoleKeys keys, TextWriter output, string indent, CancellationToken token, string suffix = "")
    {
        var drawn = 0;
        void Draw()
        {
            var line = indent + Render() + suffix;
            output.Write('\r');
            output.Write(line.PadRight(drawn));
            output.Flush();
            drawn = line.Length;
        }

        Draw();
        while (true)
        {
            var outcome = Press(keys.ReadKey(token));
            switch (outcome.Kind)
            {
                case ConsoleMenuOutcomeKind.Moved:
                    Draw();
                    break;
                case ConsoleMenuOutcomeKind.Chosen:
                    Draw();
                    return outcome.Index;
                case ConsoleMenuOutcomeKind.Dismissed:
                    return null;
            }
        }
    }
}
