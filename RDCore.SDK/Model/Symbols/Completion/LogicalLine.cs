using System.Text;
using System.Text.RegularExpressions;

namespace RDCore.SDK.Model.Symbols.Completion;

/// <summary>
/// The logical line a position of a module is in (<strong>MS-VBAL §3.2.2</strong>): its physical lines joined where a line continuation ends one, cut at the
/// position.
/// </summary>
/// <remarks>
/// <para>
/// A line continuation is a <c>_</c> after a space or a tab at the end of a physical line. It and the line terminator after it read as one space, so a
/// statement written on several physical lines reads as written on one.
/// </para>
/// <para>
/// The whitespace before the <c>_</c> is narrower than <strong>MS-VBAL §3.2.2</strong> has it: its <c>WSC</c> is also the <c>U+0019</c> character, the
/// ideographic space <c>U+3000</c> and the other spaces of Unicode class <c>Zs</c>, which the parser's lexer does not take before a line continuation.
/// Physical lines the parser reads as two logical lines are two here too.
/// </para>
/// <para>
/// What a completion list and a signature help read of the call being written (<see cref="CompletionSite"/>, <see cref="SignatureSite"/>) is read off the
/// logical line: an argument written on the next physical line is still one of the call, and a named argument written there is still given.
/// </para>
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
        var first = line;
        while (first > 0 && Continued(lines[first - 1]) is not null)
        {
            first--;
        }

        var before = new StringBuilder();
        for (var index = first; index < line; index++)
        {
            before.Append(Continued(lines[index]));
        }

        before.Append(text, 0, character);
        var after = new StringBuilder();
        var last = line;
        if (Continued(text) is { } continued && character <= continued.Length)
        {
            after.Append(continued, character, continued.Length - character);
            while (last + 1 < lines.Count)
            {
                last++;
                var next = lines[last].TrimEnd('\r');
                if (Continued(next) is not { } more)
                {
                    after.Append(next);
                    break;
                }

                after.Append(more);
            }
        }
        else
        {
            after.Append(text, character, text.Length - character);
        }

        return new LogicalLine(before.ToString(), after.ToString(), first, last);
    }

    // a physical line that a line continuation ends, the continuation read as a space; null for one that ends the logical line.
    private static string? Continued(string line)
        => Continuation().Match(line.TrimEnd('\r')) is { Success: true } match ? line[..match.Index] + " " : null;

    [GeneratedRegex(@"[ \t]+_[ \t]*$")]
    private static partial Regex Continuation();
}
