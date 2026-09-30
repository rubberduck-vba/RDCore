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

    public string Value => RuntimeValue.BoxedValue?.ToString() ?? string.Empty;
    public virtual int Length => Value.Length;
    public override int Size => IsNullString ? 0 : 2 * Length + 2;


    public virtual VBStringValue WithValue(string? value) => this with { Handle = new ValueBindingHandle(new VBRuntimeValue<string>(value ?? string.Empty)) };

    public override string ToString() => Value;

    public bool Equals(IVBTypedValue<VBStringValue, string>? other) => Value == other?.Value;
}
