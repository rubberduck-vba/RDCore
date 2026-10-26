using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
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
/// A conversion is a function of a value and a destination type — and, to <c>String</c>, of the regional settings
/// it writes in — and nothing else: it needs no expression to be evaluating, no stack to recurse on, and no symbol
/// to resolve. Let-coercion is the implicit use of it — an assignment, an operand, an argument — and the
/// explicit-conversion functions of the standard library (<c>CInt</c>, <c>CDbl</c>, …) are the other; both are
/// defined by the specification as "Let-coerced to <em>Type</em>", and both therefore get their value from here
/// rather than one being built on the other, which would make a conversion function report itself as a coercion.
/// <para>
/// All of it is pure, so it is static — the same as <c>VBNumericType.BankersRounding</c>, which it uses.
/// </para>
/// <para>
/// 👉 The regional settings are an argument rather than something read, so that a conversion stays a function of
/// what it is given: <see cref="ToText"/> takes a culture, and is invariant when it is given none. Let-coercion
/// passes the session environment's, and so does <c>CStr</c>; the functions whose text the specification fixes
/// pass the invariant culture themselves.
/// </para>
/// <para>
/// 👉 A <c>Null</c> converts to no type that cannot hold it (<strong>MS-VBAL §5.5.1.2.10</strong>), so every
/// conversion here raises error 94 for one. Let-coercion dispatches on the destination type, and so never
/// reached a rule keyed on a <c>Null</c> <em>source</em>: a <c>Null</c> assigned to a <c>Long</c> was an
/// internal error instead.
/// </para>
/// <para>
/// 👉 The same goes for an <c>Error</c> value, the other source the specification keys a rule on
/// (<strong>MS-VBAL §5.5.1.2.9</strong>): it converts to none of these types, so every conversion here raises
/// error 13 for one. The explicit-conversion functions that read an Error as its error code swap the code in
/// before they convert.
/// </para>
/// </remarks>
public static partial class ValueConversions
{
    /// <summary>
    /// Converts <paramref name="source"/> to the numeric type <paramref name="destination"/>
    /// (<strong>MS-VBAL §5.5.1.2.1</strong>), and from a <c>String</c>, <c>Boolean</c>, <c>Date</c> or
    /// <c>Empty</c> (<strong>§5.5.1.2.2</strong>, <strong>§5.5.1.2.3</strong>, <strong>§5.5.1.2.4</strong>,
    /// <strong>§5.5.1.2.11</strong>).
    /// </summary>
    /// <param name="source">The value, unwrapped from any <c>Variant</c> that held it.</param>
    /// <param name="destination">The numeric type to convert to.</param>
    /// <param name="regional">The regional settings a <c>String</c> is read by; en-US when none are given.</param>
    public static ValueConversionResult ToNumeric(VBTypedValue source, VBNumericType destination, RegionalSettings? regional = null) => source.TypeInfo switch
    {
        VBStringType => StringToNumeric(((VBStringValue)source).Value, destination, regional),

        // if the source value is within the range of the destination type, the result is a copy of the value: a whole number is converted as the whole
        // number it is, which a LongLong past 2^53 is not as a Double.
        IIntegralNumericType when WholeValueOf(source) is { } whole => FromWhole(whole, destination),

        // a Currency or a Decimal is converted as the decimal number it is, which has digits a Double does not.
        IFixedPointNumericType when FixedValueOf(source) is { } fixedPoint => FromFixed(fixedPoint, destination),

        // if the source value is finite and within the range of the destination type, the result is the value
        // converted to an integer using Banker's Rounding.
        IFloatingPointNumericType when destination is IIntegralNumericType
            => Ranged(((VBNumericTypedValue)source).AsDouble, destination)
                ? ValueConversionResult.Success(destination.CreateValue(VBNumericType.BankersRounding((VBNumericTypedValue)source)))
                : ValueConversionResult.Failure(VBRuntimeErrorId.Overflow),

        // MS-VBAL §2.4: an infinity and a NaN are values of a Single and of a Double, and one is the other's as it is (Single inf -> Double, no error;
        // recorded in Excel, grid oracle\s16 F20). Of any other type it is no value: an overflow, below.
        IFloatingPointNumericType when destination is VBSingleType or VBDoubleType && !double.IsFinite(((VBNumericTypedValue)source).AsDouble)
            => ValueConversionResult.Success(destination.CreateValue(((VBNumericTypedValue)source).AsDouble)),

        // a Single made a Decimal is its value to 7 significant digits, ties to even, as MS-VBA has it: CDec(CSng(0.1)) is 0.1, and a Single of 16777217
        // is 16777220 - where a Double keeps the 15 digits a Double's conversion does (recorded in Excel, grid oracle\s43 K01-K26, J01).
        VBSingleType when destination is VBDecimalType
            => Ranged(((VBSingleValue)source).Value, destination)
                ? ValueConversionResult.Success(new VBDecimalValue((decimal)((VBSingleValue)source).Value))
                : ValueConversionResult.Failure(VBRuntimeErrorId.Overflow),

        // Double -> Single, Single -> Currency, and so on.
        IFloatingPointNumericType
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

        // MS-VBAL 5.5.1.2.10: Null converts to no type that cannot hold it.
        VBNullType => ValueConversionResult.Failure(VBRuntimeErrorId.InvalidUseOfNull),

        // MS-VBAL 5.5.1.2.9: an Error converts to nothing but a Variant or an Error.
        VBErrorType => ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch),

