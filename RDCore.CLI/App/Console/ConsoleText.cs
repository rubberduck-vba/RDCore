using System.Text;

namespace RDCore.CLI.App.Console;

/// <summary>
/// Text as a terminal lays it out: in cells, which is not in characters - an emoji or a CJK character takes two, and a combining mark or a variation selector
/// none of its own.
/// </summary>
/// <remarks>
/// The widths are the ones a modern terminal (Windows Terminal, the terminals of VS Code and of most Linux desktops) gives: East Asian wide and fullwidth
/// characters, and the symbols that are shown as emoji by default, take two cells; a character followed by <c>U+FE0F</c> (the emoji presentation selector)
/// is shown as an emoji too. Anything that has to line up on the right - the border of a box - is measured with <see cref="WidthOf"/>.
/// </remarks>
public static class ConsoleText
{
    private const int EmojiPresentationSelector = 0xFE0F;
    private const int ZeroWidthJoiner = 0x200D;

    // the symbols below U+1F000 that a terminal shows as emoji, and so in two cells, without being asked to (Emoji_Presentation=Yes).
    private static readonly (int First, int Last)[] Wide =
    [
        (0x1100, 0x115F), (0x231A, 0x231B), (0x2329, 0x232A), (0x23E9, 0x23EC), (0x23F0, 0x23F0), (0x23F3, 0x23F3), (0x25FD, 0x25FE), (0x2614, 0x2615),
        (0x2648, 0x2653), (0x267F, 0x267F), (0x2693, 0x2693), (0x26A1, 0x26A1), (0x26AA, 0x26AB), (0x26BD, 0x26BE), (0x26C4, 0x26C5), (0x26CE, 0x26CE),
        (0x26D4, 0x26D4), (0x26EA, 0x26EA), (0x26F2, 0x26F3), (0x26F5, 0x26F5), (0x26FA, 0x26FA), (0x26FD, 0x26FD), (0x2705, 0x2705), (0x270A, 0x270B),
        (0x2728, 0x2728), (0x274C, 0x274C), (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757), (0x2795, 0x2797), (0x27B0, 0x27B0), (0x27BF, 0x27BF),
        (0x2B1B, 0x2B1C), (0x2B50, 0x2B50), (0x2B55, 0x2B55), (0x2E80, 0x303E), (0x3041, 0xA4CF), (0xAC00, 0xD7A3), (0xF900, 0xFAFF), (0xFE30, 0xFE4F),
        (0xFF00, 0xFF60), (0xFFE0, 0xFFE6), (0x1F000, 0x1FAFF), (0x20000, 0x3FFFD),
    ];

    /// <summary>
    /// How many cells <paramref name="text"/> takes at a terminal.
    /// </summary>
    /// <param name="text">The text: one line of it, without control characters.</param>
    public static int WidthOf(string text)
    {
        var width = 0;
        var previous = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var cells = CellsOf(rune.Value, previous);
            width += cells;
            previous = rune.Value == EmojiPresentationSelector ? 2 : cells;
        }

        return width;
    }

    // what one rune adds, given what the one before it took: the presentation selector widens the narrow symbol it follows to an emoji's two cells.
    private static int CellsOf(int rune, int previous)
    {
        if (rune == EmojiPresentationSelector)
        {
            return previous == 1 ? 1 : 0;
        }

        if (rune == ZeroWidthJoiner || rune is >= 0xFE00 and <= 0xFE0E || rune is >= 0x0300 and <= 0x036F || rune is >= 0x1F3FB and <= 0x1F3FF)
        {
            return 0;
        }

        foreach (var (first, last) in Wide)
        {
            if (rune < first)
            {
                break;
            }

            if (rune <= last)
            {
                return 2;
            }
        }

        return 1;
    }

    /// <summary>
    /// <paramref name="text"/>, followed by as many spaces as it takes to fill <paramref name="width"/> cells.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="width">The cells to fill; text that is wider already is left as it is.</param>
    public static string PadToWidth(string text, int width) => text + new string(' ', Math.Max(0, width - WidthOf(text)));

    /// <summary>
    /// <paramref name="text"/> cut to <paramref name="width"/> cells, the last of them an ellipsis when anything had to go.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="width">The cells it may take.</param>
    public static string Truncate(string text, int width)
    {
        if (WidthOf(text) <= width)
        {
            return text;
        }

        var kept = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (WidthOf(kept.ToString() + rune) > width - 1)
            {
                break;
            }

            kept.Append(rune.ToString());
        }

        return kept.Append('…').ToString();
    }

    /// <summary>
    /// The lines of <paramref name="text"/>, each broken where a word would go past <paramref name="width"/> cells - and in a word too long for a line of its own.
    /// </summary>
    /// <param name="text">The text, its lines separated by any line ending.</param>
    /// <param name="width">The cells a line may take: at least one.</param>
    public static IReadOnlyList<string> Wrap(string text, int width)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);

        var lines = new List<string>();
        foreach (var paragraph in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";
                if (WidthOf(candidate) <= width)
                {
                    line.Clear().Append(candidate);
                    continue;
                }

                if (line.Length > 0)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }

                // a word wider than a line is broken where the line ends.
                var rest = word;
                while (WidthOf(rest) > width)
                {
                    var head = HeadOf(rest, width);
                    lines.Add(head);
                    rest = rest[head.Length..];
                }

                line.Append(rest);
            }

            lines.Add(line.ToString());
        }

        return lines;
    }

    // the longest start of the text that fits in the cells, and never nothing.
    private static string HeadOf(string text, int width)
    {
        var length = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (length > 0 && WidthOf(text[..(length + rune.Utf16SequenceLength)]) > width)
            {
                break;
            }

            length += rune.Utf16SequenceLength;
        }

        return text[..length];
    }
}
