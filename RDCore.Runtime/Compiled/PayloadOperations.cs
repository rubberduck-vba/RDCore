using RDCore.SDK.Model.Types;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// What an operation of payloads says of itself: it is done, it raises an error, or it is not done here and the boxed evaluation does it.
/// </summary>
public enum PayloadStatus
{
    /// <summary>The operation is done: its result is the value.</summary>
    Ok = 0,

    /// <summary>The operation is not done on payloads - its result is not exact, or rounds - and the boxed evaluation does it, raising what it raises.</summary>
    Decline = -1,

    /// <summary>Run-time error 6, <em>Overflow</em>: the result is past the range of the type.</summary>
    Overflow = 6,

    /// <summary>Run-time error 11, <em>Division by zero</em>.</summary>
    DivisionByZero = 11,
}

/// <summary>
/// A comparison operator (<strong>MS-VBAL §5.6.9.5</strong>), as compiled code names it.
/// </summary>
public enum ComparisonOperator
{
    /// <summary><c>=</c></summary>
    Equal,

    /// <summary><c>&lt;&gt;</c></summary>
    NotEqual,

    /// <summary><c>&lt;</c></summary>
    Less,

    /// <summary><c>&lt;=</c></summary>
    LessOrEqual,

    /// <summary><c>&gt;</c></summary>
    Greater,

    /// <summary><c>&gt;=</c></summary>
    GreaterOrEqual,
}

/// <summary>
/// A logical operator (<strong>MS-VBAL §5.6.9.8</strong>), as compiled code names it.
/// </summary>
public enum LogicalOperator
{
    /// <summary><c>And</c></summary>
    And,

    /// <summary><c>Or</c></summary>
    Or,

    /// <summary><c>Xor</c></summary>
    Xor,

    /// <summary><c>Eqv</c></summary>
    Eqv,

    /// <summary><c>Imp</c></summary>
    Imp,
}

/// <summary>
/// The operations of payloads of an integer type, a <c>Boolean</c> or a <c>Currency</c>, their comparisons, and the exact conversions of a payload from one
/// scalar type to another (IR lowering S1.8): one static method per operation, told the type it operates in, that says what it did
/// (<see cref="PayloadStatus"/>). The interpreter's evaluation on payloads and compiled code both call them, so the two are one implementation.
/// </summary>
/// <remarks>
/// The operations are those of <strong>MS-VBAL §5.6.9.3</strong> in the type's own width, checked: a result that does not fit is an overflow, and an integer
/// division or a modulo by zero a division by zero. An operation the boxed evaluation does by rounding - a <c>Currency</c> product that is no whole number of
/// ten-thousandths, a conversion that is not exact - declines.
/// </remarks>
public static class PayloadOperations
{
    /// <summary>What a <c>Currency</c> is scaled by.</summary>
    public const long CurrencyScale = 10000;

    /// <summary>
    /// Whether <paramref name="value"/> is a value of <paramref name="kind"/>, an integer type or a <c>Boolean</c>: in the type's range, and -1 or 0 for a
    /// <c>Boolean</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInRange(ScalarKind kind, long value) => kind switch
    {
        ScalarKind.Byte => value is >= byte.MinValue and <= byte.MaxValue,
        ScalarKind.Integer => value is >= short.MinValue and <= short.MaxValue,
        ScalarKind.Long => value is >= int.MinValue and <= int.MaxValue,
        ScalarKind.LongLong => true,
        ScalarKind.Boolean => value is 0 or -1,
        _ => false,
    };

    /// <summary><c>left + right</c>, of an integer type or a <c>Currency</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PayloadStatus Add(ScalarKind kind, long left, long right, out long result)
    {
        if (kind is ScalarKind.LongLong or ScalarKind.Currency)
        {
            result = left + right;
            return ((left ^ result) & (right ^ result)) < 0 ? PayloadStatus.Overflow : PayloadStatus.Ok;
        }

        result = left + right;
        return Ranged(kind, result);
    }

    /// <summary><c>left - right</c>, of an integer type or a <c>Currency</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PayloadStatus Subtract(ScalarKind kind, long left, long right, out long result)
    {
        if (kind is ScalarKind.LongLong or ScalarKind.Currency)
        {
            result = left - right;
            return ((left ^ right) & (left ^ result)) < 0 ? PayloadStatus.Overflow : PayloadStatus.Ok;
        }

        result = left - right;
        return Ranged(kind, result);
    }

    /// <summary>
    /// <c>left * right</c>, of an integer type or a <c>Currency</c>: a <c>Currency</c> product is the product of the ten-thousandths divided by 10,000, when
    /// that is a whole number - the boxed evaluation rounds one that is not, to even.
    /// </summary>
    public static PayloadStatus Multiply(ScalarKind kind, long left, long right, out long result)
    {
        if (kind is ScalarKind.Currency)
        {
            result = 0;
            var product = (Int128)left * right;
            if (product % CurrencyScale != 0)
            {
                return PayloadStatus.Decline;
            }

            var scaled = product / CurrencyScale;
            if (scaled < long.MinValue || scaled > long.MaxValue)
            {
                return PayloadStatus.Overflow;
            }

            result = (long)scaled;
            return PayloadStatus.Ok;
        }

        if (kind is ScalarKind.LongLong)
        {
            var wide = Math.BigMul(left, right, out var low);
            result = low;
            return wide == (low >> 63) ? PayloadStatus.Ok : PayloadStatus.Overflow;
        }

        result = left * right;
        return Ranged(kind, result);
    }

