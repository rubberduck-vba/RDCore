namespace RDCore.CLI.App.Console;

/// <summary>
/// The characters a <see cref="ConsoleDialogBox"/> is drawn with.
/// </summary>
/// <param name="TopLeft">The top left corner.</param>
/// <param name="Horizontal">The top and the bottom edges.</param>
/// <param name="TopRight">The top right corner.</param>
/// <param name="Vertical">The left and the right edges.</param>
/// <param name="BottomLeft">The bottom left corner.</param>
/// <param name="BottomRight">The bottom right corner.</param>
/// <param name="SeparatorLeft">Where a separator meets the left edge.</param>
/// <param name="Separator">A separator across the box.</param>
/// <param name="SeparatorRight">Where a separator meets the right edge.</param>
public sealed record class ConsoleBorder(
    char TopLeft, char Horizontal, char TopRight, char Vertical, char BottomLeft, char BottomRight, char SeparatorLeft, char Separator, char SeparatorRight)
{
    /// <summary>The double-line border of a QBasic dialog, in box-drawing characters.</summary>
    public static ConsoleBorder Double { get; } = new('╔', '═', '╗', '║', '╚', '╝', '╟', '─', '╢');

    /// <summary>A border of plain ASCII characters, for an output that cannot show box-drawing ones.</summary>
    public static ConsoleBorder Ascii { get; } = new('+', '-', '+', '|', '+', '+', '+', '-', '+');

    /// <summary>
    /// The border an output can show: <see cref="Double"/> when it is written in a Unicode encoding, <see cref="Ascii"/> otherwise.
    /// </summary>
    /// <param name="output">The output.</param>
    public static ConsoleBorder For(TextWriter output) => output.Encoding is System.Text.UnicodeEncoding or System.Text.UTF8Encoding or System.Text.UTF32Encoding
        ? Double
        : Ascii;
}

/// <summary>
/// A dialog drawn at the console the way a dialog of C64 BASIC or QBasic was: a box with a border, its title in the top edge, centred on the line, its message
/// wrapped inside, and a row at the bottom for what the dialog asks - its buttons, or a line to type on.
/// </summary>
/// <remarks>
/// <para>
/// The box is as wide as what is in it needs, and no wider than the console leaves room for: a message too long for a line is wrapped, at a width that is
/// comfortable to read rather than at the edge of the console. An icon stands before the message, which hangs indented under it, as it does in a Windows dialog.
/// </para>
/// <para>
/// The row that asks is drawn by whatever answers it (a <see cref="ConsoleMenu"/>, a <see cref="ConsoleLineEditor"/>), between <see cref="RowPrefix"/> and the end of
/// the row; <see cref="Show{T}"/> draws the rest of the box around it. Where the output understands ANSI escape sequences, the whole box is drawn first and the
/// cursor goes back up to that row; where it does not, the bottom of the box is drawn once the row is answered.
/// </para>
/// </remarks>
public sealed class ConsoleDialogBox
{
    /// <summary>The narrowest the inside of a box is, so that a dialog with little to say still looks like one.</summary>
    public const int MinimumWidth = 30;

    /// <summary>The widest a message is let run before it is wrapped.</summary>
    public const int ComfortableWidth = 64;

    // the blank columns between the border and what is in the box, on each side; and the fewest columns between the box and the edges of the console.
    private const int Padding = 2;
    private const int Margin = 2;

    // the cells an icon is given, whatever it takes: the two of an emoji. The column of the message is that far, and one more, from the edge of the box.
    private const int IconWidth = 2;

    private const string CursorUp = "\u001b[{0}A";
    private const string CursorDown = "\u001b[{0}B";
    private const string CursorToColumn = "\u001b[{0}G";

    private readonly ConsoleBorder _border;
    private readonly bool _ansi;
    private readonly string _margin;
    private readonly List<string> _header = [];
    private readonly List<string> _footer = [];

    /// <summary>
    /// Lays out a box.
    /// </summary>
    /// <param name="title">The title, drawn in the top edge; nothing is drawn there when it is empty.</param>
    /// <param name="icon">What stands before the message - an emoji, which is given a column of its own - or nothing.</param>
    /// <param name="message">The message, its lines separated by any line ending.</param>
    /// <param name="rowWidth">The cells the row that asks takes: the width of its buttons, or the least a line to type on should have.</param>
    /// <param name="consoleWidth">The cells of a line of the console.</param>
    /// <param name="border">What the box is drawn with.</param>
    /// <param name="separated">Whether a separator stands between the message and the row that asks, as it does above the buttons of a dialog.</param>
    /// <param name="ansi">
    /// Whether the box is drawn on an output that understands ANSI escape sequences: the message and the right edge are then put at the columns they belong in,
    /// rather than after the cells the text before them is measured to take - terminals do not agree on how wide an emoji is, and the right edge stays in line
    /// with the others whatever the one of the icon is drawn as.
    /// </param>
    public ConsoleDialogBox(string title, string icon, string message, int rowWidth, int consoleWidth, ConsoleBorder border, bool separated, bool ansi = false)
    {
        _border = border;
        _ansi = ansi;

        // the icon's column, and the space between it and the message, are the same on every line of the message: the lines under the icon hang indented under it.
        var gutter = icon.Length == 0 ? 0 : Math.Max(IconWidth, ConsoleText.WidthOf(icon)) + 1;
        var messageWidth = message.ReplaceLineEndings("\n").Split('\n').Max(ConsoleText.WidthOf) + gutter;
        var widest = Math.Max(MinimumWidth, consoleWidth - 2 * (Margin + Padding + 1));
        Width = Math.Min(widest, Math.Max(MinimumWidth, Math.Max(rowWidth, Math.Max(ConsoleText.WidthOf(title) + 4, Math.Min(messageWidth, ComfortableWidth)))));

        var outer = Width + 2 * (Padding + 1);
        _margin = new string(' ', Math.Max(0, (consoleWidth - outer) / 2));

        _header.Add(TopEdge(title));
        _header.Add(Row(string.Empty));
        var lines = ConsoleText.Wrap(message, Math.Max(1, Width - gutter));
        for (var index = 0; index < lines.Count; index++)
        {
            _header.Add(MessageRow(index == 0 ? icon : string.Empty, gutter, lines[index]));
        }

        _header.Add(Row(string.Empty));
        if (separated)
        {
            _header.Add($"{_margin}{border.SeparatorLeft}{new string(border.Separator, Width + 2 * Padding)}{border.SeparatorRight}");
        }
        else
        {
            _footer.Add(Row(string.Empty));
        }

        _footer.Add($"{_margin}{border.BottomLeft}{new string(border.Horizontal, Width + 2 * Padding)}{border.BottomRight}");
    }

