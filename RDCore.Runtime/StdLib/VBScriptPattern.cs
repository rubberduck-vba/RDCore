using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RDCore.Runtime.StdLib;

/// <summary>
/// A regular expression of VBScript 5.5 (<strong>RD-VBAL §6.2</strong>), as one of .NET's that matches what it matches in MS-VBA.
/// </summary>
/// <remarks>
/// <para>
/// The syntax is JScript's, which ECMAScript 3 describes, with JScript's leniencies: an escape that means nothing is the character escaped (<c>\p</c>,
/// <c>\k</c>, <c>\Z</c>), a brace that is no quantifier is a brace (<c>a{,2}</c>), and a backreference to a group the pattern does not have is an octal
/// escape (<c>\2</c>) or a digit (<c>\8</c>). .NET reads most of these otherwise, so the pattern is translated, construct by construct, rather than handed
/// over: every character is written as itself, every class as the characters it has, and every anchor as what it asserts. What is translated needs no
/// option of .NET's to mean what it says.
/// </para>
/// <para>
/// What MS-VBA does, recorded in Excel (oracle\s26): <c>\d</c>, <c>\w</c>, <c>\s</c> and <c>\b</c> are ASCII; <c>.</c> is any character but a line feed;
/// <c>^</c> and <c>$</c> are the start and the very end of the text, or of a line, which a carriage return, a line feed, U+2028 or U+2029 each end; a
/// backreference to a group that has not matched fails; a group inside a repetition is what it matched last. A letter matches its other case when the two
/// are the same in lower case - except the Kelvin, Ohm and Angstrom signs, which are not their letters - so the text is matched in lower case, character for
/// character (<see cref="Subject"/>), against a pattern whose characters are too.
/// </para>
/// </remarks>
internal sealed class VBScriptPattern
{
    /// <summary>Syntax error in regular expression.</summary>
    public const int SyntaxError = 5017;

    /// <summary>Unexpected quantifier.</summary>
    public const int UnexpectedQuantifier = 5018;

    /// <summary>Expected ']' in regular expression.</summary>
    public const int ExpectedBracket = 5019;

    /// <summary>Expected ')' in regular expression.</summary>
    public const int ExpectedParenthesis = 5020;

    /// <summary>Invalid range in character set.</summary>
    public const int InvalidRange = 5021;

    private VBScriptPattern(Regex regex, int groupCount, bool ignoreCase)
    {
        Regex = regex;
        GroupCount = groupCount;
        IgnoreCase = ignoreCase;
    }

    /// <summary>The expression of .NET's that matches what the pattern does, against <see cref="Subject"/>.</summary>
    public Regex Regex { get; }

    /// <summary>How many capturing groups the pattern has.</summary>
    public int GroupCount { get; }

    /// <summary>Whether a letter matches either of its cases.</summary>
    public bool IgnoreCase { get; }

    /// <summary>
    /// What the expression is matched against for <paramref name="text"/>: the text itself, or, when case is ignored, each of its characters in lower case.
    /// One character stays one character, so a match's position and length are the text's.
    /// </summary>
    public string Subject(string text) => IgnoreCase ? string.Create(text.Length, text, (folded, source) =>
    {
        for (var index = 0; index < source.Length; index++)
        {
            folded[index] = Fold(source[index]);
        }
    }) : text;

    /// <summary>
    /// Translates a pattern.
    /// </summary>
    /// <param name="pattern">The pattern, as the program wrote it.</param>
    /// <param name="ignoreCase">Whether a letter matches either of its cases.</param>
    /// <param name="multiline">Whether <c>^</c> and <c>$</c> are the start and the end of each line.</param>
    /// <param name="translated">The pattern, when it is one.</param>
    /// <param name="error">The number of the run-time error that it is not one, from <see cref="SyntaxError"/> to <see cref="InvalidRange"/>.</param>
    /// <param name="reason">What is wrong with it, and where.</param>
    public static bool TryTranslate(string pattern, bool ignoreCase, bool multiline, out VBScriptPattern? translated, out int error, out string? reason)
    {
        var translator = new Translator(pattern, ignoreCase, multiline);
        if (!translator.Translate(out var expression, out error, out reason))
        {
            translated = null;
            return false;
        }

        translated = new VBScriptPattern(new Regex(expression, RegexOptions.CultureInvariant), translator.GroupCount, ignoreCase);
        return true;
    }