    /// <summary><c>left \ right</c> (<strong>MS-VBAL §5.6.9.3.5</strong>), of an integer type: the quotient, truncated.</summary>
    public static PayloadStatus IntegerDivide(ScalarKind kind, long left, long right, out long result)
    {
        result = 0;
        if (kind is ScalarKind.Currency or ScalarKind.Boolean)
        {
            return PayloadStatus.Decline;
        }

        if (right == 0)
        {
            return PayloadStatus.DivisionByZero;
        }

        if (left == long.MinValue && right == -1)
        {
            return PayloadStatus.Overflow;
        }

        result = left / right;
        return Ranged(kind, result);
    }

    /// <summary><c>left Mod right</c> (<strong>MS-VBAL §5.6.9.3.6</strong>), of an integer type: the remainder, of the dividend's sign.</summary>
    public static PayloadStatus Modulo(ScalarKind kind, long left, long right, out long result)
    {
        result = 0;
        if (kind is ScalarKind.Currency or ScalarKind.Boolean)
        {
            return PayloadStatus.Decline;
        }

        if (right == 0)
        {
            return PayloadStatus.DivisionByZero;
        }

        if (left == long.MinValue && right == -1)
        {
            return PayloadStatus.Overflow;
        }

        result = left % right;
        return Ranged(kind, result);
    }

    /// <summary>
    /// A logical operation (<strong>MS-VBAL §5.6.9.8</strong>): the bits of the operands, in an integer type or a <c>Boolean</c> - a <c>Boolean</c>'s -1 or
    /// 0 giving -1 or 0.
    /// </summary>
    public static PayloadStatus Logical(LogicalOperator @operator, ScalarKind kind, long left, long right, out long result)
    {
        result = @operator switch
        {
            LogicalOperator.And => left & right,
            LogicalOperator.Or => left | right,
            LogicalOperator.Xor => left ^ right,
            LogicalOperator.Eqv => Invert(kind, left ^ right),
            _ => Invert(kind, left) | right,
        };

        return kind is ScalarKind.Currency ? PayloadStatus.Decline : Ranged(kind, result);
    }

    /// <summary><c>Not operand</c> (<strong>MS-VBAL §5.6.9.8.1</strong>): the bits inverted, in an integer type or a <c>Boolean</c> - the 8 bits of a <c>Byte</c>.</summary>
    public static PayloadStatus Not(ScalarKind kind, long operand, out long result)
    {
        result = Invert(kind, operand);
        return kind is ScalarKind.Currency ? PayloadStatus.Decline : Ranged(kind, result);
    }

    /// <summary><c>-operand</c>, of an integer type or a <c>Currency</c>: the negation of the least value of an integer type is not in its range.</summary>
    public static PayloadStatus Negate(ScalarKind kind, long operand, out long result)
    {
        result = 0;
        if (operand == long.MinValue)
        {
            return PayloadStatus.Overflow;
        }

        result = -operand;
        return kind is ScalarKind.Currency ? PayloadStatus.Ok : Ranged(kind, result);
    }

    /// <summary><c>-operand</c>, of a <c>Single</c>, a <c>Double</c> or a <c>Date</c>: a <c>Date</c> that is no date's serial is not one.</summary>
    public static PayloadStatus Negate(ScalarKind kind, double operand, out double result)
        => Floating(kind, -operand, out result);

    /// <summary>
    /// A comparison of two payloads of an integer type, a <c>Boolean</c> or a <c>Currency</c> (<strong>MS-VBAL §5.6.9.5</strong>), let-coerced to its
    /// effective type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Compare(ComparisonOperator @operator, long left, long right) => IsTrue(@operator, left.CompareTo(right));

    /// <summary>
    /// A comparison of two payloads of a <c>Single</c>, a <c>Double</c> or a <c>Date</c>, let-coerced to its effective type, as <see cref="double.CompareTo(double)"/>
    /// orders them.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Compare(ComparisonOperator @operator, double left, double right) => IsTrue(@operator, left.CompareTo(right));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTrue(ComparisonOperator @operator, int order) => @operator switch
    {
        ComparisonOperator.Equal => order == 0,
        ComparisonOperator.NotEqual => order != 0,
        ComparisonOperator.Less => order < 0,
        ComparisonOperator.LessOrEqual => order <= 0,
        ComparisonOperator.Greater => order > 0,
        _ => order >= 0,
    };

