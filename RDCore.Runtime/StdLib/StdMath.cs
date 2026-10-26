using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Numerics;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdMathModule"/>
/// <remarks>
/// The specification states little of these beyond what each one computes, and leaves <c>Rnd</c> and <c>Randomize</c> implementation-defined; what it
/// leaves open is what MS-VBA does, measured in Excel (64-bit, en-US) and tabulated in RD-VBAL §6.1.2.10:
/// <list type="bullet">
/// <item><see cref="Abs"/> and <see cref="Round"/> answer in the type they were given, a <c>Boolean</c> as an <c>Integer</c>; <see cref="Sgn"/> answers an <c>Integer</c>.</item>
/// <item><see cref="Round"/> rounds a tie to even, as it is <em>after</em> scaling to the digits asked for: <c>Round(2.345, 2)</c> is <c>2.35</c>, because
/// <c>2.345 * 100</c> is a hair over <c>234.5</c> as a <c>Double</c>.</item>
/// <item><see cref="Rnd"/> and <see cref="Randomize"/> are MS-VBA's generator (<see cref="ISessionRandomNumberGenerator"/>), so that a seeded sequence is the
/// one MS-VBA generates.</item>
/// </list>
/// <para>
/// 👉 <see cref="Sin"/>, <see cref="Cos"/> and <see cref="Tan"/> refuse an angle of 2<sup>63</sup> radians or more, as MS-VBA does. A sine and a cosine
/// are the platform's, within a unit of the last place of the true value as MS-VBA's are, and its to the last bit but for about one argument in forty;
/// a tangent is MS-VBA's quotient of the two, divided in 80 bits - not the platform's own tangent, which differs in the last bit for one in three
/// (recorded in Excel, grid oracle\s43 M01-M03, T: 80 arguments).
/// </para>
/// <para>
/// An object argument stands for its simple data value (<strong>MS-VBAL §5.5.1.2.13</strong>), read through its default member
/// (<see cref="ObjectDefaultMember.ValueOf"/>): <c>Abs</c> of an object whose default member returns <c>Null</c> is <c>Null</c>, as in MS-VBA.
/// </para>
/// </remarks>
/// <param name="session">The session whose random-number generator <see cref="Rnd"/> and <see cref="Randomize"/> use, and whose clock seeds it.</param>
public sealed class StdMath(IRuntimeSession session) : IStdMathModule
{
    // MS-VBA's generator, measured: each number is the previous one times 0x43FD43FD plus 0xC39EC3, modulo 2^24, and Rnd answers it as a fraction of 2^24.
    private const long Multiplier = 0x43FD43FD;
    private const long Increment = 0xC39EC3;
    private const int SeedMask = 0xFFFFFF;
    private const float SeedRange = 0x1000000;

    // what Randomize replaces of the seed: the upper two of its three bytes.
    private const int RandomizedBits = 0xFFFF00;

    // the most digits MS-VBA rounds a Double to; a Currency or a Decimal it rounds to any number of digits, which is all the digits it has.
    private const int MostDoubleDigits = 22;
    private const int MostCurrencyDigits = 4;
    private const int MostDecimalDigits = 28;

    // MS-VBA refuses an angle of this many radians or more (error 5); 9.2E+18 is an angle, and 1E+19 is not.
    private const double LargestAngle = 9223372036854775808d;

