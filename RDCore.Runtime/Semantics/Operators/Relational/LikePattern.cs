using RDCore.SDK.Runtime;
using System.Collections.Concurrent;
using System.Globalization;

namespace RDCore.Runtime.Semantics.Operators.Relational;

/// <summary>
/// A <c>like-pattern</c> (<strong>MS-VBAL §5.6.9.6</strong>), parsed once into its elements and matched the way MS-VBA matches it.
/// </summary>
/// <remarks>
/// Matching is the two-pointer wildcard algorithm: a mismatch after a <c>*</c> resumes from that <c>*</c> one character further, so a match
/// takes at most the length of the text times the length of the pattern, and allocates nothing but the letters of a text that has a character
/// to expand, in text mode - <c>String(1000, "a") Like "*a*a*a*b"</c>
/// answers <c>False</c> at once. Every element other than <c>*</c> matches what is at its place, the one way it can; what an element matches
/// never depends on how much text a <c>*</c> took, so the result is the one any matching order gives.
/// <para>
/// Where MS-VBA departs from MS-VBAL, this matches MS-VBA (RD-VBAL §5.6.9.6):
/// </para>
/// <list type="bullet">
/// <item><c>[]</c> matches no character at all (it is not there), and <c>[!]</c> matches a <c>!</c>.</item>
/// <item>A <c>]</c> right after <c>[</c> or <c>[!</c> closes the list.</item>
/// <item>An invalid element - an unterminated list, or a range whose first character is above its last - raises error 93 when it is tested
/// against a character, and the items of a list are tested in order until one matches. A <c>*</c> reached with text left to match checks the
/// whole rest of the pattern first, and raises error 93 if any of it is invalid; a <c>*</c> reached at the end of the text checks nothing.</item>
/// <item><c>#</c> matches <c>0</c> to <c>9</c> and no other digit; <c>?</c> and <c>*</c> match a line feed like any other character.</item>
/// <item>In text mode a character matches its other case where the two map to each other in the regional settings (<see cref="Fold"/>),
/// and a range is folded the same way before it is tested: <c>[A-z]</c> is <c>[a-z]</c>, and <c>[Z-a]</c> is invalid.</item>
/// <item>In text mode a character the collation expands to two letters (<see cref="Expansion"/>) is matched as itself or as its letters, in the
/// text and in the pattern alike: see <see cref="TextUnits"/>.</item>
/// </list>
/// </remarks>
internal sealed class LikePattern
{
    // a program matches against a handful of patterns; one that builds patterns as it goes must not grow this without bound.
    private const int CacheCapacity = 256;
    private static readonly ConcurrentDictionary<string, LikePattern> Cache = new(StringComparer.Ordinal);

    private readonly Element[] _binary;
    private readonly bool[] _binaryInvalidFrom;

    // the pattern folded for the culture of the last text-mode match; one culture per session, so this is replaced about never.
    private TextForm? _text;

    private LikePattern(string pattern)
    {
        _binary = Parse(pattern, out var hasEmptyCharList);
        _binaryInvalidFrom = InvalidFrom(_binary);
        HasEmptyCharList = hasEmptyCharList;
    }

    /// <summary>
    /// Gets the parsed form of a pattern, from the ones already parsed if it is there.
    /// </summary>
    /// <param name="pattern">The characters of the <c>like-pattern</c>.</param>
    public static LikePattern Get(string pattern)
    {
        if (Cache.TryGetValue(pattern, out var parsed))
        {
            return parsed;
        }

        parsed = new LikePattern(pattern);
        if (Cache.Count >= CacheCapacity)
        {
            Cache.Clear();
        }

        Cache.TryAdd(pattern, parsed);
        return parsed;
    }

    /// <summary>
    /// Whether the pattern has a <c>[]</c> or a <c>[!]</c>: MS-VBAL makes these a list of no character, which MS-VBA does not.
    /// </summary>
    public bool HasEmptyCharList { get; }

    /// <summary>
    /// Whether the pattern has an element MS-VBAL makes invalid under the given comparison rules, which MS-VBA reports only when it reaches it.
    /// </summary>
    /// <param name="rules">The comparison mode of the module, and the regional settings text mode folds case by.</param>
    public bool IsInvalid(StringComparisonRules rules)
        => (rules.IgnoresCase ? TextFormFor(rules.EffectiveCulture.TextInfo).InvalidFrom : _binaryInvalidFrom)[0];

