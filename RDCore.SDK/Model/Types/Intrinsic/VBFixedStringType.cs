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
}