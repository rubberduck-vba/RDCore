#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBStringType"/> representing the <c>String*</c><em><c>length</c></em> (fixed-length <c>String</c>) data type.
/// </summary>
/// <summary>
/// Represents a <c>String*</c><em>length</em> fixed-length <c>String</c> value.
/// </summary>
/// <param name="Length">The length of the string value.</param>
/// <remarks>
/// 👉 Fixed-length strings are padded (with spaces) or truncated as needed to meet the specified declared length.<br/>
/// </remarks>
public sealed record class VBFixedStringType(int Length) : VBStringType
{
    private const int _maxLength = 65526;

    /// <summary>
    /// A string of <see cref="Length"/> null characters (U+0000): the initial value of a fixed-length string variable (<strong>MS-VBAL 2.3</strong>).
    /// </summary>
    public override VBTypedValue DefaultValue => new VBStringValue(new string('\0', Length));

    /// <summary>
    /// <strong>MS-VBAL 2.2 Entities and Data Types</strong> restricts the maximum length of a fixed-length string to 65,526 characters.
    /// </summary>
    public static int MaxLength { get; } = _maxLength;

    /// <summary>
    /// The largest number a length can be written as, as a literal: one past it is no length at all (MS-VBA: "Syntax error", Excel-verified).
    /// </summary>
    public const int MaxWrittenLength = 65535;

    /// <summary>
    /// Whether a fixed-length string can be <paramref name="length"/> characters long: from 1 to <see cref="MaxLength"/> (MS-VBA: "Invalid length for
    /// fixed-length string" otherwise, Excel-verified).
    /// </summary>
    /// <param name="length">The length, Let-coerced to <c>Long</c>.</param>
    public static bool IsValidLength(long length) => length >= 1 && length <= _maxLength;

    /// <summary>
    /// Reads a length written as a literal (<strong>MS-VBAL §5.2.3.1.4</strong> <c>&lt;string-length&gt;</c>, an <c>INTEGER</c>): decimal, <c>&amp;H</c>
    /// hexadecimal or <c>&amp;O</c> octal, with or without a type-declaration character.
    /// </summary>
    /// <param name="text">The length as written.</param>
    /// <param name="length">The number it is.</param>
    /// <returns><see langword="false"/> when the text is no integer literal: <c>1.5</c>, or a name.</returns>
    public static bool TryParseLengthLiteral(string text, out long length)
    {
        length = 0;
        var digits = text.TrimEnd('%', '&', '^', '!', '#', '@');
        if (digits.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(digits[2..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out length);
        }

        if (digits.StartsWith("&O", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var digit in digits[2..])
            {
                if (digit is < '0' or > '7' || length > long.MaxValue / 8)
                {
                    return false;
                }

                length = length * 8 + (digit - '0');
            }

            return digits.Length > 2;
        }

        return long.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out length);
    }
}