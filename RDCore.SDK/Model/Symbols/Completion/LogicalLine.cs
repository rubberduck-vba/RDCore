using System.Text.RegularExpressions;

namespace RDCore.SDK.Model.Symbols.Completion;

/// <summary>
/// The logical line a position of a module is in (<strong>MS-VBAL §3.2.2</strong>): its physical lines joined where a line continuation ends one, cut at the
/// position.
/// </summary>
/// <remarks>
/// A line continuation is a <c>_</c> after a space or a tab at the end of a physical line. It and the line terminator after it read as one space, so a
/// statement written on several physical lines reads as written on one. What a completion list and a signature help read of the call being written
/// (<see cref="CompletionSite"/>, <see cref="SignatureSite"/>) is read off the logical line: an argument written on the next physical line is still one of
/// the call, and a named argument written there is still given.
/// </remarks>
/// <param name="Before">The text of the logical line before the position.</param>
/// <param name="After">The text of the logical line from the position on.</param>
/// <param name="FirstLine">The first physical line of the logical line, from 0.</param>
/// <param name="LastLine">The last physical line of the logical line, from 0.</param>
public sealed partial record class LogicalLine(string Before, string After, int FirstLine, int LastLine)
{
    /// <summary>
    /// The logical line that physical line <paramref name="line"/> is part of, cut at <paramref name="character"/>.
    /// </summary>
    /// <param name="lines">The physical lines of the module; a line may end with the <c>\r</c> of its terminator.</param>
    /// <param name="line">The physical line of the position, from 0.</param>
    /// <param name="character">The position in that line, clamped to it.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="line"/> is not a line of <paramref name="lines"/>.</exception>
    public static LogicalLine At(IReadOnlyList<string> lines, int line, int character)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(line, lines.Count);

        var text = lines[line].TrimEnd('\r');
        character = Math.Clamp(character, 0, text.Length);
        var before = text[..character];
        var first = line;
        while (first > 0 && Continued(lines[first - 1]) is { } previous)
        {
            before = previous + before;
            first--;
        }

        var after = text[character..];
        var last = line;
        if (Continued(text) is { } continued && character <= continued.Length)
        {
            after = continued[character..];
            while (last + 1 < lines.Count)
            {
                last++;
                var next = lines[last].TrimEnd('\r');
                if (Continued(next) is not { } more)
                {
                    after += next;
                    break;
                }

                after += more;
            }
        }

        return new LogicalLine(before, after, first, last);
    }

    // a physical line that a line continuation ends, the continuation read as a space; null for one that ends the logical line.
    private static string? Continued(string line)
        => Continuation().Match(line.TrimEnd('\r')) is { Success: true } match ? line[..match.Index] + " " : null;

    [GeneratedRegex(@"[ \t]+_[ \t]*$")]
    private static partial Regex Continuation();
}
