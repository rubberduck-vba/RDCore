using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents and holds a <c>String</c> value.
/// </summary>
public record class VBStringValue : VBTypedValue, IVBTypedValue<VBStringValue, string>
{
    /// <summary>
    /// Creates a new <c>VBStringValue</c>.
    /// </summary>
    public VBStringValue(IBindingHandle handle) : base(VBStringType.TypeInfo) 
    {
        Handle = handle;
    }
    /// <summary>
    /// Creates a new <c>VBStringValue</c>.
    /// </summary>
    public VBStringValue(string value) : this(new ValueBindingHandle(new VBRuntimeValue<string>(value))) { }

    public const string Zero = "0";
    /// <summary>
    /// Defined in MS-VBAL 5.5.1.2.4 Let-coercion to and from String.
    /// </summary>
    /// <remarks>
    /// Does not appear to be actually implemented in MS-VBA.
    /// </remarks>
    public const string PositiveInfinity = "1.#INF";
    /// <summary>
    /// Defined in MS-VBAL 5.5.1.2.4 Let-coercion to and from String.
    /// </summary>
    /// <remarks>
    /// Does not appear to be actually implemented in MS-VBA, but is explicitly specified as having the literal same string value as <c>PositiveInfinity</c>.
    /// Given the presence of transcription errors elsewhere and that this looks like one, the token is defined here with a negation prefix 
    /// that distinguishes the two infinity types, as was probably intended - inferred from the presence of separate specifications for positive and negative infinity.
    /// </remarks>
    public const string NegativeInfinity = "-1.#INF";
    /// <summary>
    /// Defined in MS-VBAL 5.5.1.2.4 Let-coercion to and from String.
    /// </summary>
    /// <remarks>
    /// Does not appear to be actually implemented in MS-VBA. This token is actually specified as "-1.#IND", but given the apparent typographical error in the negative infinity specification,
    /// the specified negation prefix present in this token is deemed to have been intended for the negative infinity token instead.
    /// </remarks>
    public const string NaN = "1.#IND";

    /// <summary>
    /// Creates a <c>VBStringValue</c> from the source text of a MS-VBAL 3.3.4 <c>&lt;STRING&gt;</c> token —
    /// the literal as it is spelled in source, delimiters included.
    /// </summary>
    /// <param name="token">
    /// The token's source text. Leading and trailing double-quote delimiters are removed when present;
    /// text carrying none is taken as-is, so a caller holding an already-unquoted value can pass it
    /// through without first having to tell the two apart.
    /// </param>
    /// <remarks>
    /// MS-VBAL 3.3.4: "a sequence of two &lt;double-quote&gt; characters represents a single occurrence of
    /// the character U+0022 within the data value" — a literal's data value is therefore never its source
    /// text, and slicing off the delimiters alone leaves every embedded quote doubled.
    /// </remarks>
    public static VBStringValue FromLiteralToken(string token) => new(UnquoteLiteralToken(token));

    /// <summary>
    /// Gets the data value (MS-VBAL 2.1) of a MS-VBAL 3.3.4 <c>&lt;STRING&gt;</c> token from its source text.
    /// </summary>
    /// <param name="token">
    /// The token's source text. Leading and trailing double-quote delimiters are removed when present;
    /// text carrying none is taken as-is.
    /// </param>
    /// <remarks>
    /// The <see cref="string"/> counterpart of <see cref="FromLiteralToken(string)"/>, for callers that
    /// need the data value itself rather than a value bound to it.
    /// </remarks>
    public static string UnquoteLiteralToken(string token)
    {
        var unquoted = token.Length >= 2 && token[0] == '"' && token[^1] == '"'
            ? token[1..^1]
            : token;

        return unquoted.Replace("\"\"", "\"");
    }

