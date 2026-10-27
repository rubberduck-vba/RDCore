using System.Globalization;
using System.Text;

namespace RDCore.SDK.Runtime;

/// <summary>
/// The characters of a text as MS-VBA's text mode matches them, in <c>Like</c> (<strong>MS-VBAL §5.6.9.6</strong>) and in a search - <c>InStr</c>,
/// <c>InStrRev</c>, <c>Replace</c>, <c>Split</c>, <c>Filter</c>: folded to one case, and four characters as the two letters they expand to.
/// </summary>
/// <remarks>
/// This is not how the relational operators compare in text mode (<see cref="StringComparisonRules.Compare(ReadOnlySpan{char}, ReadOnlySpan{char})"/>):
/// MS-VBA's <c>=</c> compares by the regional collation, which ignores width and kana and expands more, where its <c>Like</c> and its searches fold case and
/// expand <c>æ</c>, <c>œ</c>, <c>ß</c> and <c>þ</c>, and nothing else (recorded in Excel: oracle\like-collation, oracle\instr-text). A soft hyphen, a full-width
/// letter, katakana, an accented letter and the Kelvin sign are characters of their own; <c>ĳ</c>, the ligatures <c>ﬀ</c> to <c>ﬆ</c>, <c>ǆ</c> and <c>ẞ</c>
/// are not expanded.
/// </remarks>
public static class TextModeCharacters
{
    /// <summary>
    /// The character a character matches in text mode: its lowercase in the regional settings, where the uppercase of that lowercase is the character again.
    /// </summary>
    /// <remarks>
    /// A case mapping that does not go both ways is not a difference in case: the Kelvin sign (U+212A) lowercases to <c>k</c>, which uppercases to <c>K</c>, so
    /// it matches no <c>k</c>, as in MS-VBA. Accents are not case. The regional settings decide what the case of a letter is: in Turkish, <c>İ</c> is the capital
    /// of <c>i</c>.
    /// </remarks>
    /// <param name="c">The character.</param>
    /// <param name="textInfo">The casing of the regional settings.</param>
    public static char Fold(char c, TextInfo textInfo)
    {
        var lower = textInfo.ToLower(c);
        return lower != c && textInfo.ToUpper(lower) == c ? lower : c;
    }

    /// <summary>
    /// The two letters text mode matches a folded character as, if it is one MS-VBA expands: <c>æ</c>, <c>œ</c>, <c>ß</c> and <c>þ</c>, which the capitals
    /// <c>Æ</c>, <c>Œ</c> and <c>Þ</c> fold to.
    /// </summary>
    /// <param name="folded">A character, folded (<see cref="Fold"/>).</param>
    /// <returns>Its letters, or <see langword="null"/> when it is not expanded.</returns>
    public static string? Expansion(char folded) => folded switch
    {
        'æ' => "ae",
        'œ' => "oe",
        'ß' => "ss",
        'þ' => "th",
        _ => null,
    };

    /// <summary>
    /// The letters of a text in text mode: each character folded, and each that is expanded as its letters.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="textInfo">The casing of the regional settings.</param>
    /// <param name="characterOf">For each letter, the index in <paramref name="text"/> of the character it is part of.</param>
    /// <returns>The letters.</returns>
    public static string Letters(ReadOnlySpan<char> text, TextInfo textInfo, out int[] characterOf)
    {
        var letters = new StringBuilder(text.Length + 4);
        var owners = new List<int>(text.Length + 4);
        for (var i = 0; i < text.Length; i++)
        {
            var folded = Fold(text[i], textInfo);
            if (Expansion(folded) is { } expansion)
            {
                letters.Append(expansion);
                owners.Add(i);
                owners.Add(i);
            }
            else
            {
                letters.Append(folded);
                owners.Add(i);
            }
        }

        characterOf = [.. owners];
        return letters.ToString();
    }
}
