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
    /// What text mode disregards when it compares text: the case of a letter, and nothing else.
    /// </summary>
    /// <remarks>
    /// In text mode "the text of the strings is compared in a case-insensitive manner, according to the host-defined regional settings"
    /// (<strong>MS-VBAL §5.6.9.5</strong>). Every text comparison here is built from this one value - <see cref="Comparer"/> and the
    /// <see cref="Compare(ReadOnlySpan{char}, ReadOnlySpan{char})"/> of <c>StrComp</c> - so
    /// <c>StrComp(a, b, vbTextCompare) = 0</c> exactly when <c>a = b</c> under <c>Option Compare Text</c>, by construction. Folding
    /// kana type and width as well would be one host's collation rather than the specification's case-insensitive comparison.
    /// <para>
    /// 👉 <c>Like</c> does not read this: it matches with <see cref="IgnoresCase"/> and <see cref="EffectiveCulture"/> on its own terms.
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
        => IgnoresCase
            ? Math.Sign(EffectiveCulture.CompareInfo.Compare(left, right, TextOptions))
            : Math.Sign(CompareBinary(MemoryMarshal.AsBytes(left), MemoryMarshal.AsBytes(right)));

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