    /// <summary>The cells inside the box that text is written in: its width less the border and the padding.</summary>
    public int Width { get; }

    /// <summary>The lines of the box above the row that asks.</summary>
    public IReadOnlyList<string> Header => _header;

    /// <summary>The lines of the box below the row that asks.</summary>
    public IReadOnlyList<string> Footer => _footer;

    /// <summary>What a row of the box starts with: the margin, the left edge, and the padding.</summary>
    public string RowPrefix => $"{_margin}{_border.Vertical}{new string(' ', Padding)}";

    /// <summary>What a row of the box ends with: the padding, and the right edge.</summary>
    public string RowSuffix => $"{new string(' ', Padding)}{_border.Vertical}";

    /// <summary>
    /// A row of the box with <paramref name="text"/> in it, filled to the right edge.
    /// </summary>
    /// <param name="text">The text, no wider than <see cref="Width"/>.</param>
    public string Row(string text) => _ansi
        ? RowPrefix + text + RightEdge()
        : RowPrefix + ConsoleText.PadToWidth(text, Width) + RowSuffix;

    // a line of the message: the icon - on the first line - in its own column, and the text in the column after it.
    private string MessageRow(string icon, int gutter, string text) => _ansi
        ? RowPrefix + icon + string.Format(CursorToColumn, ContentColumn + gutter) + text + RightEdge()
        : Row(ConsoleText.PadToWidth(icon, gutter) + text);

    // the 1-based columns of the console the text of a row starts at, and the right edge is at.
    private int ContentColumn => _margin.Length + 1 + Padding + 1;

    private int RightEdgeColumn => _margin.Length + 1 + Padding + Width + Padding + 1;

    // the right edge, put at its column: whatever the text before it is drawn as, it is where the others are.
    private string RightEdge() => string.Format(CursorToColumn, RightEdgeColumn) + _border.Vertical;

    /// <summary>
    /// The blank cells before <paramref name="text"/> that centre it in a row, and the ones after it that fill the row to the right edge.
    /// </summary>
    /// <param name="text">The text.</param>
    public (string Before, string After) Centring(string text)
    {
        var free = Math.Max(0, Width - ConsoleText.WidthOf(text));
        return (new string(' ', free / 2), new string(' ', free - free / 2));
    }

    private string TopEdge(string title)
    {
        var inside = Width + 2 * Padding;
        var caption = title.Length == 0 ? string.Empty : $" {ConsoleText.Truncate(title, inside - 4)} ";
        var free = inside - ConsoleText.WidthOf(caption);
        return $"{_margin}{_border.TopLeft}{new string(_border.Horizontal, free / 2)}{caption}{new string(_border.Horizontal, free - free / 2)}{_border.TopRight}";
    }

    /// <summary>
    /// Draws the box, with the row that asks drawn by <paramref name="ask"/>, which starts at the start of that row and leaves the cursor anywhere on it.
    /// </summary>
    /// <typeparam name="T">The answer.</typeparam>
    /// <param name="output">Where the box is drawn.</param>
    /// <param name="ansi">Whether <paramref name="output"/> understands ANSI escape sequences, which draw the bottom of the box before the row is answered.</param>
    /// <param name="ask">Draws the row and answers it.</param>
    /// <returns>What <paramref name="ask"/> answered; the cursor is on the line after the box.</returns>
    public T Show<T>(TextWriter output, bool ansi, Func<T> ask)
    {
        output.WriteLine();
        foreach (var line in _header)
        {
            output.WriteLine(line);
        }

        if (!ansi)
        {
            var plain = ask();
            output.WriteLine();
            WriteFooter(output);
            return plain;
        }

        // the row is drawn blank, then the rest of the box under it; and the cursor goes back up to the row to answer it.
        output.WriteLine(Row(string.Empty));
        WriteFooter(output);
        output.Write(string.Format(CursorUp, _footer.Count + 1));
        output.Write('\r');
        output.Flush();

        try
        {
            return ask();
        }
        finally
        {
            // what was asked is answered, or no longer waited for: the cursor goes past the box, and nothing after it is written over it.
            output.Write(string.Format(CursorDown, _footer.Count));
            output.WriteLine('\r');
            output.Flush();
        }
    }

    /// <summary>
    /// Draws the box with <paramref name="row"/> as the row that asks, which nothing answers at the console: what is asked is read from a script.
    /// </summary>
    /// <param name="output">Where the box is drawn.</param>
    /// <param name="row">What the row shows.</param>
    public void Show(TextWriter output, string row)
    {
        output.WriteLine();
        foreach (var line in _header)
        {
            output.WriteLine(line);
        }

        output.WriteLine(row);
        WriteFooter(output);
    }

    private void WriteFooter(TextWriter output)
    {
        foreach (var line in _footer)
        {
            output.WriteLine(line);
        }
    }
}
