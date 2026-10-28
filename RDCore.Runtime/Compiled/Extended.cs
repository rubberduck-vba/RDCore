using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Numerics;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// The value of a <c>Single</c>, <c>Double</c> or <c>Date</c> operation as MS-VBA computes it, in the 80-bit extended format, before it is stored
/// (<strong>MS-VBAL §5.6.9.3</strong>): what compiled code computes an extended intermediate in, as the interpreter does (its <c>ExtendedIntermediates</c>).
/// </summary>
/// <remarks>
/// <para>
/// A value that is a <c>Double</c> exactly - which every operand of a <c>Single</c>, <c>Double</c> or <c>Date</c> operation is, and every integer up to
/// 2<sup>53</sup> - is kept as one, and an operation of two of them is computed in <c>Double</c> when its result is exact: the extended format would compute
/// the same value, which rounds to itself where it is stored. An operation whose result is not exact, or is past the range or the precision a
/// <c>Double</c> holds exactly, is computed in the extended format (<see cref="Float80"/>), and so is everything computed from it.
/// </para>
/// <para>
/// The exceptions the operations signal are pending until the value is stored, as they are of an x87's: storing it then raises the error they are.
/// </para>
/// </remarks>
public readonly struct Extended
{
    // 2^-968: past this, the error of a product or the remainder of a quotient may be no Double, and an exact result is not told from an inexact one.
    private const double LeastCheckedMagnitude = 4.0083367200179456e-292;

    // 2^53: every integer of this magnitude or less is a Double.
    private const long GreatestExactInteger = 1L << 53;

    private readonly double _double;
    private readonly Float80 _value;
    private readonly bool _isDouble;
    private readonly FloatingPointExceptions _pending;

    private Extended(double value, FloatingPointExceptions pending)
    {
        _double = value;
        _isDouble = true;
        _pending = pending;
    }

    private Extended(Float80 value, FloatingPointExceptions pending)
    {
        _value = value;
        _pending = pending;
    }

    // the value, in the extended format.
    private Float80 Wide => _isDouble ? Float80.FromDouble(_double) : _value;

    /// <summary>
    /// The value of a payload of an integral kind, exactly: a <c>Currency</c> is its ten-thousandths divided by 10,000, rounded once.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Extended OfIntegral(ScalarKind kind, long value)
        => kind is not ScalarKind.Currency && value is >= -GreatestExactInteger and <= GreatestExactInteger
            ? new Extended(value, FloatingPointExceptions.None)
            : OfIntegralWide(kind, value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Extended OfIntegralWide(ScalarKind kind, long value)
        => new(kind is ScalarKind.Currency ? Float80.FromCurrency(value) : Float80.FromInt64(value), FloatingPointExceptions.None);

    /// <summary>The value of a payload of a floating-point kind, exactly.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Extended OfFloating(double value)
        => double.IsFinite(value) ? new Extended(value, FloatingPointExceptions.None) : new Extended(Float80.FromDouble(value), FloatingPointExceptions.None);

    /// <summary><c>left + right</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Extended Add(Extended left, Extended right)
    {
        if (left._isDouble && right._isDouble)
        {
            // Knuth's two-sum: the error of the rounded sum, which is exact when it is zero.
            var sum = left._double + right._double;
            var virtualRight = sum - left._double;
            var error = (left._double - (sum - virtualRight)) + (right._double - virtualRight);
            if (error == 0 && double.IsFinite(sum))
            {
                return new Extended(sum, left._pending | right._pending);
            }
        }

        return AddWide(left, right);
    }

    /// <summary><c>left - right</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Extended Subtract(Extended left, Extended right)
    {
        if (left._isDouble && right._isDouble)
        {
            // the two-sum of left and -right, which is the difference exactly.
            var negated = -right._double;
            var difference = left._double + negated;
            var virtualRight = difference - left._double;
            var error = (left._double - (difference - virtualRight)) + (negated - virtualRight);
            if (error == 0 && double.IsFinite(difference))
            {
                return new Extended(difference, left._pending | right._pending);
            }
        }

        return SubtractWide(left, right);
    }

    /// <summary><c>left * right</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Extended Multiply(Extended left, Extended right)
    {
        if (left._isDouble && right._isDouble)
        {
            var product = left._double * right._double;
            if (product == 0 ? left._double == 0 || right._double == 0
                : double.IsFinite(product) && Math.Abs(product) >= LeastCheckedMagnitude && Math.FusedMultiplyAdd(left._double, right._double, -product) == 0)
            {
                return new Extended(product, left._pending | right._pending);
            }
        }

        return MultiplyWide(left, right);
    }

    /// <summary><c>left / right</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Extended Divide(Extended left, Extended right)
    {
        if (left._isDouble && right._isDouble && right._double != 0)
        {
            var quotient = left._double / right._double;
            if (quotient == 0 ? left._double == 0
                : double.IsFinite(quotient) && Math.Abs(quotient) >= LeastCheckedMagnitude && Math.Abs(left._double) >= LeastCheckedMagnitude
                    && Math.FusedMultiplyAdd(-quotient, right._double, left._double) == 0)
            {
                return new Extended(quotient, left._pending | right._pending);
            }
        }

        return DivideWide(left, right);
    }

    /// <summary>
    /// <c>left + right</c> of two values that are <c>Double</c>s exactly, when the sum is exact and finite: what the extended format computes, and stores as a
    /// <c>Double</c> as it is. <see langword="false"/> when it is not, and the sum is to be computed in the extended format (<see cref="Add"/>).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryAddExactly(double left, double right, out double sum)
    {
        sum = left + right;
        var virtualRight = sum - left;
        return (left - (sum - virtualRight)) + (right - virtualRight) == 0 && double.IsFinite(sum);
    }

    /// <summary><c>left - right</c>, as <see cref="TryAddExactly"/> computes a sum.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TrySubtractExactly(double left, double right, out double difference) => TryAddExactly(left, -right, out difference);

    /// <summary><c>left * right</c>, as <see cref="TryAddExactly"/> computes a sum: the product exact, finite, and of a magnitude its error is a <c>Double</c> at.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryMultiplyExactly(double left, double right, out double product)
    {
        product = left * right;
        return product == 0 ? left == 0 || right == 0
            : double.IsFinite(product) && Math.Abs(product) >= LeastCheckedMagnitude && Math.FusedMultiplyAdd(left, right, -product) == 0;
    }

    /// <summary><c>left / right</c>, as <see cref="TryAddExactly"/> computes a sum: the quotient exact, finite, and of a magnitude its remainder is a <c>Double</c> at.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryDivideExactly(double left, double right, out double quotient)
    {
        quotient = right == 0 ? 0 : left / right;
        return right != 0 && (quotient == 0 ? left == 0
            : double.IsFinite(quotient) && Math.Abs(quotient) >= LeastCheckedMagnitude && Math.Abs(left) >= LeastCheckedMagnitude
                && Math.FusedMultiplyAdd(-quotient, right, left) == 0);
    }

    /// <summary><c>-operand</c>, which signals nothing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Extended Negate(Extended operand)
        => operand._isDouble ? new Extended(-operand._double, operand._pending) : new Extended(operand._value.Negate(), operand._pending);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Extended AddWide(Extended left, Extended right)
        => new(Float80.Add(left.Wide, right.Wide, out var raised), left._pending | right._pending | raised);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Extended SubtractWide(Extended left, Extended right)
        => new(Float80.Subtract(left.Wide, right.Wide, out var raised), left._pending | right._pending | raised);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Extended MultiplyWide(Extended left, Extended right)
        => new(Float80.Multiply(left.Wide, right.Wide, out var raised), left._pending | right._pending | raised);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Extended DivideWide(Extended left, Extended right)
        => new(Float80.Divide(left.Wide, right.Wide, out var raised), left._pending | right._pending | raised);

    /// <summary>
    /// The value converted to a floating-point kind - stored as it, when that is the type of its expression: rounded to a <c>Double</c> or a
    /// <c>Single</c>, or a <c>Date</c>'s serial. What is pending, or the rounding signals, is the error it raises.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PayloadStatus ToFloating(Extended value, ScalarKind kind, out double result)
    {
        if (value._isDouble && value._pending is FloatingPointExceptions.None)
        {
            result = value._double;
            switch (kind)
            {
                case ScalarKind.Double:
                    return PayloadStatus.Ok;
                case ScalarKind.Date when VBDateType.IsValidSerial(result):
                    return PayloadStatus.Ok;
                case ScalarKind.Single when IsSingleExact(result):
                    result = (float)result;
                    return PayloadStatus.Ok;
            }
        }

        return Converted(value, kind, out _, out result);
    }

    /// <summary>
    /// The value converted to an integral kind (<strong>MS-VBAL §5.5.1.2.1</strong>): rounded to the nearest whole number, ties to even, and a
    /// <c>Currency</c>'s ten-thousandths so. What is no number, or is past the range of the kind, is an overflow.
    /// </summary>
    public static PayloadStatus ToIntegral(Extended value, ScalarKind kind, out long result) => Converted(value, kind, out result, out _);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PayloadStatus Converted(Extended value, ScalarKind kind, out long integral, out double floating)
    {
        (integral, floating) = (0, 0);
        var converted = ExtendedIntermediates.Convert(new WideValue(value.Wide, VBDoubleType.TypeInfo, value._pending), ScalarKinds.TypeOf(kind), out var error);
        if (error is { } raised)
        {
            return raised is VBRuntimeErrorId.DivisionByZero ? PayloadStatus.DivisionByZero : PayloadStatus.Overflow;
        }

        if (converted is not { } payload)
        {
            return PayloadStatus.Decline;
        }

        (integral, floating) = (payload.Integral, payload.Floating);
        return PayloadStatus.Ok;
    }

    /// <summary>
    /// A comparison of two values of a floating-point effective type (<strong>MS-VBAL §5.6.9.5</strong>): each rounded to that type's precision - not its
    /// range - and a NaN, ordered with nothing, an invalid operation. What is pending is the error it raises.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PayloadStatus Compare(ComparisonOperator @operator, Extended left, Extended right, ScalarKind effective, out bool result)
    {
        if (left._isDouble && right._isDouble && (left._pending | right._pending) is FloatingPointExceptions.None
            && (effective is not ScalarKind.Single || IsSingleExact(left._double) && IsSingleExact(right._double)))
        {
            result = effective is ScalarKind.Single
                ? PayloadOperations.Compare(@operator, (double)(float)left._double, (float)right._double)
                : PayloadOperations.Compare(@operator, left._double, right._double);
            return PayloadStatus.Ok;
        }

        return CompareWide(@operator, left, right, effective, out result);
    }

    // 2^-126, the least normal Single.
    private const double LeastNormalSingle = 1.1754943508222875e-38;

    // a value whose rounding to a Single's precision is its conversion to a Single: zero, or in the range of a normal Single.
    private static bool IsSingleExact(double value) => value == 0 || Math.Abs(value) >= LeastNormalSingle && Math.Abs(value) <= float.MaxValue;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PayloadStatus CompareWide(ComparisonOperator @operator, Extended left, Extended right, ScalarKind effective, out bool result)
    {
        result = false;
        var type = ScalarKinds.TypeOf(effective);
        var compared = ExtendedIntermediates.Compare(TokenOf(@operator), new WideValue(left.Wide, type, left._pending), new WideValue(right.Wide, type, right._pending),
            type, out var error);
        if (error is { } raised)
        {
            return raised is VBRuntimeErrorId.DivisionByZero ? PayloadStatus.DivisionByZero : PayloadStatus.Overflow;
        }

        if (compared is not { } isTrue)
        {
            return PayloadStatus.Decline;
        }

        result = isTrue;
        return PayloadStatus.Ok;
    }

    private static string TokenOf(ComparisonOperator @operator) => @operator switch
    {
        ComparisonOperator.Equal => SDK.Model.Tokens.CompareEqualOp,
        ComparisonOperator.NotEqual => SDK.Model.Tokens.CompareNotEqualOp,
        ComparisonOperator.Less => SDK.Model.Tokens.CompareLessThanOp,
        ComparisonOperator.LessOrEqual => SDK.Model.Tokens.CompareLessThanOrEqualOp,
        ComparisonOperator.Greater => SDK.Model.Tokens.CompareGreaterThanOp,
        _ => SDK.Model.Tokens.CompareGreaterThanOrEqualOp,
    };

    /// <summary>The value, as the interpreter computes with it: what a test compares the two with.</summary>
    internal WideValue AsWide(ScalarKind type) => new(Wide, ScalarKinds.TypeOf(type), _pending);
}
