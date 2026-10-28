using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace RDCore.SDK.Runtime;

/// <summary>
/// How the relational operators compare <c>String</c> values where an operation is evaluated or analyzed (<strong>MS-VBAL §5.6.9.5</strong>):
/// the comparison mode of the module, and the regional settings of the environment.
/// </summary>
/// <param name="Mode">
/// The comparison mode of the module (<strong>MS-VBAL §5.2.1.1</strong>): <see cref="OptionCompare.Text"/> compares regardless of case,
/// anything else compares the data of the strings (<see cref="Compare(VBStringValue, VBStringValue)"/>). Never
/// <see cref="OptionCompare.Database"/>: that is resolved to one of the two by the platform.
/// </param>
/// <param name="Culture">
/// The regional settings of the environment, that text is collated by. The invariant culture when none is given.
/// </param>
public readonly record struct StringComparisonRules(OptionCompare Mode = OptionCompare.Binary, CultureInfo? Culture = null)
{
    /// <summary>
    /// The culture text comparisons are collated by.
    /// </summary>
    public CultureInfo EffectiveCulture => Culture ?? CultureInfo.InvariantCulture;

    /// <summary>
    /// Whether strings that differ in case only are equal.
    /// </summary>
    public bool IgnoresCase => Mode == OptionCompare.Text;

    /// <summary>
    /// Whether this is the text comparison of a locale that a <c>Compare</c> argument names by its identifier (<see cref="TryForLocale"/>), rather than the
    /// text mode of the environment: a search then folds case the same way in every locale, and keeps the letters the locale's alphabet has
    /// (<see cref="TextModeCharacters.KeepsAshAndThorn"/>).
    /// </summary>
    public bool IsLocaleComparison { get; init; }

    /// <summary>
    /// The text comparison a <c>Compare</c> argument names when it is neither <c>vbBinaryCompare</c>, <c>vbTextCompare</c> nor <c>vbDatabaseCompare</c>: the
    /// locale whose identifier it is, if there is one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MS-VBA reads such a <c>Compare</c> as a Windows locale identifier (LCID), and compares text case-insensitively by that locale's collation: a neutral
    /// language (<c>3</c>, Catalan), a locale (<c>1033</c>), a locale with a sort order (<c>66567</c>, German phone book), and the identifiers of the user's,
    /// the system's and the custom default locales (<c>1024</c>, <c>2048</c>, <c>3072</c>, <c>4096</c>, <c>5120</c>), which are the environment's here. An
    /// identifier of no locale - <c>240</c>, <c>5000</c>, <c>65535</c>, a negative number - is no comparison, and the member's error 5. MS-VBAL's
    /// <c>VbCompareMethod</c> (§6.1.1.5) names two comparisons, <c>vbBinaryCompare</c> and <c>vbTextCompare</c>, and no locale.
    /// </para>
    /// <para>
    /// The locale's collation orders the strings (<c>StrComp</c>): <c>ä</c> comes after <c>z</c> in Swedish, <c>æ</c> after <c>z</c> in Danish. Its case is
    /// not: <c>I</c> and <c>i</c> are the two cases of one letter in a Turkish comparison too, as in MS-VBA's.
    /// </para>
    /// </remarks>
    /// <param name="lcid">The identifier.</param>
    /// <param name="environment">The regional settings of the environment, which the default locales' identifiers name.</param>
    /// <param name="rules">The text comparison of the locale.</param>
    /// <returns><see langword="false"/> when <paramref name="lcid"/> identifies no locale.</returns>
    public static bool TryForLocale(int lcid, CultureInfo environment, out StringComparisonRules rules)
    {
        rules = default;
        CultureInfo culture;
        if (lcid is LocaleUserDefault or LocaleSystemDefault or LocaleCustomDefault or LocaleCustomUnspecified or LocaleCustomUserInterfaceDefault)
        {
            culture = environment;
        }
        else
        {
            if (lcid <= 0)
            {
                return false;
            }

            try
            {
                culture = CultureInfo.GetCultureInfo(lcid);
            }
            catch (CultureNotFoundException)
            {
                return false;
            }
        }

        rules = new StringComparisonRules(OptionCompare.Text, culture) { IsLocaleComparison = true };
        return true;
    }

    // the identifiers Windows gives the default locales (LOCALE_USER_DEFAULT, LOCALE_SYSTEM_DEFAULT, LOCALE_CUSTOM_DEFAULT, LOCALE_CUSTOM_UNSPECIFIED,
    // LOCALE_CUSTOM_UI_DEFAULT), which name no culture of their own.
    private const int LocaleUserDefault = 0x0400;
    private const int LocaleSystemDefault = 0x0800;
    private const int LocaleCustomDefault = 0x0C00;
    private const int LocaleCustomUnspecified = 0x1000;
    private const int LocaleCustomUserInterfaceDefault = 0x1400;

    // the casing a search folds with, and whether æ and þ are letters of their own: a locale's comparison folds case the same way in every locale.
    private (TextInfo Casing, bool KeepsAshAndThorn) SearchLetters
        => IsLocaleComparison
            ? (CultureInfo.InvariantCulture.TextInfo, TextModeCharacters.KeepsAshAndThorn(EffectiveCulture))
            : (EffectiveCulture.TextInfo, false);

    /// <summary>
    /// What text mode disregards when it compares text: the case of a letter, and nothing else.
    /// </summary>
    /// <remarks>
    /// In text mode "the text of the strings is compared in a case-insensitive manner, according to the host-defined regional settings"
    /// (<strong>MS-VBAL §5.6.9.5</strong>). Every text comparison here is built from this one value - <see cref="Comparer"/>, and the
    /// <see cref="Compare(ReadOnlySpan{char}, ReadOnlySpan{char})"/> of <c>StrComp</c> - so <c>StrComp(a, b, vbTextCompare) = 0</c> exactly when <c>a = b</c>
    /// under <c>Option Compare Text</c>, by construction. Folding kana type and width as well would be one host's collation rather than the specification's
    /// case-insensitive comparison.
    /// <para>
    /// 👉 <c>Like</c> and the search of <see cref="TryFind"/> do not read this: they match the letters of <see cref="TextModeCharacters"/>, as MS-VBA's do.
    /// </para>
    /// </remarks>
    internal const CompareOptions TextOptions = CompareOptions.IgnoreCase;

    /// <summary>
    /// Compares strings the way the relational operators do: by their data in binary mode (<see cref="Compare(VBStringValue, VBStringValue)"/>),
    /// and regardless of case according to the environment's collation in text mode.
    /// </summary>
    /// <remarks>
    /// 👉 A <see cref="string"/> is whole characters, so this cannot see a trailing byte: <see cref="Compare(VBStringValue, VBStringValue)"/>
    /// is what compares two <c>String</c> values, and agrees with this wherever neither has one.
    /// </remarks>
    public StringComparer Comparer => IgnoresCase ? EffectiveCulture.CompareInfo.GetStringComparer(TextOptions) : BinaryComparer.Instance;

    /// <summary>
    /// Compares two strings of whole characters the way the relational operators do, as <c>StrComp</c> reports it.
    /// </summary>
    /// <param name="left">The characters of the left operand.</param>
    /// <param name="right">The characters of the right operand.</param>
    /// <returns><c>-1</c> when <paramref name="left"/> is less than <paramref name="right"/>, <c>0</c> when they are equal, and
    /// <c>1</c> otherwise.</returns>
    public int Compare(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
        => !IgnoresCase ? Math.Sign(CompareBinary(MemoryMarshal.AsBytes(left), MemoryMarshal.AsBytes(right)))
            : IsLocaleComparison ? Math.Sign(EffectiveCulture.CompareInfo.Compare(Folded(left), Folded(right), TextOptions))
            : Math.Sign(EffectiveCulture.CompareInfo.Compare(left, right, TextOptions));

    // a locale's comparison folds case the same way in every locale, then orders by the locale's collation.
    private static string Folded(ReadOnlySpan<char> text)
    {
        var casing = CultureInfo.InvariantCulture.TextInfo;
        var folded = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            folded[i] = TextModeCharacters.Fold(text[i], casing);
        }

        return new string(folded);
    }

    /// <summary>
    /// Compares two <c>String</c> values the way the relational operators do (<strong>MS-VBAL §5.6.9.5</strong>), a trailing byte
    /// included.
    /// </summary>
    /// <remarks>
    /// In binary mode "each byte of the implementation-specific representation of the string data is compared", which is every byte
    /// of <see cref="VBStringValue.GetBytes"/>, in the one order <see cref="CompareBinary"/> defines. In text mode "the text of the
    /// strings is compared in a case-insensitive manner", and a trailing byte is not text: it only breaks a tie between two texts
    /// that are equal regardless of case - no trailing byte before any, then by value - since an extra byte is not a difference in
    /// case.
    /// <para>
    /// 🚧 Whether MS-VBA's text mode sees a trailing byte at all is yet to be run against MS-VBA; ignoring it would be returning the
    /// text comparison alone.
    /// </para>
    /// </remarks>
    /// <param name="lhs">The left operand.</param>
    /// <param name="rhs">The right operand.</param>
    /// <returns>Less than zero when <paramref name="lhs"/> is less than <paramref name="rhs"/>, zero when they are equal, and greater than zero otherwise.</returns>
    public int Compare(VBStringValue lhs, VBStringValue rhs)
    {
        if (lhs.TrailingByte is null && rhs.TrailingByte is null)
        {
            return Compare(lhs.Value.AsSpan(), rhs.Value.AsSpan());
        }

        if (IgnoresCase)
        {
            var text = Compare(lhs.Value.AsSpan(), rhs.Value.AsSpan());
            return text != 0 ? text : Nullable.Compare(lhs.TrailingByte, rhs.TrailingByte);
        }

        return CompareBinary(lhs.GetBytes(), rhs.GetBytes());
    }

    /// <summary>
    /// Finds the first occurrence of <paramref name="value"/> in <paramref name="source"/>, the way <c>InStr</c> finds it.
    /// </summary>
    /// <remarks>
    /// Binary mode finds the code units of <paramref name="value"/>, which is where either reading of binary mode finds two strings equal. Text mode is not the
    /// comparison <c>=</c> makes: it searches the letters of both (<see cref="TextModeCharacters.Letters"/>) - folded to one case, and <c>æ</c>, <c>œ</c>,
    /// <c>ß</c> and <c>þ</c> as the two letters they expand to - and an occurrence may start or end inside a character that is expanded. It is found at the
    /// character it starts in: <c>InStr(1, "xæx", "ex", vbTextCompare)</c> is 2, and so is <c>InStr(1, "xaex", "æ", vbTextCompare)</c>. Nothing is ignored:
    /// a soft hyphen, a full-width letter or katakana is a character of its own, where <c>=</c> would ignore it (MS-VBA, recorded in Excel: oracle\instr-text
    /// N01-N06). MS-VBAL says nothing of how a text search matches.
    /// </remarks>
    /// <param name="source">The characters to search.</param>
    /// <param name="value">The characters to search for.</param>
    /// <param name="index">The 0-based position in <paramref name="source"/> of the character the occurrence starts in.</param>
    /// <param name="length">How many characters of <paramref name="source"/> the occurrence takes up, the ones it starts and ends in included.</param>
    /// <returns><c>true</c> when it is found; a zero-length <paramref name="value"/> is found at the start.</returns>
    public bool TryFind(ReadOnlySpan<char> source, ReadOnlySpan<char> value, out int index, out int length)
        => Find(source, value, last: false, out index, out length);

    /// <summary>
    /// Finds the last occurrence of <paramref name="value"/> in <paramref name="source"/>, the way <see cref="TryFind"/> finds the first.
    /// </summary>
    /// <param name="source">The characters to search.</param>
    /// <param name="value">The characters to search for.</param>
    /// <param name="index">The 0-based position in <paramref name="source"/> of the character the occurrence starts in.</param>
    /// <param name="length">How many characters of <paramref name="source"/> the occurrence takes up, the ones it starts and ends in included.</param>
    /// <returns><c>true</c> when it is found; a zero-length <paramref name="value"/> is found at the end.</returns>
    public bool TryFindLast(ReadOnlySpan<char> source, ReadOnlySpan<char> value, out int index, out int length)
        => Find(source, value, last: true, out index, out length);

    /// <summary>
    /// Every occurrence of <paramref name="value"/> in <paramref name="source"/> that is made of whole characters, from left to right and never overlapping,
    /// the way <c>Replace</c> and <c>Split</c> find them: each search resumes after the occurrence it found.
    /// </summary>
    /// <remarks>
    /// Text mode searches the letters as <see cref="TryFind"/> does, and takes an occurrence only where it starts and ends with a character: a part of a
    /// character that is expanded is not replaced, nor split at. <c>Replace("xæx", "a", "-", , , vbTextCompare)</c> is <c>"xæx"</c>, and
    /// <c>Replace("xaex", "æ", "-", , , vbTextCompare)</c> is <c>"x-x"</c> (MS-VBA, recorded in Excel: oracle\instr-text N07). A zero-length
    /// <paramref name="value"/> finds nothing - there is no run to replace or to split at.
    /// </remarks>
    /// <param name="source">The characters to search.</param>
    /// <param name="value">The characters to search for.</param>
    public IEnumerable<(int Index, int Length)> FindAll(string source, string value)
    {
        if (value.Length == 0)
        {
            yield break;
        }

        if (!IgnoresCase)
        {
            var start = 0;
            while (start <= source.Length && source.AsSpan(start).IndexOf(value) is var index and >= 0)
            {
                yield return (start + index, value.Length);
                start += index + value.Length;
            }

            yield break;
        }

        var (casing, keeps) = SearchLetters;
        var letters = TextModeCharacters.Letters(source, casing, out var characterOf, keeps);
        var sought = TextModeCharacters.Letters(value, casing, out _, keeps);
        var from = 0;
        while (from <= letters.Length && letters.IndexOf(sought, from, StringComparison.Ordinal) is var first and >= 0)
        {
            var end = first + sought.Length;
            if (StartsCharacter(characterOf, first) && StartsCharacter(characterOf, end))
            {
                yield return (characterOf[first], characterOf[end - 1] - characterOf[first] + 1);
                from = end;
            }
            else
            {
                from = first + 1;
            }
        }
    }

    // whether the letter at index is the first of its character: the start of the text, or of another character than the letter before; the end of the text too.
    private static bool StartsCharacter(int[] characterOf, int index)
        => index == 0 || index == characterOf.Length || characterOf[index - 1] != characterOf[index];

    private bool Find(ReadOnlySpan<char> source, ReadOnlySpan<char> value, bool last, out int index, out int length)
    {
        length = value.Length;
        if (value.IsEmpty)
        {
            index = last ? source.Length : 0;
            return true;
        }

        if (!IgnoresCase)
        {
            index = last ? source.LastIndexOf(value) : source.IndexOf(value);
            return index >= 0;
        }

        var (casing, keeps) = SearchLetters;
        var letters = TextModeCharacters.Letters(source, casing, out var characterOf, keeps);
        var sought = TextModeCharacters.Letters(value, casing, out _, keeps);
        var at = last ? letters.LastIndexOf(sought, StringComparison.Ordinal) : letters.IndexOf(sought, StringComparison.Ordinal);
        if (at < 0)
        {
            index = -1;
            return false;
        }

        index = characterOf[at];
        length = characterOf[at + sought.Length - 1] - index + 1;
        return true;
    }

    /// <summary>
    /// 🎯 Which reading of binary mode <see cref="CompareBinary"/> takes: <c>false</c> orders the data of two strings by its code units,
    /// then by a trailing byte (<see cref="CompareCodeUnits"/>); <c>true</c> orders it by its bytes (<see cref="CompareByteImages"/>).
    /// </summary>
    /// <remarks>
    /// The two agree on which strings are equal, and on the order of two strings of whole characters whose code units are all below
    /// <c>U+0100</c>.
    /// <para>
    /// 🚧 Which one MS-VBA takes is yet to be run against it, and the other goes when it has been. The byte image is the literal reading
    /// of §5.6.9.5 over RDCore's little-endian format, where <c>"Ł"</c> (<c>41 01</c>) is less than <c>"B"</c> (<c>42 00</c>); code units
    /// are the order binary mode has always had here, where it is greater.
    /// </para>
    /// </remarks>
    internal const bool BinaryModeComparesByteImages = false;

    // the one binary order: every binary comparison of String data goes through here, the StringComparer's included.
    private static int CompareBinary(ReadOnlySpan<byte> lhs, ReadOnlySpan<byte> rhs)
        => BinaryModeComparesByteImages ? CompareByteImages(lhs, rhs) : CompareCodeUnits(lhs, rhs);

    /// <summary>
    /// Orders the data of two strings by its code units, then by a trailing byte: a string whose characters are those of the other
    /// followed by more is greater, and between the same characters, no trailing byte is less than any, and trailing bytes are ordered by
    /// value.
    /// </summary>
    /// <param name="lhs">The data of the left operand, two bytes per code unit, little-endian.</param>
    /// <param name="rhs">The data of the right operand, two bytes per code unit, little-endian.</param>
    internal static int CompareCodeUnits(ReadOnlySpan<byte> lhs, ReadOnlySpan<byte> rhs)
    {
        var codeUnits = MemoryMarshal.Cast<byte, char>(lhs).SequenceCompareTo(MemoryMarshal.Cast<byte, char>(rhs));
        return codeUnits != 0 ? codeUnits : Nullable.Compare(TrailingByteOf(lhs), TrailingByteOf(rhs));
    }

    /// <summary>
    /// Orders the data of two strings by its bytes, the way <strong>MS-VBAL §5.6.9.5</strong> reads: "each byte of the
    /// implementation-specific representation of the string data is compared, starting from the byte representing the first character of
    /// each string. [...] If all bytes in one string are equal to their respective bytes in the other string, but the other string is
    /// longer, the longer string is considered greater."
    /// </summary>
    /// <param name="lhs">The data of the left operand, two bytes per code unit, little-endian.</param>
    /// <param name="rhs">The data of the right operand, two bytes per code unit, little-endian.</param>
    internal static int CompareByteImages(ReadOnlySpan<byte> lhs, ReadOnlySpan<byte> rhs) => lhs.SequenceCompareTo(rhs);

    private static byte? TrailingByteOf(ReadOnlySpan<byte> data) => data.Length % 2 == 0 ? null : data[^1];

    /// <summary>
    /// The binary mode of <see cref="Comparer"/>: <see cref="CompareBinary"/> over the data of each string, <c>null</c> read as the
    /// zero-length string it compares equal to.
    /// </summary>
    /// <remarks>
    /// Both readings of binary mode find two strings equal exactly when their code units are, so equality and hashing are ordinal.
    /// </remarks>
    private sealed class BinaryComparer : StringComparer
    {
        public static BinaryComparer Instance { get; } = new();

        public override int Compare(string? x, string? y)
            => CompareBinary(MemoryMarshal.AsBytes((x ?? string.Empty).AsSpan()), MemoryMarshal.AsBytes((y ?? string.Empty).AsSpan()));

        public override bool Equals(string? x, string? y) => string.Equals(x ?? string.Empty, y ?? string.Empty, StringComparison.Ordinal);

        public override int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(obj ?? string.Empty);
    }
}