    /// <summary>
    /// Matches text against the pattern.
    /// </summary>
    /// <param name="text">The characters to match.</param>
    /// <param name="rules">The comparison mode of the module, and the regional settings text mode folds case by.</param>
    /// <returns>Whether the whole text matches the whole pattern.</returns>
    /// <exception cref="InvalidLikePatternException">The match reached an invalid element.</exception>
    public bool IsMatch(ReadOnlySpan<char> text, StringComparisonRules rules)
    {
        if (!rules.IgnoresCase)
        {
            return IsMatch(new TextUnits(text, textInfo: null), _binary, _binaryInvalidFrom);
        }

        var textInfo = rules.EffectiveCulture.TextInfo;
        var form = TextFormFor(textInfo);
        return IsMatch(TextUnits.Of(text, textInfo), form.Elements, form.InvalidFrom);
    }

    private static bool IsMatch(in TextUnits text, Element[] elements, bool[] invalidFrom)
    {
        var p = 0;
        var t = 0;
        var star = -1;
        var starText = 0;
        while (t < text.Length)
        {
            if (p < elements.Length && elements[p].Kind == ElementKind.Star)
            {
                if (invalidFrom[p + 1])
                {
                    throw new InvalidLikePatternException();
                }

                star = p++;
                starText = t;
                continue;
            }

            if (p < elements.Length && elements[p].Match(text, t) is var after && after > t)
            {
                p++;
                t = after;
                continue;
            }

            if (star < 0)
            {
                return false;
            }

            // the last * takes one more character, and the rest of the pattern starts over after it.
            p = star + 1;
            t = ++starText;
        }

        // the text is matched: whatever is left of the pattern must match nothing, and only a * can. It checks nothing here.
        return p == elements.Length || (p == elements.Length - 1 && elements[p].Kind == ElementKind.Star);
    }

    /// <summary>
    /// The character a character matches in text mode: its lowercase in the regional settings, where the uppercase of that lowercase is
    /// the character again.
    /// </summary>
    /// <remarks>
    /// A case mapping that does not go both ways is not a difference in case: the Kelvin sign (U+212A) lowercases to <c>k</c>, which
    /// uppercases to <c>K</c>, so <c>ChrW(&amp;H212A) Like "k"</c> is <c>False</c> as it is in MS-VBA. Accents are not case: MS-VBA does
    /// not match <c>é</c> with <c>e</c>, nor <c>ß</c> with a range of <c>[a-z]</c>, in text mode. The regional settings decide what the
    /// case of a letter is: in Turkish, <c>İ</c> is the capital of <c>i</c>.
    /// </remarks>
    internal static char Fold(char c, TextInfo textInfo)
    {
        var lower = textInfo.ToLower(c);
        return lower != c && textInfo.ToUpper(lower) == c ? lower : c;
    }

    /// <summary>
    /// The two letters text mode matches a folded character as, if it is one MS-VBA's <c>Like</c> expands.
    /// </summary>
    /// <remarks>
    /// These are the characters MS-VBA expands (Excel, oracle <c>like-collation</c> M03-M06): <c>æ</c>, <c>œ</c>, <c>ß</c> and <c>þ</c>, and the
    /// capitals that fold to them. Others that <c>=</c> compares equal to two letters in text mode - <c>ĳ</c>, the <c>ﬀ</c> to <c>ﬆ</c> ligatures,
    /// <c>ǆ</c>, <c>ǉ</c>, <c>ǌ</c>, <c>ǳ</c>, <c>ẞ</c> - <c>Like</c> matches as themselves only.
    /// </remarks>
    internal static string? Expansion(char folded) => folded switch
    {
        'æ' => "ae",
        'œ' => "oe",
        'ß' => "ss",
        'þ' => "th",
        _ => null,
    };

    private TextForm TextFormFor(TextInfo textInfo)
    {
        var form = _text;
        if (form is null || !ReferenceEquals(form.TextInfo, textInfo))
        {
            var elements = Expanded(Array.ConvertAll(_binary, element => element.Folded(textInfo)));
            _text = form = new TextForm(textInfo, elements, InvalidFrom(elements));
        }

        return form;
    }

    // a character of the pattern that text mode expands is its two letters, each matched as a character of the pattern is.
    private static Element[] Expanded(Element[] folded)
    {
        if (!Array.Exists(folded, element => element.Kind == ElementKind.Literal && Expansion(element.Character) is not null))
        {
            return folded;
        }

        var elements = new List<Element>(folded.Length + 1);
        foreach (var element in folded)
        {
            if (element.Kind == ElementKind.Literal && Expansion(element.Character) is { } letters)
            {
                elements.Add(element with { Character = letters[0] });
                elements.Add(element with { Character = letters[1] });
            }
            else
            {
                elements.Add(element);
            }
        }

        return [.. elements];
    }

    private sealed record class TextForm(TextInfo TextInfo, Element[] Elements, bool[] InvalidFrom);