    // a character's lower case, as the comparison that ignores case has it: the compatibility signs that are letters' look-alikes are not those letters.
    private static char Fold(char character) => character switch
    {
        'İ' => 'i',
        'Ω' or 'K' or 'Å' => character,
        _ => char.ToLowerInvariant(character),
    };

    private sealed class PatternException(int error, string reason) : Exception(reason)
    {
        public int Error { get; } = error;
    }

    // what the last term of an alternative was, which says whether a quantifier can follow it.
    private enum Term
    {
        None,
        Atom,
        Assertion,
        Quantified,
    }

    private sealed class Translator(string pattern, bool ignoreCase, bool multiline)
    {
        private const string LineTerminators = @"\n\r  ";
        private const string WordCharacter = "[0-9A-Za-z_]";

        private static readonly (char Low, char High)[] Digits = [('0', '9')];
        private static readonly (char Low, char High)[] WordCharacters = [('0', '9'), ('A', 'Z'), ('_', '_'), ('a', 'z')];
        private static readonly (char Low, char High)[] Spaces = [('\t', '\r'), (' ', ' ')];

        private readonly StringBuilder _expression = new();
        private int _position;

        public int GroupCount { get; } = CountGroups(pattern);

        public bool Translate(out string expression, out int error, out string? reason)
        {
            try
            {
                Disjunction();
                if (_position < pattern.Length)
                {
                    // the only thing a disjunction stops at, at the top, is a parenthesis that closes nothing.
                    throw Fail(SyntaxError, "a ')' closes no group");
                }

                expression = _expression.ToString();
                error = 0;
                reason = null;
                return true;
            }
            catch (PatternException failure)
            {
                expression = string.Empty;
                error = failure.Error;
                reason = failure.Message;
                return false;
            }
        }

        private PatternException Fail(int error, string what) => new(error, $"{what}, at position {_position + 1} of the pattern");

        private bool AtEnd => _position >= pattern.Length;

        private char Current => pattern[_position];

        private char? Peek(int offset = 1) => _position + offset < pattern.Length ? pattern[_position + offset] : null;

        private void Disjunction()
        {
            Alternative();
            while (!AtEnd && Current == '|')
            {
                _expression.Append('|');
                _position++;
                Alternative();
            }
        }

        private void Alternative()
        {
            var last = Term.None;
            while (!AtEnd && Current is not ('|' or ')'))
            {
                if (TryQuantifier(out var quantifier))
                {
                    if (last != Term.Atom)
                    {
                        throw Fail(UnexpectedQuantifier, "a quantifier follows nothing it can repeat");
                    }

                    _expression.Append(quantifier);
                    last = Term.Quantified;
                    continue;
                }

                last = TermAt();
            }
        }

        // *, +, ?, {n}, {n,} or {n,m}, and a ? after it that makes it lazy. A brace that is none of these is no quantifier, but a brace.
        private bool TryQuantifier(out string quantifier)
        {
            quantifier = string.Empty;
            switch (Current)
            {
                case '*' or '+' or '?':
                    quantifier = Current.ToString();
                    _position++;
                    break;

                case '{' when TryBraces(out var minimum, out var maximum, out var length):
                    if (maximum < minimum)
                    {
                        throw Fail(SyntaxError, "a quantifier's maximum is less than its minimum");
                    }

                    quantifier = maximum is null ? $"{{{minimum},}}" : minimum == maximum ? $"{{{minimum}}}" : $"{{{minimum},{maximum}}}";
                    _position += length;
                    break;

                default:
                    return false;
            }

            if (!AtEnd && Current == '?')
            {
                quantifier += "?";
                _position++;
            }

            return true;
        }