        _ => ValueConversionResult.NotApplicable,
    };

    private static bool Ranged(double value, VBNumericType destination) => VBNumericType.IsWithinRange(value, destination);

    /// <summary>
    /// Converts a whole number to the numeric type <paramref name="destination"/> (<strong>MS-VBAL §5.5.1.2.1</strong>): if it is within the range of the
    /// type, the result is a copy of the value - exactly, in an integral or fixed-point type, and rounded to the nearest value a floating-point type holds.
    /// </summary>
    /// <param name="value">The whole number.</param>
    /// <param name="destination">The numeric type to convert to.</param>
    public static ValueConversionResult FromWhole(long value, VBNumericType destination) => destination switch
    {
        VBByteType => value is >= byte.MinValue and <= byte.MaxValue ? ValueConversionResult.Success(new VBByteValue((byte)value)) : Overflow,
        VBIntegerType => value is >= short.MinValue and <= short.MaxValue ? ValueConversionResult.Success(new VBIntegerValue((short)value)) : Overflow,
        VBLongType => value is >= int.MinValue and <= int.MaxValue ? ValueConversionResult.Success(new VBLongValue((int)value)) : Overflow,
        VBLongLongType => ValueConversionResult.Success(new VBLongLongValue(value)),
        IFixedPointNumericType => FromFixed(value, destination),
        _ => Ranged(value, destination) ? ValueConversionResult.Success(destination.CreateValue(value)) : Overflow,
    };

    // a decimal number to a numeric type: for an integral type, a number within its range is rounded to a whole number by Banker's rounding, which a
    // number within the range always rounds to one within it.
    private static ValueConversionResult FromFixed(decimal value, VBNumericType destination) => destination switch
    {
        IIntegralNumericType when WholeRangeOf(destination) is var (lowest, highest)
            => value >= lowest && value <= highest ? FromWhole((long)Math.Round(value, MidpointRounding.ToEven), destination) : Overflow,
        VBCurrencyType => value >= CurrencyMinValue && value <= CurrencyMaxValue ? ValueConversionResult.Success(new VBCurrencyValue(value)) : Overflow,
        VBDecimalType => ValueConversionResult.Success(new VBDecimalValue(value)),
        _ => Ranged((double)value, destination) ? ValueConversionResult.Success(destination.CreateValue((double)value)) : Overflow,
    };

    private static ValueConversionResult Overflow => ValueConversionResult.Failure(VBRuntimeErrorId.Overflow);

    // the smallest and largest whole numbers of an integral type.
    private static (long Lowest, long Highest) WholeRangeOf(VBNumericType integral) => integral switch
    {
        VBByteType => (byte.MinValue, byte.MaxValue),
        VBIntegerType => (short.MinValue, short.MaxValue),
        VBLongType => (int.MinValue, int.MaxValue),
        _ => (long.MinValue, long.MaxValue),
    };

    private static decimal CurrencyMinValue => VBRuntimeValue<VBRuntimeCurrencyValue>.CurrencyMinValue.Value.Value;
    private static decimal CurrencyMaxValue => VBRuntimeValue<VBRuntimeCurrencyValue>.CurrencyMaxValue.Value.Value;

    /// <summary>
    /// The whole number an integral value (a Byte, an Integer, a Long or a LongLong) is, exactly; <see langword="null"/> for any other value.
    /// </summary>
    /// <param name="value">The value.</param>
    public static long? WholeValueOf(VBTypedValue value) => value switch
    {
        VBByteValue byteValue => byteValue.Value,
        VBIntegerValue integer => integer.Value,
        VBLongValue longValue => longValue.Value,
        VBLongLongValue longLong => longLong.Value,
        _ => null,
    };

    // the decimal number a fixed-point value is; null for one that is not one of the fixed-point types.
    internal static decimal? FixedValueOf(VBTypedValue value) => value switch
    {
        VBCurrencyValue currency => currency.Value.Value,
        VBDecimalValue decimalValue => decimalValue.Value,
        _ => null,
    };

    // MS-VBAL 5.5.1.2.4, as MS-VBA reads it (NumberGrammar).
    private static ValueConversionResult StringToNumeric(string? text, VBNumericType destination, RegionalSettings? regional = null)
    {
        if (!NumberGrammar.TryRead(text ?? string.Empty, regional ?? RegionalSettings.Default, out var reading))
        {
            return ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch);
        }

        if (reading.Radix is { } bits)
        {
            return FromRadix(bits, destination);
        }

        if (!double.TryParse(reading.Digits, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var interpretedValue))
        {
            return ValueConversionResult.Failure(VBRuntimeErrorId.TypeMismatch);
        }

        // a number written without an exponent is the decimal number it is written as, when a Decimal holds it: "9223372036854775807" is the largest
        // LongLong, which no Double is.
        if (reading.Exponent is null
            && decimal.TryParse(reading.Digits, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var writtenValue))
        {
            return FromFixed(writtenValue, destination);
        }

        var scaledValue = reading.Exponent is { } exponent ? interpretedValue * Math.Pow(10, exponent) : interpretedValue;
        return !double.IsNaN(scaledValue) && Ranged(scaledValue, destination)
            ? ValueConversionResult.Success(destination.CreateValue(scaledValue))
            : ValueConversionResult.Failure(VBRuntimeErrorId.Overflow);
    }

    // the digits of an &H or &O literal, as MS-VBA converts them (recorded in Excel): to an Integer, up to 16 bits are the Integer they are (&HFFFF is -1)
    // and more are the low 32 bits read as a Long, which may not fit; to a Long, up to 32 bits are the Long they are, and more overflow; to any other
    // type, up to 32 bits are the Long they are (&HFFFF is 65535, &HFFFFFFFF is -1), and more are the number they are.
    private static ValueConversionResult FromRadix(ulong bits, VBNumericType destination) => destination switch
    {
        VBIntegerType or VBByteType => FromWhole(bits <= ushort.MaxValue ? (short)(ushort)bits : (int)(uint)(bits & uint.MaxValue), destination),
        VBLongType => bits <= uint.MaxValue ? FromWhole((int)(uint)bits, destination) : Overflow,
        _ when bits <= uint.MaxValue => FromWhole((int)(uint)bits, destination),
        _ => bits <= long.MaxValue ? FromWhole((long)bits, destination) : FromFixed(bits, destination),
    };
}