    /// <summary>
    /// The text a pattern is matched against, as the units its elements match: in binary mode its characters, and in text mode its characters
    /// folded, with a character MS-VBA expands (<see cref="Expansion"/>) as its two letters.
    /// </summary>
    /// <remarks>
    /// What an element takes of a character that is expanded is what MS-VBA takes of it (Excel, oracle <c>like-collation</c> L01-L08, M01-M08):
    /// <list type="bullet">
    /// <item>A character, or a <c>*</c>, takes one letter: <c>æ Like "a?"</c>, <c>æ Like "*e"</c> and <c>"ae" Like "æ"</c> are <c>True</c>.</item>
    /// <item>A <c>?</c> takes the rest of the character it is at - the whole of it, at its start: <c>æ Like "??"</c> is <c>False</c>.</item>
    /// <item>A list takes the whole character when the character is in it, and otherwise its first letter, when that is: <c>æ Like "[a-æ]"</c> and
    /// <c>æ Like "[ae][ae]"</c> are <c>True</c>, <c>æ Like "[ae]"</c> and <c>æ Like "[æa]e"</c> are <c>False</c>. An item of a list that is expanded is
    /// matched by its letters too: <c>"ae" Like "[æ]"</c> is <c>True</c>.</item>
    /// <item>A negated list tests the first letter, and takes it: <c>æ Like "[!x]e"</c> is <c>True</c>, <c>æ Like "[!a]e"</c> and <c>æ Like "[!x]"</c>
    /// are <c>False</c>.</item>
    /// <item>A range is a range of characters, as it is in binary mode: <c>"b" Like "[æ-c]"</c> raises error 93.</item>
    /// </list>
    /// </remarks>
    private readonly ref struct TextUnits
    {
        private readonly ReadOnlySpan<char> _text;
        // folds each character as it is read, for a text with nothing to expand.
        private readonly TextInfo? _textInfo;
        // a text with something to expand: its characters folded, each expansion as its letters,
        private readonly string? _units;
        // and for each of those, the character it is part of, and where that character ends.
        private readonly string? _characters;
        private readonly int[]? _ends;

        public TextUnits(ReadOnlySpan<char> text, TextInfo? textInfo)
        {
            _text = text;
            _textInfo = textInfo;
        }

        private TextUnits(string units, string characters, int[] ends)
        {
            _units = units;
            _characters = characters;
            _ends = ends;
        }

        public static TextUnits Of(ReadOnlySpan<char> text, TextInfo textInfo)
        {
            var expands = false;
            foreach (var c in text)
            {
                if (Expansion(Fold(c, textInfo)) is not null)
                {
                    expands = true;
                    break;
                }
            }

            if (!expands)
            {
                return new(text, textInfo);
            }

            var units = new System.Text.StringBuilder(text.Length + 4);
            var characters = new System.Text.StringBuilder(text.Length + 4);
            var ends = new List<int>(text.Length + 4);
            foreach (var c in text)
            {
                var folded = Fold(c, textInfo);
                var letters = Expansion(folded) ?? folded.ToString();
                var end = units.Length + letters.Length;
                foreach (var letter in letters)
                {
                    units.Append(letter);
                    characters.Append(folded);
                    ends.Add(end);
                }
            }

            return new(units.ToString(), characters.ToString(), [.. ends]);
        }

        public int Length => _units?.Length ?? _text.Length;

        public char this[int index] => _units is not null ? _units[index] : _textInfo is null ? _text[index] : Fold(_text[index], _textInfo);

        // the index after the last unit of the character the unit at index is part of.
        public int EndOfCharacter(int index) => _ends?[index] ?? index + 1;

        // whether the unit at index is the first of a character that is expanded, and so that character.
        public bool IsExpandedCharacter(int index, out char character)
        {
            character = default;
            if (_ends is null || (index > 0 && _ends[index - 1] != index) || _ends[index] - index < 2)
            {
                return false;
            }

            character = _characters![index];
            return true;
        }

        public bool StartsWith(int index, string letters)
        {
            if (index + letters.Length > Length)
            {
                return false;
            }

            for (var k = 0; k < letters.Length; k++)
            {
                if (this[index + k] != letters[k])
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static Element[] Parse(string pattern, out bool hasEmptyCharList)
    {
        hasEmptyCharList = false;
        var elements = new List<Element>(pattern.Length);
        for (var i = 0; i < pattern.Length; i++)
        {
            switch (pattern[i])
            {
                case '?':
                    elements.Add(new(ElementKind.Any));
                    break;
                case '#':
                    elements.Add(new(ElementKind.Digit));
                    break;
                case '*':
                    // consecutive stars match what one does.
                    if (elements.Count == 0 || elements[^1].Kind != ElementKind.Star)
                    {
                        elements.Add(new(ElementKind.Star));
                    }
                    break;
                case '[':
                    i = ParseCharList(pattern, i, elements, ref hasEmptyCharList);
                    break;
                default:
                    elements.Add(new(ElementKind.Literal, pattern[i]));
                    break;
            }
        }

        return [.. elements];
    }

    // returns the index of the last character of the list.
    private static int ParseCharList(string pattern, int open, List<Element> elements, ref bool hasEmptyCharList)
    {
        var start = open + 1;
        var isNegated = start < pattern.Length && pattern[start] == '!';
        if (isNegated)
        {
            start++;
        }

        if (start < pattern.Length && pattern[start] == ']')
        {
            // a ] right after [ or [! closes the list: MS-VBA reads [] as nothing at all, and [!] as a !.
            hasEmptyCharList = true;
            if (isNegated)
            {
                elements.Add(new(ElementKind.Literal, '!'));
            }
            return start;
        }

        var close = pattern.IndexOf(']', start);
        if (close < 0)
        {
            elements.Add(new(ElementKind.Unterminated));
            return pattern.Length - 1;
        }

        // like-pattern-charlist = "[" ["!"] ["-"] *like-pattern-charlist-element ["-"] "]": a - that does not sit between two characters
        // of a range is a character of the list, at the start, at the end, or right after a range.
        var ranges = new List<CharRange>(close - start);
        for (var k = start; k < close;)
        {
            if (k + 2 < close && pattern[k + 1] == '-')
            {
                ranges.Add(new(pattern[k], pattern[k + 2]));
                k += 3;
            }
            else
            {
                ranges.Add(new(pattern[k], pattern[k]));
                k++;
            }
        }

        elements.Add(new(ElementKind.List, IsNegated: isNegated, Ranges: [.. ranges]));
        return close;
    }

    // index k: whether an element at k or after it is invalid; one more than there are elements, so that "after the last" is false.
    private static bool[] InvalidFrom(Element[] elements)
    {
        var invalidFrom = new bool[elements.Length + 1];
        for (var k = elements.Length - 1; k >= 0; k--)
        {
            invalidFrom[k] = invalidFrom[k + 1] || elements[k].IsInvalid;
        }

        return invalidFrom;
    }

    private enum ElementKind : byte
    {
        Literal,
        Any,
        Digit,
        List,
        Star,
        Unterminated,
    }

    // an item of a list: a range, or one character, which in text mode may be one that is expanded (Letters).
    private readonly record struct CharRange(char First, char Last, string? Letters = null)
    {
        public bool IsReversed => First > Last;

        public bool Contains(char c) => c >= First && c <= Last;
    }

    private readonly record struct Element(ElementKind Kind, char Character = default, bool IsNegated = false, CharRange[]? Ranges = null)
    {
        public bool IsInvalid => Kind == ElementKind.Unterminated || (Kind == ElementKind.List && Array.Exists(Ranges!, range => range.IsReversed));

        public Element Folded(TextInfo textInfo) => Kind switch
        {
            ElementKind.Literal => this with { Character = Fold(Character, textInfo) },
            ElementKind.List => this with
            {
                Ranges = Array.ConvertAll(Ranges!, range => range.First == range.Last
                    ? new CharRange(Fold(range.First, textInfo), Fold(range.First, textInfo), Expansion(Fold(range.First, textInfo)))
                    : new CharRange(Fold(range.First, textInfo), Fold(range.Last, textInfo)))
            },
            _ => this,
        };

        // the index after what the element matches at index of the text, or -1 when it does not match there.
        public int Match(in TextUnits text, int index)
        {
            switch (Kind)
            {
                case ElementKind.Literal:
                    return text[index] == Character ? index + 1 : -1;
                case ElementKind.Any:
                    return text.EndOfCharacter(index);
                case ElementKind.Digit:
                    return text[index] is >= '0' and <= '9' ? index + 1 : -1;
                case ElementKind.List:
                    // an expanded character in the list as itself is taken whole.
                    if (!IsNegated && text.IsExpandedCharacter(index, out var character)
                        && Array.Exists(Ranges!, range => !range.IsReversed && range.Contains(character)))
                    {
                        return text.EndOfCharacter(index);
                    }

                    // the items are tested in order, and the first that matches ends the test: an invalid range after it is not reached.
                    foreach (var range in Ranges!)
                    {
                        if (range.IsReversed)
                        {
                            throw new InvalidLikePatternException();
                        }

                        if (range.Letters is { } letters ? text.StartsWith(index, letters) : range.Contains(text[index]))
                        {
                            return IsNegated ? -1 : index + (range.Letters?.Length ?? 1);
                        }
                    }
                    return IsNegated ? index + 1 : -1;
                default:
                    throw new InvalidLikePatternException();
            }
        }
    }
}

/// <summary>
/// A <c>Like</c> match reached an element of its pattern that is not a valid <c>like-pattern-element</c> (<strong>MS-VBAL §5.6.9.6</strong>).
/// </summary>
internal sealed class InvalidLikePatternException : Exception
{
}
