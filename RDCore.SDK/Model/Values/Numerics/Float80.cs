using System.Numerics;

namespace RDCore.SDK.Model.Values.Numerics;

/// <summary>
/// The floating-point exceptions an operation of <see cref="Float80"/> values signals: the conditions an x87 floating-point unit records in its status
/// word, and keeps until they are cleared.
/// </summary>
/// <remarks>
/// The values are the bits of those conditions in the x87 status word. Only the conditions MS-VBA reports are signalled: an inexact result, a denormal
/// operand and an underflow are not errors in VBA, and are not recorded.
/// </remarks>
[Flags]
public enum FloatingPointExceptions
{
    /// <summary>No exception.</summary>
    None = 0,

    /// <summary>
    /// An operation that has no numeric result, of operands that are no NaN: infinity less infinity, zero times infinity, zero divided by zero, infinity
    /// divided by infinity. Its result is a NaN.
    /// </summary>
    Invalid = 1,

    /// <summary>A finite number other than zero divided by zero. Its result is an infinity.</summary>
    DivideByZero = 4,

    /// <summary>A result whose magnitude, rounded, is past the range of the format it is rounded to. Its result is an infinity.</summary>
    Overflow = 8,
}

/// <summary>
/// An 80-bit extended-precision binary floating-point number, as an x87 floating-point unit computes with: a sign, a 15-bit exponent and a 64-bit
/// significand, rounded to nearest, ties to even.
/// </summary>
/// <remarks>
/// <para>
/// <strong>MS-VBAL 5.6.9.3</strong> lets an implementation "perform floating point operations with a higher-precision than the effective value type
/// (such as an 'extended' or 'long double' type)", and MS-VBA does: an expression of <c>Single</c> and <c>Double</c> operands is computed in this format,
/// and its value is rounded to the effective type where it is stored. So <c>(1E+308 * 10) / 10</c> is <c>1E+308</c> with no overflow, and
/// <c>0.1 + 0.2 - 0.3</c> is <c>2.77555756156289E-17</c>, where an evaluation in <c>Double</c> gives <c>5.55111512312578E-17</c>.
/// </para>
/// <para>
/// Every operation is correctly rounded, as IEEE 754 requires of the basic operations, and reports the <see cref="FloatingPointExceptions"/> it signals;
/// a NaN produced by an invalid operation is the x87's default NaN (the "real indefinite", negative, which VBA writes <c>-nan(ind)</c>), and an
/// operation of a NaN gives that NaN back, made quiet. Rounding to <see cref="double"/> or <see cref="float"/> (<see cref="ToDouble"/>,
/// <see cref="ToSingle"/>) is what storing the value in a variable of that type does: it is a second rounding, of the value already rounded to 64 bits.
/// </para>
/// </remarks>
public readonly struct Float80 : IEquatable<Float80>
{
    private const int ExponentBias = 16383;
    private const int InfiniteExponent = 0x7FFF;
    private const ulong IntegerBit = 1UL << 63;
    private const ulong QuietBit = 1UL << 62;

    // the formats a value is rounded to: the bits of its significand, and the least and greatest exponent of a normal number of it.
    private readonly record struct Format(int Precision, int MinExponent, int MaxExponent);

    private static readonly Format Extended = new(64, -16382, 16383);
    private static readonly Format DoubleFormat = new(53, -1022, 1023);
    private static readonly Format SingleFormat = new(24, -126, 127);

    private Float80(ushort signAndExponent, ulong significand)
    {
        SignAndExponent = signAndExponent;
        Significand = significand;
    }

    /// <summary>
    /// The sign bit (bit 15) and the biased exponent (bits 0-14) of the value, as the x87 format encodes them.
    /// </summary>
    public ushort SignAndExponent { get; }

    /// <summary>
    /// The 64-bit significand of the value, with its explicit integer bit (bit 63), as the x87 format encodes it.
    /// </summary>
    public ulong Significand { get; }

    /// <summary>Positive zero.</summary>
    public static Float80 Zero => default;

    /// <summary>Positive infinity.</summary>
    public static Float80 PositiveInfinity => new(InfiniteExponent, IntegerBit);

    /// <summary>Negative infinity.</summary>
    public static Float80 NegativeInfinity => new(0x8000 | InfiniteExponent, IntegerBit);

    /// <summary>
    /// The NaN an invalid operation produces: the x87's "real indefinite", a negative quiet NaN, which VBA writes <c>-nan(ind)</c>.
    /// </summary>
    public static Float80 Indefinite => new(0x8000 | InfiniteExponent, IntegerBit | QuietBit);

    private int BiasedExponent => SignAndExponent & InfiniteExponent;

    /// <summary>Whether the sign bit is set: a negative number, negative zero, or a NaN whose sign is negative.</summary>
    public bool IsNegative => (SignAndExponent & 0x8000) != 0;

    /// <summary>Whether the value is a NaN.</summary>
    public bool IsNaN => BiasedExponent == InfiniteExponent && (Significand << 1) != 0;

    /// <summary>Whether the value is an infinity, of either sign.</summary>
    public bool IsInfinity => BiasedExponent == InfiniteExponent && (Significand << 1) == 0;

    /// <summary>Whether the value is a number: neither an infinity nor a NaN.</summary>
    public bool IsFinite => BiasedExponent != InfiniteExponent;

    /// <summary>Whether the value is zero, of either sign.</summary>
    public bool IsZero => BiasedExponent == 0 && Significand == 0;

    /// <summary>
    /// The value of a <see cref="double"/>, exactly: every <c>Double</c> is a value of this format.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    public static Float80 FromDouble(double value)
    {
        var bits = (ulong)BitConverter.DoubleToInt64Bits(value);
        var negative = (bits >> 63) != 0;
        var exponent = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & ((1UL << 52) - 1);
        if (exponent == 0x7FF)
        {
            // an infinity, or a NaN, whose payload is kept in the bits under the quiet bit.
            return new Float80((ushort)((negative ? 0x8000 : 0) | InfiniteExponent), IntegerBit | (fraction << 11));
        }

        return exponent == 0
            ? FromParts(negative, fraction, -1074)
            : FromParts(negative, fraction | (1UL << 52), exponent - 1075);
    }

    /// <summary>
    /// The value of a <see cref="float"/>, exactly: every <c>Single</c> is a value of this format.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    public static Float80 FromSingle(float value) => FromDouble(value);

    /// <summary>
    /// The value of a 64-bit integer, exactly: the significand holds every <c>LongLong</c>.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    public static Float80 FromInt64(long value) => FromParts(value < 0, value < 0 ? (ulong)-(value + 1) + 1 : (ulong)value, 0);

    /// <summary>
    /// The value of a <c>Currency</c>: its ten-thousandths divided by 10,000, rounded once.
    /// </summary>
    /// <param name="scaled">The value of the <c>Currency</c>, in ten-thousandths.</param>
    public static Float80 FromCurrency(long scaled) => Divide(FromInt64(scaled), FromInt64(10_000), out _);

    // a value that is exactly significand * 2^exponent, which the format holds.
    private static Float80 FromParts(bool negative, ulong significand, int exponent)
    {
        var flags = FloatingPointExceptions.None;
        return significand == 0 ? SignedZero(negative) : Round(negative, significand, exponent, sticky: false, Extended, ref flags);
    }

    private static Float80 SignedZero(bool negative) => new((ushort)(negative ? 0x8000 : 0), 0);

    private static Float80 SignedInfinity(bool negative) => negative ? NegativeInfinity : PositiveInfinity;

    /// <summary>
    /// The value rounded to a <see cref="double"/>, to nearest, ties to even: what storing it in a <c>Double</c> does. A value past the range of a
    /// <c>Double</c> is an infinity, and signals <see cref="FloatingPointExceptions.Overflow"/>; a NaN keeps its sign and the high bits of its payload.
    /// </summary>
    /// <param name="exceptions">The exceptions the rounding signals.</param>
    public double ToDouble(out FloatingPointExceptions exceptions)
    {
        exceptions = FloatingPointExceptions.None;
        if (!IsFinite)
        {
            var sign = IsNegative ? 1UL << 63 : 0;
            var payload = IsNaN ? Math.Max((Significand & ~IntegerBit) >> 11, 1) : 0;
            return BitConverter.Int64BitsToDouble((long)(sign | (0x7FFUL << 52) | payload));
        }

        if (IsZero)
        {
            return IsNegative ? -0.0 : 0.0;
        }

        var rounded = RoundTo(DoubleFormat, ref exceptions);
        return rounded.IsFinite ? rounded.PackDouble() : (IsNegative ? double.NegativeInfinity : double.PositiveInfinity);
    }

    /// <summary>
    /// The value rounded to a <see cref="float"/>, to nearest, ties to even: what storing it in a <c>Single</c> does. A value past the range of a
    /// <c>Single</c> is an infinity, and signals <see cref="FloatingPointExceptions.Overflow"/>.
    /// </summary>
    /// <param name="exceptions">The exceptions the rounding signals.</param>
    public float ToSingle(out FloatingPointExceptions exceptions)
    {
        exceptions = FloatingPointExceptions.None;
        if (!IsFinite || IsZero)
        {
            return (float)ToDouble(out _);
        }

        var rounded = RoundTo(SingleFormat, ref exceptions);
        return rounded.IsFinite ? (float)rounded.PackDouble() : (IsNegative ? float.NegativeInfinity : float.PositiveInfinity);
    }

    /// <summary>
    /// The value rounded to the 53 bits of a <c>Double</c>'s significand, in the range of this format: what the x87 does to a value it keeps, with its
    /// precision control set to double precision. No value overflows.
    /// </summary>
    public Float80 RoundToDoublePrecision() => RoundToPrecision(DoubleFormat.Precision);

    /// <summary>
    /// The value rounded to the 24 bits of a <c>Single</c>'s significand, in the range of this format. No value overflows.
    /// </summary>
    public Float80 RoundToSinglePrecision() => RoundToPrecision(SingleFormat.Precision);

    private Float80 RoundToPrecision(int precision)
    {
        if (!IsFinite || IsZero)
        {
            return this;
        }

        var flags = FloatingPointExceptions.None;
        return RoundTo(Extended with { Precision = precision }, ref flags);
    }

    private Float80 RoundTo(Format format, ref FloatingPointExceptions exceptions)
    {
        var (significand, exponent) = Unpack();
        return Round(IsNegative, significand, exponent, sticky: false, format, ref exceptions);
    }

    // a finite value that is a value of the double format, packed in its bits.
    private double PackDouble()
    {
        if (IsZero)
        {
            return IsNegative ? -0.0 : 0.0;
        }

        var (significand, exponent) = Unpack();
        var sign = IsNegative ? 1UL << 63 : 0;
        var lead = exponent + 63;
        if (lead >= DoubleFormat.MinExponent)
        {
            return BitConverter.Int64BitsToDouble((long)(sign | ((ulong)(lead + 1023) << 52) | ((significand << 1) >> 12)));
        }

        // a subnormal Double: its fraction is the value in units of 2^-1074.
        return BitConverter.Int64BitsToDouble((long)(sign | (significand >> (-1074 - exponent))));
    }

    // a finite value other than zero, as significand * 2^exponent, the significand normalized: its bit 63 is set.
    private (ulong Significand, int Exponent) Unpack()
    {
        var biased = BiasedExponent;
        var exponent = (biased == 0 ? 1 : biased) - ExponentBias - 63;
        var shift = BitOperations.LeadingZeroCount(Significand);
        return (Significand << shift, exponent - shift);
    }

    // MS-VBAL 5.6.9.3, IEEE 754 §4.3.1: significand * 2^exponent - plus something less than one unit of the significand's last bit, when sticky - rounded
    // to nearest, ties to even, to the precision and the range of a format: one that is past its greatest exponent is an infinity (Overflow), one under its
    // least exponent is a subnormal, with as many bits as are left of its precision.
    private static Float80 Round(bool negative, UInt128 significand, int exponent, bool sticky, Format format, ref FloatingPointExceptions exceptions)
    {
        var shift = (int)UInt128.LeadingZeroCount(significand);
        significand <<= shift;
        exponent -= shift;

        // the exponent of the leading bit: the value is in [2^lead, 2^(lead+1)).
        var lead = exponent + 127;
        if (lead > format.MaxExponent)
        {
            exceptions |= FloatingPointExceptions.Overflow;
            return SignedInfinity(negative);
        }

        var bits = format.Precision - Math.Max(0, format.MinExponent - lead);
        if (bits < 0)
        {
            return SignedZero(negative);
        }

        // the bits kept, and those rounded away: more than half of the last bit kept rounds up, and so does exactly half of an odd one.
        var dropped = 128 - bits;
        var kept = bits == 0 ? UInt128.Zero : significand >> dropped;
        var rest = dropped == 128 ? significand : significand & ((UInt128.One << dropped) - 1);
        var half = UInt128.One << (dropped - 1);
        if (rest > half || (rest == half && (sticky || (kept & 1) == 1)))
        {
            kept++;
        }

        if (kept == 0)
        {
            return SignedZero(negative);
        }

        // the value is kept * 2^unit, whose leading bit a carry out of the kept bits moves up by one - which can take it past the greatest exponent.
        var unit = lead - bits + 1;
        var length = 128 - (int)UInt128.LeadingZeroCount(kept);
        var leading = unit + length - 1;
        if (leading > format.MaxExponent)
        {
            exceptions |= FloatingPointExceptions.Overflow;
            return SignedInfinity(negative);
        }

        // in the extended format: a 64-bit significand whose bit 63 is the leading bit, and a biased exponent - or a subnormal of it, under its least.
        var mantissa = (ulong)(kept << (64 - length));
        var biased = leading + ExponentBias;
        if (biased <= 0)
        {
            mantissa >>= 1 - biased;
            biased = 0;
        }

        return new Float80((ushort)((negative ? 0x8000 : 0) | biased), mantissa);
    }

    /// <summary>The value with its sign inverted. A NaN is negated too, with no exception.</summary>
    public Float80 Negate() => new((ushort)(SignAndExponent ^ 0x8000), Significand);

    /// <summary>The value with its sign cleared. A NaN's sign is cleared too, with no exception.</summary>
    public Float80 Abs() => new((ushort)(SignAndExponent & 0x7FFF), Significand);

    // IEEE 754 §6.2.3: an operation of a NaN is a NaN, made quiet; of two, the one whose significand is the greater (the x87's rule).
    private static Float80 Quiet(Float80 nan) => new(nan.SignAndExponent, nan.Significand | QuietBit);

    private static Float80 NaNOf(Float80 a, Float80 b)
        => a.IsNaN && b.IsNaN ? Quiet((a.Significand & ~QuietBit) >= (b.Significand & ~QuietBit) ? a : b) : Quiet(a.IsNaN ? a : b);

    /// <summary>The sum of two values, correctly rounded.</summary>
    /// <param name="a">The augend.</param>
    /// <param name="b">The addend.</param>
    /// <param name="exceptions">The exceptions the operation signals: <see cref="FloatingPointExceptions.Invalid"/> for infinities of opposite signs.</param>
    public static Float80 Add(Float80 a, Float80 b, out FloatingPointExceptions exceptions)
    {
        exceptions = FloatingPointExceptions.None;
        if (a.IsNaN || b.IsNaN)
        {
            return NaNOf(a, b);
        }

        if (a.IsInfinity || b.IsInfinity)
        {
            if (a.IsInfinity && b.IsInfinity && a.IsNegative != b.IsNegative)
            {
                exceptions = FloatingPointExceptions.Invalid;
                return Indefinite;
            }

            return a.IsInfinity ? a : b;
        }

        if (a.IsZero || b.IsZero)
        {
            return a.IsZero && b.IsZero ? SignedZero(a.IsNegative && b.IsNegative) : (a.IsZero ? b : a);
        }

        var (ma, ea) = a.Unpack();
        var (mb, eb) = b.Unpack();
        var negative = a.IsNegative;
        if (ea < eb || (ea == eb && ma < mb))
        {
            (ma, ea, mb, eb) = (mb, eb, ma, ea);
            negative = b.IsNegative;
        }

        // aligned under 62 guard bits; the bits of the smaller shifted out of them are kept as one sticky bit in the last, which rounds as they would.
        var larger = (UInt128)ma << 62;
        var smaller = (UInt128)mb << 62;
        var distance = ea - eb;
        if (distance >= 127)
        {
            smaller = 1;
        }
        else if (distance > 0)
        {
            var lost = (smaller & ((UInt128.One << distance) - 1)) != 0;
            smaller = (smaller >> distance) | (lost ? UInt128.One : UInt128.Zero);
        }

        var sum = a.IsNegative == b.IsNegative ? larger + smaller : larger - smaller;
        return sum == 0 ? Zero : Round(negative, sum, ea - 62, sticky: false, Extended, ref exceptions);
    }

    /// <summary>The difference of two values, correctly rounded.</summary>
    /// <param name="a">The minuend.</param>
    /// <param name="b">The subtrahend.</param>
    /// <param name="exceptions">The exceptions the operation signals: <see cref="FloatingPointExceptions.Invalid"/> for infinities of the same sign.</param>
    public static Float80 Subtract(Float80 a, Float80 b, out FloatingPointExceptions exceptions)
        => Add(a, b.IsNaN ? b : b.Negate(), out exceptions);

    /// <summary>The product of two values, correctly rounded.</summary>
    /// <param name="a">The multiplicand.</param>
    /// <param name="b">The multiplier.</param>
    /// <param name="exceptions">The exceptions the operation signals: <see cref="FloatingPointExceptions.Invalid"/> for zero times infinity,
    /// <see cref="FloatingPointExceptions.Overflow"/> past the range of the format.</param>
    public static Float80 Multiply(Float80 a, Float80 b, out FloatingPointExceptions exceptions)
    {
        exceptions = FloatingPointExceptions.None;
        if (a.IsNaN || b.IsNaN)
        {
            return NaNOf(a, b);
        }

        var negative = a.IsNegative != b.IsNegative;
        if (a.IsInfinity || b.IsInfinity)
        {
            if (a.IsZero || b.IsZero)
            {
                exceptions = FloatingPointExceptions.Invalid;
                return Indefinite;
            }

            return SignedInfinity(negative);
        }

        if (a.IsZero || b.IsZero)
        {
            return SignedZero(negative);
        }

        var (ma, ea) = a.Unpack();
        var (mb, eb) = b.Unpack();
        return Round(negative, (UInt128)ma * mb, ea + eb, sticky: false, Extended, ref exceptions);
    }

    /// <summary>The quotient of two values, correctly rounded.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <param name="exceptions">The exceptions the operation signals: <see cref="FloatingPointExceptions.Invalid"/> for zero divided by zero and infinity
    /// divided by infinity, <see cref="FloatingPointExceptions.DivideByZero"/> for any other finite number divided by zero,
    /// <see cref="FloatingPointExceptions.Overflow"/> past the range of the format.</param>
    public static Float80 Divide(Float80 a, Float80 b, out FloatingPointExceptions exceptions)
    {
        exceptions = FloatingPointExceptions.None;
        if (a.IsNaN || b.IsNaN)
        {
            return NaNOf(a, b);
        }

        var negative = a.IsNegative != b.IsNegative;
        if ((a.IsInfinity && b.IsInfinity) || (a.IsZero && b.IsZero))
        {
            exceptions = FloatingPointExceptions.Invalid;
            return Indefinite;
        }

        if (a.IsInfinity || b.IsZero)
        {
            exceptions = a.IsInfinity ? FloatingPointExceptions.None : FloatingPointExceptions.DivideByZero;
            return SignedInfinity(negative);
        }

        if (a.IsZero || b.IsInfinity)
        {
            return SignedZero(negative);
        }

        // a 128-bit quotient in two steps of 64: the dividend shifted for a first quotient of 64 bits, then its remainder; what is left is sticky.
        var (ma, ea) = a.Unpack();
        var (mb, eb) = b.Unpack();
        var shift = ma >= mb ? 63 : 64;
        var dividend = (UInt128)ma << shift;
        var high = (ulong)(dividend / mb);
        var remainder = (ulong)(dividend % mb);
        var next = (UInt128)remainder << 64;
        var low = (ulong)(next / mb);
        var sticky = next % mb != 0;
        return Round(negative, ((UInt128)high << 64) | low, ea - eb - shift - 64, sticky, Extended, ref exceptions);
    }

    /// <summary>
    /// The integer the value rounds to - to nearest, ties to even (<see cref="MidpointRounding.ToEven"/>); toward zero
    /// (<see cref="MidpointRounding.ToZero"/>, VBA's <c>Fix</c>); toward negative infinity (<see cref="MidpointRounding.ToNegativeInfinity"/>, VBA's
    /// <c>Int</c>); or toward positive infinity (<see cref="MidpointRounding.ToPositiveInfinity"/>). An infinity, a NaN and zero are themselves.
    /// </summary>
    /// <param name="mode">The direction of the rounding.</param>
    public Float80 RoundToIntegral(MidpointRounding mode)
    {
        if (!IsFinite || IsZero)
        {
            return this;
        }

        var (significand, exponent) = Unpack();
        if (exponent >= 0)
        {
            return this;
        }

        // the integer part, and what is under it as a fraction of 2^64 (half is bit 63): a value of an exponent under -64 is less than one half, and
        // only its being more than nothing matters; one of -64 is from one half to one. The integer part is under 2^63, which one more does not pass.
        var integral = exponent <= -64 ? 0UL : significand >> -exponent;
        var fraction = exponent < -64 ? 1UL : exponent == -64 ? significand : significand << (64 + exponent);
        var roundsUp = mode switch
        {
            MidpointRounding.ToEven => fraction > IntegerBit || (fraction == IntegerBit && (integral & 1) == 1),
            MidpointRounding.AwayFromZero => fraction >= IntegerBit,
            MidpointRounding.ToZero => false,
            MidpointRounding.ToNegativeInfinity => IsNegative && fraction != 0,
            MidpointRounding.ToPositiveInfinity => !IsNegative && fraction != 0,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        return FromParts(IsNegative, integral + (roundsUp ? 1UL : 0UL), 0);
    }

    /// <summary>
    /// The value as a 64-bit integer, when it is an integer of that range.
    /// </summary>
    /// <param name="value">The integer, or 0.</param>
    /// <returns>Whether the value is an integer from <see cref="long.MinValue"/> to <see cref="long.MaxValue"/>.</returns>
    public bool TryToInt64(out long value)
    {
        value = 0;
        if (!IsFinite)
        {
            return false;
        }

        if (IsZero)
        {
            return true;
        }

        // an integer has nothing under its units bit; its magnitude is at most 2^63 (long.MinValue's), whose normalized exponent is 0.
        var (significand, exponent) = Unpack();
        if (exponent > 0 || (exponent < 0 && (exponent <= -64 || significand << (64 + exponent) != 0)))
        {
            return false;
        }

        var magnitude = exponent == 0 ? significand : significand >> -exponent;
        if (IsNegative)
        {
            if (magnitude > IntegerBit)
            {
                return false;
            }

            value = magnitude == IntegerBit ? long.MinValue : -(long)magnitude;
            return true;
        }

        if (magnitude >= IntegerBit)
        {
            return false;
        }

        value = (long)magnitude;
        return true;
    }

    /// <summary>
    /// Compares two values as IEEE 754 orders them: negative and positive zero are equal, and a NaN is ordered with nothing.
    /// </summary>
    /// <returns>A negative number, zero or a positive number as <paramref name="a"/> is less than, equal to or greater than <paramref name="b"/>; or
    /// <see langword="null"/> when either is a NaN.</returns>
    public static int? Compare(Float80 a, Float80 b)
    {
        if (a.IsNaN || b.IsNaN)
        {
            return null;
        }

        if (a.IsZero && b.IsZero)
        {
            return 0;
        }

        if (a.IsNegative != b.IsNegative)
        {
            return a.IsNegative ? -1 : 1;
        }

        var magnitude = (a.BiasedExponent, a.Significand).CompareTo((b.BiasedExponent, b.Significand));
        return a.IsNegative ? -magnitude : magnitude;
    }

    /// <summary>Whether two values have the same encoding: a bitwise comparison, under which a NaN equals itself and the two zeros differ.</summary>
    public bool Equals(Float80 other) => SignAndExponent == other.SignAndExponent && Significand == other.Significand;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Float80 other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(SignAndExponent, Significand);

    /// <summary>The value rounded to a <see cref="double"/>, and its encoding.</summary>
    public override string ToString() => $"{ToDouble(out _):R} (0x{SignAndExponent:X4}{Significand:X16})";
}