        private bool TryBraces(out long minimum, out long? maximum, out int length)
        {
            minimum = 0;
            maximum = null;
            length = 0;

            var index = _position + 1;
            var first = Number(ref index);
            if (first is null)
            {
                return false;
            }

            minimum = first.Value;
            if (index < pattern.Length && pattern[index] == '}')
            {
                maximum = minimum;
                length = index + 1 - _position;
                return true;
            }

            if (index >= pattern.Length || pattern[index] != ',')
            {
                return false;
            }

            index++;
            var second = Number(ref index);
            if (index >= pattern.Length || pattern[index] != '}')
            {
                return false;
            }

            maximum = second;
            length = index + 1 - _position;
            return true;
        }

        // the digits at index, as a count .NET can take: one too large to be one is as large as one can be.
        private long? Number(ref int index)
        {
            var start = index;
            while (index < pattern.Length && char.IsAsciiDigit(pattern[index]))
            {
                index++;
            }

            if (index == start)
            {
                return null;
            }

            return long.TryParse(pattern.AsSpan(start, index - start), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? Math.Min(value, int.MaxValue)
                : int.MaxValue;
        }

        private Term TermAt()
        {
            var character = Current;
            switch (character)
            {
                case '^':
                    _position++;
                    _expression.Append(multiline ? $"(?<![^{LineTerminators}])" : @"\A");
                    return Term.Assertion;

                case '$':
                    _position++;
                    _expression.Append(multiline ? $"(?![^{LineTerminators}])" : @"\z");
                    return Term.Assertion;

                case '.':
                    _position++;
                    _expression.Append(@"[^\n]");
                    return Term.Atom;

                case '(':
                    Group();
                    return Term.Atom;

                case '[':
                    Class();
                    return Term.Atom;

                case '\\':
                    return Escape();

                default:
                    _position++;
                    Literal(character);
                    return Term.Atom;
            }
        }

        private void Group()
        {
            _position++;
            if (!AtEnd && Current == '?')
            {
                var kind = Peek();
                if (kind is not (':' or '=' or '!'))
                {
                    throw Fail(SyntaxError, "'(?' starts no group VBScript has");
                }

                _expression.Append("(?").Append(kind);
                _position += 2;
            }
            else
            {
                _expression.Append('(');
            }

            Disjunction();
            if (AtEnd)
            {
                throw Fail(ExpectedParenthesis, "a group is not closed");
            }

            _position++;
            _expression.Append(')');
        }

        private Term Escape()
        {
            if (Peek() is not { } escaped)
            {
                throw Fail(SyntaxError, "the pattern ends with a backslash");
            }

            switch (escaped)
            {
                case 'b':
                    _position += 2;
                    _expression.Append($"(?:(?<={WordCharacter})(?!{WordCharacter})|(?<!{WordCharacter})(?={WordCharacter}))");
                    return Term.Assertion;

                case 'B':
                    _position += 2;
                    _expression.Append($"(?:(?<={WordCharacter})(?={WordCharacter})|(?<!{WordCharacter})(?!{WordCharacter}))");
                    return Term.Assertion;

                case 'd' or 'D' or 'w' or 'W' or 's' or 'S':
                    _position += 2;
                    AppendClass(false, [], [.. ClassEscape(escaped)]);
                    return Term.Atom;

                case >= '1' and <= '9' when Backreference() is { } group:
                    _expression.Append($@"\k<{group}>");
                    return Term.Atom;

                default:
                    _position++;
                    if (CharacterEscape() is { } character)
                    {
                        Literal(character);
                    }
                    else
                    {
                        // \c and no letter: what JScript makes of it matches nothing (oracle\s26 U01).
                        _expression.Append("(?!)");
                    }

                    return Term.Atom;
            }
        }

        // \1 to \99, when the pattern has that many groups: \10 is group 10 of a pattern that has ten, and group 1 followed by 0 of one that has fewer.
        // Otherwise it is no backreference, and what it is is the escape's (CharacterEscape).
        private int? Backreference()
        {
            var first = pattern[_position + 1] - '0';
            if (Peek(2) is { } next && char.IsAsciiDigit(next) && first * 10 + (next - '0') is var both && both <= GroupCount)
            {
                _position += 3;
                return both;
            }

            if (first <= GroupCount)
            {
                _position += 2;
                return first;
            }

            return null;
        }

        // the character an escape stands for, the backslash read: or null for one that stands for none (\c and no letter).
        private char? CharacterEscape()
        {
            var escaped = Current;
            _position++;
            switch (escaped)
            {
                case 'f':
                    return '\f';
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                case 'v':
                    return '\v';
                case 'c':
                    if (!AtEnd && char.IsAsciiLetter(Current))
                    {
                        var letter = Current;
                        _position++;
                        return (char)(letter % 32);
                    }

                    return null;
                case 'x' when Hex(2) is { } code:
                    return code;
                case 'u' when Hex(4) is { } code:
                    return code;
                case >= '0' and <= '7':
                    return Octal(escaped);
                default:
                    return escaped;
            }
        }

        private char? Hex(int digits)
        {
            if (_position + digits > pattern.Length
                || !int.TryParse(pattern.AsSpan(_position, digits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
            {
                return null;
            }

            _position += digits;
            return (char)code;
        }

        // up to three octal digits, as much of them as is a character: \377 is one, \400 is \40 followed by 0.
        private char Octal(char first)
        {
            var value = first - '0';
            var most = first <= '3' ? 2 : 1;
            for (var more = 0; more < most && !AtEnd && Current is >= '0' and <= '7'; more++)
            {
                value = value * 8 + (Current - '0');
                _position++;
            }

            return (char)value;
        }

        private void Literal(char character) => _expression.Append(Escaped(ignoreCase ? Fold(character) : character));

        private static string Escaped(char character) => char.IsAsciiLetterOrDigit(character) ? character.ToString() : $@"\u{(int)character:X4}";

        // a set of characters, written as one: the characters given in it, in lower case when case is ignored, and the ones of its escapes as they are.
        private void Class()
        {
            _position++;
            var negated = !AtEnd && Current == '^';
            if (negated)
            {
                _position++;
            }

            var characters = new List<(char Low, char High)>();
            var escapes = new List<(char Low, char High)>();
            var first = true;
            while (true)
            {
                if (AtEnd)
                {
                    throw Fail(ExpectedBracket, "a character set is not closed");
                }

                // a ']' that is the first of the set is one of its characters.
                if (Current == ']' && !first)
                {
                    _position++;
                    break;
                }

                first = false;
                var low = ClassAtom();
                if (!AtEnd && Current == '-' && Peek() is { } after && after != ']')
                {
                    _position++;
                    var high = ClassAtom();
                    if (low is { Set: { } } || high is { Set: { } })
                    {
                        // a range whose first end is a set is that set, a '-' and the other end; one whose last is, is no range.
                        if (high is { Set: { } })
                        {
                            throw Fail(InvalidRange, "a range ends with a set of characters");
                        }

                        Add(low);
                        characters.Add(('-', '-'));
                        Add(high);
                        continue;
                    }

                    if (low is { Character: { } from } && high is { Character: { } to })
                    {
                        if (to < from)
                        {
                            throw Fail(InvalidRange, "a range's first character is after its last");
                        }

                        characters.Add((from, to));
                        continue;
                    }

                    // \c and no letter at an end: it stands for nothing, and the range is the other end and a '-'.
                    Add(low);
                    characters.Add(('-', '-'));
                    Add(high);
                    continue;
                }

                Add(low);
            }

            AppendClass(negated, characters, escapes);

            void Add((char? Character, (char Low, char High)[]? Set)? atom)
            {
                if (atom is { Character: { } character })
                {
                    characters.Add((character, character));
                }
                else if (atom is { Set: { } set })
                {
                    escapes.AddRange(set);
                }
            }
        }

        private (char? Character, (char Low, char High)[]? Set)? ClassAtom()
        {
            if (Current != '\\')
            {
                var character = Current;
                _position++;
                return (character, null);
            }

            if (Peek() is not { } escaped)
            {
                throw Fail(ExpectedBracket, "a character set is not closed");
            }

            switch (escaped)
            {
                case 'd' or 'D' or 'w' or 'W' or 's' or 'S':
                    _position += 2;
                    return (null, ClassEscape(escaped));

                // in a set, \b is a backspace.
                case 'b':
                    _position += 2;
                    return ('\b', null);

                default:
                    _position++;
                    return CharacterEscape() is { } character ? (character, null) : null;
            }
        }

        private static (char Low, char High)[] ClassEscape(char escape) => escape switch
        {
            'd' => Digits,
            'D' => Complement(Digits),
            'w' => WordCharacters,
            'W' => Complement(WordCharacters),
            's' => Spaces,
            _ => Complement(Spaces),
        };

        private static (char Low, char High)[] Complement((char Low, char High)[] ranges)
        {
            var complement = new List<(char, char)>();
            var next = 0;
            foreach (var (low, high) in ranges)
            {
                if (low > next)
                {
                    complement.Add(((char)next, (char)(low - 1)));
                }

                next = high + 1;
            }

            if (next <= char.MaxValue)
            {
                complement.Add(((char)next, char.MaxValue));
            }

            return [.. complement];
        }

        private void AppendClass(bool negated, List<(char Low, char High)> characters, List<(char Low, char High)> escapes)
        {
            var members = new bool[char.MaxValue + 1];
            foreach (var (low, high) in characters)
            {
                for (int character = low; character <= high; character++)
                {
                    members[ignoreCase ? Fold((char)character) : character] = true;
                }
            }

            foreach (var (low, high) in escapes)
            {
                for (int character = low; character <= high; character++)
                {
                    members[character] = true;
                }
            }

            var any = false;
            var written = new StringBuilder("[");
            if (negated)
            {
                written.Append('^');
            }

            for (var character = 0; character <= char.MaxValue; character++)
            {
                if (!members[character])
                {
                    continue;
                }

                var end = character;
                while (end < char.MaxValue && members[end + 1])
                {
                    end++;
                }

                written.Append($@"\u{character:X4}");
                if (end > character)
                {
                    written.Append($@"-\u{end:X4}");
                }

                any = true;
                character = end;
            }

            // a set that has no character matches none, and one that is all but none matches any.
            _expression.Append(any ? written.Append(']').ToString() : negated ? @"[\s\S]" : "(?!)");
        }

        // the capturing groups, which a backreference is told from an escape by: every '(' that is no '(?', outside a set and not escaped.
        private static int CountGroups(string pattern)
        {
            var count = 0;
            for (var index = 0; index < pattern.Length; index++)
            {
                switch (pattern[index])
                {
                    case '\\':
                        index++;
                        break;

                    case '[':
                        index++;
                        if (index < pattern.Length && pattern[index] == '^')
                        {
                            index++;
                        }

                        // a ']' that is the first of the set is one of its characters.
                        if (index < pattern.Length && pattern[index] == ']')
                        {
                            index++;
                        }

                        while (index < pattern.Length && pattern[index] != ']')
                        {
                            index += pattern[index] == '\\' ? 2 : 1;
                        }

                        break;

                    case '(' when index + 1 >= pattern.Length || pattern[index + 1] != '?':
                        count++;
                        break;
                }
            }

            return count;
        }
    }
}