    private static readonly Lazy<VBStringValue> _vbNullString = new(() => new VBStringValue((string)null!), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the <em>static value</em> of <c>vbNullString</c>.
    /// </summary>
    public static VBStringValue VBNullString => _vbNullString.Value;

    private static readonly Lazy<VBStringValue> _zeroString = new(() => new VBStringValue(string.Empty), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the <em>static value</em> of the zero-length empty string.
    /// </summary>
    public static VBStringValue ZeroLengthString => _zeroString.Value;

    /// <summary>
    /// Whether this is <c>vbNullString</c> — a <c>String</c> bound to a null pointer rather than to a
    /// zero-length string.
    /// </summary>
    /// <remarks>
    /// The only observable difference between the two in MS-VBA is the pointer: <c>vbNullString = ""</c> is
    /// <c>True</c> and <c>Len(vbNullString)</c> is <c>0</c>, but a null pointer occupies no string storage,
    /// which is what <see cref="Size"/> reports and this tells it.
    /// </remarks>
    public bool IsNullString => RuntimeValue.BoxedValue is null;

    /// <summary>
    /// The characters of the string: every whole code unit of its data.
    /// </summary>
    /// <remarks>
    /// 👉 A <see cref="TrailingByte"/> is not a character, so it is not here: this is what <c>Len</c> counts and what a
    /// character-by-character reader sees.
    /// </remarks>
    public string Value => RuntimeValue.BoxedValue?.ToString() ?? string.Empty;
    public virtual int Length => Value.Length;
    public override int Size => IsNullString ? 0 : ByteLength + 2;

    /// <summary>
    /// The byte left over after the last whole character when the string's data is an odd number of bytes, or <c>null</c>
    /// when it is not.
    /// </summary>
    /// <remarks>
    /// A <c>Byte()</c> with an odd number of elements Let-coerced to <c>String</c> is one (<strong>MS-VBAL §5.5.1.2.6</strong>):
    /// the bytes are "interpreted as if [they represent] the implementation-defined binary format used to store <c>String</c>
    /// data", and RDCore's format keeps every one of them (<see cref="VBRuntimeByteStringValue"/>).
    /// </remarks>
    public byte? TrailingByte => RuntimeValue is VBRuntimeByteStringValue odd ? odd.TrailingByte : null;

    /// <summary>
    /// The number of bytes the string's data occupies: two per character, and one more for a <see cref="TrailingByte"/>.
    /// </summary>
    /// <remarks>
    /// 👉 Measured through <see cref="Length"/>, so a fixed-length string counts every character of its declared length.
    /// </remarks>
    public int ByteLength => 2 * Length + (TrailingByte is null ? 0 : 1);

    /// <summary>
    /// The data of the string: two bytes per character, little-endian, then the <see cref="TrailingByte"/> when there is one.
    /// </summary>
    public byte[] GetBytes() => VBRuntimeByteStringValue.BytesOf(RuntimeValue);

    /// <summary>
    /// The <c>String</c> whose data is <paramref name="bytes"/>, every one of them kept: an odd number leaves a
    /// <see cref="TrailingByte"/>.
    /// </summary>
    /// <param name="bytes">The data of the string, two bytes per character, little-endian.</param>
    public static VBStringValue FromBytes(ReadOnlySpan<byte> bytes) => new(new ValueBindingHandle(VBRuntimeByteStringValue.FromBytes(bytes)));

    /// <summary>
    /// The string whose data is the data of <paramref name="lhs"/> followed by the data of <paramref name="rhs"/>.
    /// </summary>
    /// <remarks>
    /// Two strings of whole characters join their characters. A <see cref="TrailingByte"/> on the left pairs with the first
    /// byte on the right, so every character after it is made of a different pair of bytes than before.
    /// </remarks>
    /// <param name="lhs">The string whose data comes first.</param>
    /// <param name="rhs">The string whose data comes after it.</param>
    public static VBStringValue Concat(VBStringValue lhs, VBStringValue rhs)
        => lhs.TrailingByte is null && rhs.TrailingByte is null
            ? new(string.Concat(lhs.Value, rhs.Value))
            : FromBytes([.. lhs.GetBytes(), .. rhs.GetBytes()]);

    /// <summary>
    /// Returns a copy of this value holding <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    /// 👉 A <see cref="string"/> is whole characters, so the copy never has a <see cref="TrailingByte"/>.
    /// </remarks>
    public virtual VBStringValue WithValue(string? value) => this with { Handle = new ValueBindingHandle(new VBRuntimeValue<string>(value ?? string.Empty)) };

    public override string ToString() => Value;

    // guarded like the managed value VBTypedValue's own equality reads: an unbound value has no data to read.
    private byte? BoundTrailingByte => Handle.BindingCapabilities.HasFlag(BindingCapabilities.GetValue) ? TrailingByte : null;

    /// <summary>
    /// Two <c>String</c> values are equal when they hold the same characters and the same <see cref="TrailingByte"/>, if any.
    /// </summary>
    /// <remarks>
    /// 👉 The managed value <see cref="VBTypedValue"/>'s own equality keys on is the characters only, which two strings
    /// differing in a trailing byte share.
    /// </remarks>
    public virtual bool Equals(VBStringValue? other) => base.Equals(other) && BoundTrailingByte == other!.BoundTrailingByte;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), BoundTrailingByte);

    public bool Equals(IVBTypedValue<VBStringValue, string>? other) => Value == other?.Value && TrailingByte == (other as VBStringValue)?.TrailingByte;
}
