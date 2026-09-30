using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RDCore.Runtime.Semantics.Conversion;

/// <summary>
/// The outcome of converting one value to another type: the converted value, the run-time error the
/// conversion raises, or the fact that the two types have no conversion between them.
/// </summary>
/// <remarks>
/// It names the error by its id rather than building it, because building one takes what a conversion does not
/// have: where it happened, and the verbose message a particular caller wants to give. Each caller — the
/// Let-coercion of an assignment, an explicit-conversion function — attaches its own.
/// </remarks>
/// <param name="Value">The converted value, when the conversion succeeded.</param>
/// <param name="Error">The run-time error the conversion raises, when it failed.</param>
/// <param name="IsApplicable"><see langword="false"/> when the types have no conversion between them.</param>
public readonly record struct ValueConversionResult(VBTypedValue? Value, VBRuntimeErrorId? Error, bool IsApplicable = true)
{
    /// <summary>Whether the conversion produced <see cref="Value"/>.</summary>
    public bool IsSuccess => IsApplicable && Error is null && Value is not null;

    /// <summary>A conversion that succeeded.</summary>
    public static ValueConversionResult Success(VBTypedValue value) => new(value, null);

    /// <summary>A conversion that raises <paramref name="error"/>.</summary>
    public static ValueConversionResult Failure(VBRuntimeErrorId error) => new(null, error);

    /// <summary>A pair of types with no conversion between them.</summary>
    public static ValueConversionResult NotApplicable => new(null, null, false);
}

/// <summary>
/// The value-to-value conversions <strong>MS-VBAL §5.5.1.2</strong> defines between the intrinsic types: what
/// a value of one type <em>is</em>, expressed in another.
/// </summary>
/// <remarks>
/// A conversion is a function of a value and a destination type, and nothing else: it needs no expression to
/// be evaluating, no stack to recurse on, and no symbol to resolve. Let-coercion is the implicit use of it — an
/// assignment, an operand, an argument — and the explicit-conversion functions of the standard library
/// (<c>CInt</c>, <c>CDbl</c>, …) are the other; both are defined by the specification as "Let-coerced to
/// <em>Type</em>", and both therefore get their value from here rather than one being built on the other, which
/// would make a conversion function report itself as a coercion.
/// <para>
/// All of it is pure, so it is static — the same as <c>VBNumericType.BankersRounding</c>, which it uses.
/// </para>
/// <para>
/// 🚧 Only the conversions to a numeric type are here. TODO the conversions to <c>String</c>,
/// <c>Boolean</c> and <c>Date</c>, which still live in their let-coercion strategies.
/// </para>
/// </remarks>
public static partial class ValueConversions
{
    // MS-VBAL 5.5.1.2.4: numeric-coercion-string = [WS] [sign [WS]] regional-number-string
    // [exponent-clause] [WS]; exponent-clause = ("e" / "d") [sign] integer-literal. Whitespace is
    // also tolerated immediately around the sign and the exponent letter.
    [GeneratedRegex(@"^\s*(?<mantissa>[+-]?\s*(?:[0-9]+\.?[0-9]*|\.[0-9]+))\s*(?:[eEdD]\s*(?<exponent>[+-]?\s*[0-9]+))?\s*$")]
    private static partial Regex NumericCoercionStringPattern();

    /// <summary>
    /// Converts <paramref name="source"/> to the numeric type <paramref name="destination"/>
    /// (<strong>MS-VBAL §5.5.1.2.1</strong>), and from a <c>String</c>, <c>Boolean</c>, <c>Date</c> or
    /// <c>Empty</c> (<strong>§5.5.1.2.2</strong>, <strong>§5.5.1.2.3</strong>, <strong>§5.5.1.2.4</strong>,
    /// <strong>§5.5.1.2.11</strong>).
    /// </summary>
    /// <param name="source">The value, unwrapped from any <c>Variant</c> that held it.</param>
    /// <param name="destination">The numeric type to convert to.</param>
    public static ValueConversionResult ToNumeric(VBTypedValue source, VBNumericType destination) => source.TypeInfo switch
    {
        VBStringType => StringToNumeric(((VBStringValue)source).Value, destination),

        // if the source value is within the range of the destination type, the result is a copy of the value.
        IIntegralNumericType => Ranged(((VBNumericTypedValue)source).AsDouble, destination)
            ? ValueConversionResult.Success(destination.CreateValue(((VBNumericTypedValue)source).AsDouble))
            : ValueConversionResult.Failure(VBRuntimeErrorId.Overflow),

        // if the source value is finite and within the range of the destination type, the result is the value
        // converted to an integer using Banker's Rounding.
        IFloatingPointNumericType or IFixedPointNumericType when destination is IIntegralNumericType
            => Ranged(((VBNumericTypedValue)source).AsDouble, destination)
                ? ValueConversionResult.Success(destination.CreateValue(VBNumericType.BankersRounding((VBNumericTypedValue)source)))
                : ValueConversionResult.Failure(VBRuntimeErrorId.Overflow),

        // Double -> Single, Currency -> Decimal, and so on.
        IFloatingPointNumericType or IFixedPointNumericType
            => Ranged(((VBNumericTypedValue)source).AsDouble, destination)
                ? ValueConversionResult.Success(destination.CreateValue(((VBNumericTypedValue)source).AsDouble))
                : ValueConversionResult.Failure(VBRuntimeErrorId.Overflow),

        // MS-VBAL 5.5.1.2.2: Byte is the one destination-type exception: True -> 255, not -1.
        VBBooleanType when destination is VBByteType
            => ValueConversionResult.Success(new VBByteValue((byte)((bool)((VBBooleanValue)source).Value ? 255 : 0))),

        VBBooleanType => ValueConversionResult.Success(destination.CreateValue((bool)((VBBooleanValue)source).Value ? -1d : 0d)),

        // MS-VBAL 5.5.1.2.3: a Date source converts via its standard Double (serial value) representation.
        VBDateType => Ranged(((VBDateValue)source).SerialValue, destination)
            ? ValueConversionResult.Success(destination.CreateValue(((VBDateValue)source).SerialValue))
            : ValueConversionResult.Failure(VBRuntimeErrorId.Overflow),

        // MS-VBAL 5.5.1.2.11: "The result is 0."
        VBEmptyType => ValueConversionResult.Success(destination.CreateValue(0d)),

        _ => ValueConversionResult.NotApplicable,
    };

    private static bool Ranged(double value, VBNumericType destination) => VBNumericType.IsWithinRange(value, destination);

    private static ValueConversionResult StringToNumeric(string? text, VBNumericType destination)
    {
        var match = NumericCoercionStringPattern().Match(text ?? string.Empty);
        if (!match.Success)
        {
            return ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch);
        }

        if (!double.TryParse(
            match.Groups["mantissa"].Value.Replace(" ", string.Empty),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var interpretedValue))
        {
            return ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch);
        }

        var scaledValue = interpretedValue;
        if (match.Groups["exponent"].Success)
        {
            if (!int.TryParse(
                match.Groups["exponent"].Value.Replace(" ", string.Empty),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var exponent))
            {
                return ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch);
            }

            scaledValue *= Math.Pow(10, exponent);
        }

        return !double.IsNaN(scaledValue) && Ranged(scaledValue, destination)
            ? ValueConversionResult.Success(destination.CreateValue(scaledValue))
            : ValueConversionResult.Failure(VBRuntimeErrorId.Overflow);
    }
}