    #region 6.1.2.10.1 Public Functions

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Abs(VBVariantValue Number)
    {
        if (!TryRead(Number, out var source, out var unreadable))
        {
            return RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(unreadable!);
        }

        switch (source)
        {
            // "If Number is the data value Null, returns Null."
            case VBNullValue:
                return Variant(VBNullValue.Null);

            // "If Number is the data value Empty, returns the Integer data value 0."
            case VBEmptyValue:
                return Variant(new VBIntegerValue(0));

            // MS-VBA answers a Boolean as the Integer it Let-coerces to: Abs(True) is 1.
            case VBBooleanValue boolean:
                return Variant(new VBIntegerValue((short)((bool)boolean.Value ? 1 : 0)));

            // "If Number is of a numeric value type, returns a value of the same value type": so the absolute value of the smallest value of a type is an
            // overflow, there being no value of that type that is its absolute value - except for a Long: MS-VBA answers Abs(CLng(-2147483648)) with that
            // same negative Long, as a 32-bit negation wraps, and RD-VBA does what MS-VBA does.
            case VBLongValue { Value: int.MinValue }:
                return Variant(source);

            case VBNumericTypedValue when source.TypeInfo is IIntegralNumericType && ValueConversions.WholeValueOf(source) is { } whole:
                return whole == long.MinValue
                    ? Fail<VBVariantValue>(VBRuntimeErrorId.Overflow, $"The smallest {source.TypeInfo.Name} has no absolute value of its own type.")
                    : InVariant(ValueConversions.FromWhole(Math.Abs(whole), (VBNumericType)source.TypeInfo), source);

            case VBCurrencyValue currency:
                return InVariant(ValueConversions.ToNumeric(new VBDecimalValue(Math.Abs(CurrencyValueOf(currency))), VBCurrencyType.TypeInfo), source);

            case VBDecimalValue @decimal:
                return Variant(new VBDecimalValue(Math.Abs(@decimal.Value)));

            case VBSingleValue single:
                return Variant(new VBSingleValue(Math.Abs(single.Value)));

            case VBDoubleValue @double:
                return Variant(new VBDoubleValue(Math.Abs(@double.Value)));

            // a Date is of no numeric value type, but MS-VBA answers it as one: Abs(#12/29/1899#) is #12/31/1899#.
            case VBDateValue date:
                return Variant(new VBDateValue(Math.Abs(date.SerialValue)));

            // "Otherwise, the data value of Number is Let-coerced to Double and the absolute value of that data value is returned."
            default:
                return ToDouble(source, out var value, out var error)
                    ? Variant(new VBDoubleValue(Math.Abs(value)))
                    : error;
        }
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> Atn(VBDoubleValue Number)
        => Double(Math.Atan(Number.Value));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> Cos(VBDoubleValue Number)
        => Angle(Number.Value, Math.Cos);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> Exp(VBDoubleValue Number)
    {
        // "If the value of Number exceeds 709.782712893, an error occurs": the result would exceed the largest Double, which is an overflow.
        var result = Math.Exp(Number.Value);
        return double.IsInfinity(result)
            ? Fail<VBDoubleValue>(VBRuntimeErrorId.Overflow, $"e raised to {Number.Value} is greater than the largest Double.")
            : Double(result);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> Log(VBDoubleValue Number)
        // "Double containing any valid numeric expression greater than zero."
        => Number.Value > 0
            ? Double(Math.Log(Number.Value))
            : Fail<VBDoubleValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, "Only a number greater than zero has a logarithm.");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBSingleValue> Rnd(VBVariantValue? Number = default)
    {
        // "Not supplied: The next random number in the sequence." - as for any number greater than zero.
        var number = 1f;
        if (Number is not null)
        {
            if (!TryRead(Number, out var value, out var unreadable))
            {
                return RuntimeSemanticsEvaluationResult<VBSingleValue>.Error(unreadable!);
            }

            var converted = value is VBMissingValue
                ? ValueConversionResult.Success(new VBSingleValue(number))
                : ValueConversions.ToNumeric(value, VBSingleType.TypeInfo, session.Environment.Regional);
            if (converted is not { IsSuccess: true, Value: VBSingleValue single })
            {
                return Fail<VBSingleValue>(converted.Error ?? VBRuntimeErrorId.TypeMismatch, "Rnd takes a number that is a Single.");
            }

            number = single.Value;
        }

        var generator = session.RandomNumbers;
        if (number < 0)
        {
            // "Less than zero: The same number every time, using Number as the seed." MS-VBA seeds with the bits of the Single, folded into 24.
            var bits = BitConverter.SingleToUInt32Bits(number);
            generator.Seed = (int)((bits + (bits >> 24)) & SeedMask);
        }

        // "Equal to zero: The most recently generated number." - which is the seed: each number is generated from the one before it.
        if (number != 0)
        {
            generator.Seed = (int)((generator.Seed * Multiplier + Increment) & SeedMask);
        }

        return RuntimeSemanticsEvaluationResult<VBSingleValue>.Success(new VBSingleValue(generator.Seed / SeedRange));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Round(VBVariantValue Number, VBLongValue? NumDigitsAfterDecimal = default)
    {
        // "If omitted, integers are returned." MS-VBA refuses a negative number of digits before it looks at the number, Null included.
        var digits = NumDigitsAfterDecimal?.Value ?? 0;
        if (digits < 0)
        {
            return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, "A number cannot be rounded to fewer than no digits.");
        }

        if (!TryRead(Number, out var source, out var unreadable))
        {
            return RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(unreadable!);
        }

        switch (source)
        {
            case VBNullValue:
                return Variant(VBNullValue.Null);

            case VBEmptyValue:
                return Variant(new VBIntegerValue(0));

            case VBBooleanValue boolean:
                return Variant(new VBIntegerValue((short)((bool)boolean.Value ? -1 : 0)));

            // a whole number has no digits after the decimal to round.
            case VBNumericTypedValue when source.TypeInfo is IIntegralNumericType:
                return Variant(source);

            // rounding a Currency up can take it past the largest Currency.
            case VBCurrencyValue currency:
                return InVariant(ValueConversions.ToNumeric(
                    new VBDecimalValue(Math.Round(CurrencyValueOf(currency), Math.Min(digits, MostCurrencyDigits), MidpointRounding.ToEven)),
                    VBCurrencyType.TypeInfo), source);

            case VBDecimalValue @decimal:
                return Variant(new VBDecimalValue(Math.Round(@decimal.Value, Math.Min(digits, MostDecimalDigits), MidpointRounding.ToEven)));

            // MS-VBA rounds a Single as a Double, and answers the Single that is.
            case VBSingleValue single:
                return RoundDouble(single.Value, digits, out var rounded, out var error)
                    ? Variant(new VBSingleValue((float)rounded))
                    : error;

            case VBDoubleValue @double:
                return RoundDouble(@double.Value, digits, out rounded, out error)
                    ? Variant(new VBDoubleValue(rounded))
                    : error;

            // a Date rounds as its serial value: Round(#1/1/1900 6:00 PM#, 1) is 7:12 PM, the .8 of a day.
            case VBDateValue date:
                return RoundDouble(date.SerialValue, digits, out rounded, out error)
                    ? Variant(new VBDateValue(rounded))
                    : error;

            default:
                if (!ToDouble(source, out var value, out error))
                {
                    return error;
                }

                return RoundDouble(value, digits, out rounded, out error)
                    ? Variant(new VBDoubleValue(rounded))
                    : error;
        }
    }

    // MS-VBA scales the number to the digits it keeps, rounds that to a whole number, and scales it back - so a tie is a tie as the scaled Double is, not as
    // the decimal digits it was written with are: 2.675 is a hair under 2.675 as a Double, and 267.5 once scaled.
    private static bool RoundDouble(double value, int digits, out double rounded, out RuntimeSemanticsEvaluationResult<VBVariantValue> error)
    {
        rounded = value;
        error = default!;
        if (digits > MostDoubleDigits)
        {
            error = Fail<VBVariantValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, $"A Double rounds to no more than {MostDoubleDigits} digits.");
            return false;
        }

        var scale = Math.Pow(10, digits);
        var scaled = value * scale;
        if (double.IsInfinity(scaled))
        {
            // a number that large has no digits after the decimal to round.
            return true;
        }

        // a tie goes to the even one of its whole part and the next one away from zero, keeping its sign (Round(-0.5) is -0, as in MS-VBA); anything else
        // goes to its nearest whole number, which is never a negative zero (Round(-0.4) is 0).
        var whole = Math.Truncate(scaled);
        var fraction = scaled - whole;
        rounded = Math.Abs(fraction) == 0.5
            ? (whole % 2 == 0 ? whole : whole + Math.Sign(fraction))
            : Math.Round(scaled) + 0d;

        rounded /= scale;
        return true;
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Sgn(VBVariantValue Number)
    {
        if (!TryRead(Number, out var source, out var unreadable))
        {
            return RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(unreadable!);
        }

        int sign;
        switch (source)
        {
            // the specification's parameter is a Double, and Null is no Double.
            case VBNullValue:
                return Fail<VBVariantValue>(VBRuntimeErrorId.InvalidUseOfNull, "Null has no sign.");

            case VBEmptyValue:
                sign = 0;
                break;

            case VBBooleanValue boolean:
                sign = (bool)boolean.Value ? -1 : 0;
                break;

            case VBNumericTypedValue when source.TypeInfo is IIntegralNumericType && ValueConversions.WholeValueOf(source) is { } whole:
                sign = Math.Sign(whole);
                break;

            case VBCurrencyValue currency:
                sign = Math.Sign(CurrencyValueOf(currency));
                break;

            case VBDecimalValue @decimal:
                sign = Math.Sign(@decimal.Value);
                break;

            // a NaN has no sign: an overflow, as MS-VBA has it (recorded in Excel, grid oracle\s16 G10); an infinity's is its own (H05).
            case VBNumericTypedValue numeric when double.IsNaN(numeric.AsDouble):
                return Fail<VBVariantValue>(VBRuntimeErrorId.Overflow, Exceptions.VBSgn_NaN_Verbose);

            case VBNumericTypedValue numeric:
                sign = Math.Sign(numeric.AsDouble);
                break;

            case VBDateValue date:
                sign = Math.Sign(date.SerialValue);
                break;

            default:
                if (!ToDouble(source, out var value, out var error))
                {
                    return error;
                }

                sign = Math.Sign(value);
                break;
        }

        // "Returns an Integer indicating the sign of a number."
        return Variant(new VBIntegerValue((short)sign));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> Sin(VBDoubleValue Number)
        => Angle(Number.Value, Math.Sin);

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> Sqr(VBDoubleValue Number)
        // "Double containing any valid numeric expression greater than zero" - and zero, whose square root is zero.
        => Number.Value >= 0
            ? Double(Math.Sqrt(Number.Value))
            : Fail<VBDoubleValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, "A negative number has no square root.");

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBDoubleValue> Tan(VBDoubleValue Number)
        => Angle(Number.Value, x => Float80.Divide(Float80.FromDouble(Math.Sin(x)), Float80.FromDouble(Math.Cos(x)), out _).ToDouble(out _));

    private static RuntimeSemanticsEvaluationResult<VBDoubleValue> Angle(double radians, Func<double, double> function)
        => Math.Abs(radians) < LargestAngle
            ? Double(function(radians))
            : Fail<VBDoubleValue>(VBRuntimeErrorId.InvalidProcedureCallOrArgument, $"An angle is less than {LargestAngle:R} radians.");

    #endregion

    #region 6.1.2.10.2 Public Subroutines

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Randomize(VBVariantValue? Number = default)
    {
        double seed;
        VBTypedValue value = VBMissingValue.Missing;
        if (Number is not null && !TryRead(Number, out value, out var unreadable))
        {
            return RuntimeSemanticsEvaluationResult.Error(unreadable!);
        }

        if (value is VBMissingValue)
        {
            // "If the argument is missing or Empty, the value returned by the system timer is used as the new seed value." MS-VBA uses the timer only when
            // the argument is missing: an Empty is Let-coerced like any other argument, to 0.
            seed = (float)session.Clock.Now.TimeOfDay.TotalSeconds;
        }
        else if (!ToDouble(value, out seed, out var error))
        {
            return RuntimeSemanticsEvaluationResult.Error(error.ErrorInfo!);
        }

        // MS-VBA folds the high half of the Double into the upper two bytes of the seed, and keeps its low byte: the sequence after a Randomize depends on
        // where the generator was, unless an Rnd with a negative number put it somewhere first.
        var high = (uint)(BitConverter.DoubleToUInt64Bits(seed) >> 32);
        var folded = (int)((high ^ (high >> 16)) & 0xFFFF) << 8;
        session.RandomNumbers.Seed = (session.RandomNumbers.Seed & SeedMask & ~RandomizedBits) | folded;
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    #endregion

    private bool ToDouble(VBTypedValue source, out double value, out RuntimeSemanticsEvaluationResult<VBVariantValue> error)
    {
        value = 0;
        error = default!;
        var converted = ValueConversions.ToNumeric(source, VBDoubleType.TypeInfo, session.Environment.Regional);
        if (converted is { IsSuccess: true, Value: VBDoubleValue @double })
        {
            value = @double.Value;
            return true;
        }

        // a pair of types with no conversion between them is what a Let-coercion reports as a type mismatch.
        error = Fail<VBVariantValue>(converted.Error ?? VBRuntimeErrorId.TypeMismatch, $"A value of type {source.TypeInfo.Name} cannot be converted to Double.");
        return false;
    }

    private static decimal CurrencyValueOf(VBCurrencyValue currency) => Convert.ToDecimal(currency.RuntimeValue.BoxedValue);

    // the value of an argument, which an object stands for its simple data value as (MS-VBAL 5.5.1.2.13): what its default member returns.
    private bool TryRead(VBVariantValue argument, out VBTypedValue value, out IVBRaisableError? error)
    {
        var read = ObjectDefaultMember.ValueOf(session, argument);
        value = read.Result ?? argument;
        error = read.ErrorInfo;
        return read.IsSuccess;
    }

    private static RuntimeSemanticsEvaluationResult<VBVariantValue> InVariant(ValueConversionResult converted, VBTypedValue source)
        => converted is { IsSuccess: true, Value: { } value }
            ? Variant(value)
            : Fail<VBVariantValue>(converted.Error ?? VBRuntimeErrorId.Overflow, $"The result is not a {source.TypeInfo.Name}.");

    private static RuntimeSemanticsEvaluationResult<VBVariantValue> Variant(VBTypedValue value)
        => RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(new VBVariantValue(value));

    private static RuntimeSemanticsEvaluationResult<VBDoubleValue> Double(double value)
        => RuntimeSemanticsEvaluationResult<VBDoubleValue>.Success(new VBDoubleValue(value));

    private static RuntimeSemanticsEvaluationResult<TValue> Fail<TValue>(VBRuntimeErrorId id, string verbose)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(VBRuntimeErrorInfo.For(id, default, verbose));
}