    /// <summary>
    /// A payload of <paramref name="from"/> converted to <paramref name="to"/>, when the conversion is exact - what a value of one scalar type is in another,
    /// with no rounding to decide: an integer in the range of an integer type, an integer as a floating-point value, a <c>Date</c> as its serial, any number
    /// as a <c>Boolean</c>. Anything else is the boxed coercion's (<strong>MS-VBAL §5.5.1.2.1</strong>), which rounds, and declines.
    /// </summary>
    /// <param name="from">The kind of the payload.</param>
    /// <param name="integral">The payload of an integral kind.</param>
    /// <param name="floating">The payload of a floating-point kind.</param>
    /// <param name="to">The kind it is converted to.</param>
    /// <param name="resultIntegral">The payload converted, of an integral kind.</param>
    /// <param name="resultFloating">The payload converted, of a floating-point kind.</param>
    public static PayloadStatus Convert(ScalarKind from, long integral, double floating, ScalarKind to, out long resultIntegral, out double resultFloating)
    {
        resultIntegral = integral;
        resultFloating = floating;
        if (from == to)
        {
            return PayloadStatus.Ok;
        }

        var isIntegral = ScalarKinds.IsIntegral(from);

        // MS-VBAL §5.5.1.2.2: any number is True when it is not zero, and a Boolean is the -1 or 0 it holds - which a Byte does not hold, its True being 255:
        // the boxed coercion's.
        if (to is ScalarKind.Boolean)
        {
            resultIntegral = (isIntegral ? integral != 0 : floating != 0) ? -1 : 0;
            return PayloadStatus.Ok;
        }

        // a Currency is a whole number in an integer type when it is one, which nothing rounds; as a floating-point value it is the Double nearest a decimal
        // number, which is the boxed coercion's.
        if (from is ScalarKind.Currency)
        {
            if (to is ScalarKind.Byte or ScalarKind.Integer or ScalarKind.Long or ScalarKind.LongLong && integral % CurrencyScale == 0
                && IsInRange(to, integral / CurrencyScale))
            {
                resultIntegral = integral / CurrencyScale;
                return PayloadStatus.Ok;
            }

            return PayloadStatus.Decline;
        }

        if (to is ScalarKind.Currency)
        {
            return ToCurrency(from, integral, floating, out resultIntegral);
        }

        if (isIntegral)
        {
            switch (to)
            {
                case ScalarKind.Byte or ScalarKind.Integer or ScalarKind.Long or ScalarKind.LongLong:
                    return IsInRange(to, integral) ? PayloadStatus.Ok : PayloadStatus.Decline;
                case ScalarKind.Double or ScalarKind.Date:
                    return Floating(to, integral, out resultFloating) is PayloadStatus.Ok ? PayloadStatus.Ok : PayloadStatus.Decline;
                case ScalarKind.Single when from is not ScalarKind.LongLong:
                    return Floating(to, integral, out resultFloating) is PayloadStatus.Ok ? PayloadStatus.Ok : PayloadStatus.Decline;
                default:
                    return PayloadStatus.Decline;
            }
        }

        // a Date is its serial (MS-VBAL §5.5.1.2.3), and a number in the range of the serials is a Date.
        return to is ScalarKind.Double or ScalarKind.Date && Floating(to, floating, out resultFloating) is PayloadStatus.Ok
            ? PayloadStatus.Ok
            : PayloadStatus.Decline;
    }

    /// <summary>
    /// A floating-point value of <paramref name="kind"/>: a finite one, rounded to a <c>Single</c> for a <c>Single</c>; a date's serial for a <c>Date</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PayloadStatus Floating(ScalarKind kind, double value, out double result)
    {
        result = kind is ScalarKind.Single ? (float)value : value;
        return kind switch
        {
            ScalarKind.Single or ScalarKind.Double => double.IsFinite(result) ? PayloadStatus.Ok : PayloadStatus.Overflow,
            ScalarKind.Date => VBDateType.IsValidSerial(value) ? PayloadStatus.Ok : PayloadStatus.Overflow,
            _ => PayloadStatus.Decline,
        };
    }

    // MS-VBAL §5.5.1.2.1: a whole number is a Currency when it is in its range; a Double when it has no more than four decimal places - and few enough digits
    // that the Decimal the boxed coercion makes of it, rounded to 15 significant digits, is the decimal number it is the Double nearest to.
    private static PayloadStatus ToCurrency(ScalarKind from, long integral, double floating, out long result)
    {
        result = 0;
        if (ScalarKinds.IsIntegral(from))
        {
            if (integral is < long.MinValue / CurrencyScale or > long.MaxValue / CurrencyScale)
            {
                return PayloadStatus.Decline;
            }

            result = integral * CurrencyScale;
            return PayloadStatus.Ok;
        }

        var scaled = floating * CurrencyScale;
        if (from is not ScalarKind.Double || Math.Abs(scaled) >= 1e15 || scaled != Math.Floor(scaled) || scaled / CurrencyScale != floating)
        {
            return PayloadStatus.Decline;
        }

        result = (long)scaled;
        return PayloadStatus.Ok;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PayloadStatus Ranged(ScalarKind kind, long value) => IsInRange(kind, value) ? PayloadStatus.Ok : PayloadStatus.Overflow;

    private static long Invert(ScalarKind kind, long value) => kind is ScalarKind.Byte ? ~value & byte.MaxValue : ~value;
}
