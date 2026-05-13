using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Values;

/// <summary>
/// Represents and holds a <c>String</c> value.
/// </summary>
/// <remarks>
/// MS-VBAL 5.5.1.2.4 Let-coercion to and from String
/// </remarks>
internal record class VBStringValue : VBTypedValue,
    IVBTypedValue<VBStringValue, string>,
    INumericCoercion,
    IStringCoercion,
    IBooleanCoercion,
    IDateCoercion
{
    public VBStringValue(Symbol? symbol = null)
        : base(VBStringType.TypeInfo, symbol) { }

    /// <summary>
    /// Defined in MS-VBAL 5.5.1.2.4 Let-coercion to and from String.
    /// </summary>
    /// <remarks>
    /// Does not appear to be actually implemented in MS-VBA.
    /// </remarks>
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
    /// Does not appear to be actually implemented in MS-VBA.
    /// </remarks>
    public const string NegativeInfinity = "1.#INF";
    /// <summary>
    /// Defined in MS-VBAL 5.5.1.2.4 Let-coercion to and from String.
    /// </summary>
    /// <remarks>
    /// Does not appear to be actually implemented in MS-VBA.
    /// </remarks>
    public const string NaN = "-1.#IND";

    public static VBStringValue VBNullString { get; } = new VBStringValue();
    public static VBStringValue ZeroLengthString { get; } = new VBStringValue { Value = string.Empty };

    public string Value { get; init; } = default!;
    public virtual int Length => Value?.Length ?? 0;
    public override int Size => Value is null ? 0 : 2 * Length + 2;

    public virtual VBStringValue WithValue(string? value) => this with { Value = value ?? string.Empty };

    public override string ToString() => Value ?? Tokens.vbNullString;

    public bool Equals(IVBTypedValue<VBStringValue, string>? other) => Value == other?.Value;
    public override int GetHashCode() => Value.GetHashCode();

    public VBStringValue AsCoercedString(ref int depth) => this;
    public VBFixedStringValue AsCoercedFixedLengthString(int length, ref int depth) => new VBFixedStringValue(length, this.Symbol).WithFixedValue(this.Value);

    public VBDoubleValue AsCoercedDouble(ref int depth)
    {
        if (double.TryParse(Value, CultureInfo, out var coerced))
        {
            return new VBDoubleValue(Symbol).WithValue(coerced);
        }

        ThrowWithSymbol(symbol => VBRuntimeErrorException.TypeMismatch(symbol.SelectionRange!));

        // Symbol is null, we must still return something.
        // this return path should only be traversed in unit tests that may not need to associate a symbol with the value.
        // TODO determine if we should be throwing a hard exception here instead.
        return VBDoubleValue.Zero;
    }

    public VBBooleanValue AsCoercedBoolean(ref int depth)
    {
        if (string.Equals(Value, Tokens.True, StringComparison.InvariantCultureIgnoreCase)
            || string.Equals(Value, $"#{Tokens.True.ToUpperInvariant()}#", StringComparison.InvariantCulture))
        {
            // case-insensitive "True" or case-sensitive "#TRUE#", the result is True:
            return VBBooleanValue.True;
        }
        else if (string.Equals(Value, Tokens.False, StringComparison.InvariantCultureIgnoreCase)
            || string.Equals(Value, $"#{Tokens.False.ToUpperInvariant()}#", StringComparison.InvariantCulture))
        {
            // case-insensitive "False" or case-sensitive "#FALSE#", the result False:
            return VBBooleanValue.False;
        }
        else
        {
            // otherwise the result is let-coerced a Double value
            // which is then let-coerced to a Boolean value:
            depth++;
            return AsCoercedDouble(ref depth).AsCoercedBoolean(ref depth);
        }
    }

    public VBDateValue AsCoercedDate(ref int depth)
    {
        if (DateTime.TryParse(Value, CultureInfo, out var validDateValue))
        {
            try
            {
                // if host-defined regional settings can parse it as a date, it's a date.
                return new VBDateValue(Symbol).WithValue(validDateValue);
            }
            catch (VBRuntimeErrorException)
            {
                // EDGE CASE where validDateValue overflows the range of VBDateValue.
                // the string parsed as a date in the first place, so it won't be representable as anything else,
                // the correct VBRuntimeErrorException to be thrown here isn't the overflow we caught.
                ThrowWithSymbol(symbol => VBRuntimeErrorException.TypeMismatch(symbol.SelectionRange!));
            }
        }

        if (double.TryParse(Value, CultureInfo, out var validNumericValue))
        {
            try
            {
                // if host-defined regional settings can parse it as a numeric value (number or currency)
                // and the resulting value is within the range of VBDoubleValue, we let-coerce the double into a date.
                depth++;
                return new VBDoubleValue(Symbol).WithValue(validNumericValue).AsCoercedDate(ref depth);
            }
            catch (VBRuntimeErrorException)
            {
                // EDGE CASE where the coerced validNumericValue overflows the range of VBDateValue.
                // the correct VBRuntimeErrorException to be thrown here isn't the overflow we caught.
                ThrowWithSymbol(symbol => VBRuntimeErrorException.TypeMismatch(symbol.SelectionRange!));
            }
        }

        // ...otherwise runtime error 13 type mismatch is raised.
        ThrowWithSymbol(symbol => VBRuntimeErrorException.TypeMismatch(symbol.SelectionRange!));

        // Symbol is null, we must still return something.
        // this return path should only be traversed in unit tests that may not need to associate a symbol with the value.
        // TODO determine if we should be throwing a hard exception here instead.
        return VBDateValue.Zero;
    }
}
