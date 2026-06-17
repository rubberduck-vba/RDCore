using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Model.Values.Intrinsic;

/// <summary>
/// Represents and holds a <c>String</c> value.
/// </summary>
public record class VBStringValue : VBTypedValue, IVBTypedValue<VBStringValue, string>
{
    /// <summary>
    /// Creates a new <c>VBStringValue</c> associated with the specified symbol.
    /// </summary>
    /// <param name="symbol">The symbol to be associated with this value.</param>
    public VBStringValue(Symbol symbol) : base(VBStringType.TypeInfo, symbol) { }

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


    private static readonly Lazy<VBStringValue> _vbNullString = new(() => new VBStringValue(GlobalSymbols.StaticSymbols.VBNullString), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the <em>static value</em> associated with <see cref="GlobalSymbols.StaticSymbols.VBNullString"/>.
    /// </summary>
    public static VBStringValue VBNullString => _vbNullString.Value;

    private static readonly Lazy<VBStringValue> _zeroString = new(() => new VBStringValue(GlobalSymbols.StaticSymbols.VBEmptyString) { Value = string.Empty }, LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the <em>static value</em> associated with <see cref="GlobalSymbols.StaticSymbols.VBEmptyString"/>.
    /// </summary>
    public static VBStringValue ZeroLengthString => _zeroString.Value;

    public string Value { get; init; } = default!;
    public virtual int Length => Value?.Length ?? 0;
    public override int Size => Value is null ? 0 : 2 * Length + 2;

    public override object BoxedValue => Value;

    public virtual VBStringValue WithValue(string? value) => this with { Value = value ?? string.Empty };

    public override string ToString() => Value ?? Tokens.vbNullString;

    public bool Equals(IVBTypedValue<VBStringValue, string>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
