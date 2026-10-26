using System.Globalization;
using System.Numerics;
using RDCore.SDK.Model.Values.Numerics;

namespace RDCore.Tests.Model.Values;

/// <summary>
/// <see cref="Float80"/> against an exact reference: each operand is the rational it encodes, each result that rational's operation rounded to nearest,
/// ties to even, in the format - so every operation is checked for being correctly rounded, on operands of full 64-bit significands, subnormals and both
/// ends of the range. The values MS-VBA gives are recorded in Excel (grid <c>oracle\s42</c>).
/// </summary>
[TestClass]
public sealed class Float80Tests
{
    // a value exactly: (-1)^Negative * Numerator / Denominator, the denominator a power of two for any binary value.
    private readonly record struct Exact(bool Negative, BigInteger Numerator, BigInteger Denominator);

    private readonly record struct Fmt(int Precision, int MinExponent, int MaxExponent);

    private static readonly Fmt ExtendedFmt = new(64, -16382, 16383);
    private static readonly Fmt DoubleFmt = new(53, -1022, 1023);
    private static readonly Fmt SingleFmt = new(24, -126, 127);

    private static Exact ExactOf(Float80 value)
    {
        Assert.IsTrue(value.IsFinite);
        var biased = value.SignAndExponent & 0x7FFF;
        var exponent = (biased == 0 ? 1 : biased) - 16383 - 63;
        var magnitude = new BigInteger(value.Significand);
        return exponent >= 0
            ? new Exact(value.IsNegative, magnitude << exponent, BigInteger.One)
            : new Exact(value.IsNegative, magnitude, BigInteger.One << -exponent);
    }

    private static Exact ExactOf(double value) => ExactOf(Float80.FromDouble(value));

    // the reference rounding of an exact value other than zero: the result's magnitude as significand * 2^unit, or null for an overflow.
    private static (BigInteger Significand, int Unit)? Rounded(Exact exact, Fmt format)
    {
        var (n, d) = (exact.Numerator, exact.Denominator);
        var lead = (int)(n.GetBitLength() - d.GetBitLength());
        if ((lead >= 0 ? n >> lead : n << -lead) < d)
        {
            lead--;
        }

        var unit = Math.Max(lead, format.MinExponent) - format.Precision + 1;
        var scaledNumerator = unit >= 0 ? n : n << -unit;
        var scaledDenominator = unit >= 0 ? d << unit : d;
        var quotient = BigInteger.DivRem(scaledNumerator, scaledDenominator, out var remainder);
        var twice = remainder * 2;
        if (twice > scaledDenominator || (twice == scaledDenominator && !quotient.IsEven))
        {
            quotient++;
        }

        if (quotient.IsZero)
        {
            return (quotient, unit);
        }

        var leading = unit + (int)quotient.GetBitLength() - 1;
        return leading > format.MaxExponent ? null : (quotient, unit);
    }

    private static void AssertIsRounded(Exact exact, Float80 actual, Fmt format, string what)
    {
        if (exact.Numerator.IsZero)
        {
            Assert.IsTrue(actual.IsZero, what);
            return;
        }

        if (Rounded(exact, format) is not { } expected)
        {
            Assert.IsTrue(actual.IsInfinity && actual.IsNegative == exact.Negative, $"{what}: expected an infinity, got {actual}");
            return;
        }

        if (expected.Significand.IsZero)
        {
            Assert.IsTrue(actual.IsZero && actual.IsNegative == exact.Negative, $"{what}: expected a zero, got {actual}");
            return;
        }

        var got = ExactOf(actual);
        var want = expected.Unit >= 0
            ? new Exact(exact.Negative, expected.Significand << expected.Unit, BigInteger.One)
            : new Exact(exact.Negative, expected.Significand, BigInteger.One << -expected.Unit);
        Assert.IsTrue(got.Negative == want.Negative && got.Numerator * want.Denominator == want.Numerator * got.Denominator, $"{what}: got {actual}");
    }

    private static Exact Sum(Exact a, Exact b)
    {
        var l = (a.Negative ? -a.Numerator : a.Numerator) * b.Denominator;
        var r = (b.Negative ? -b.Numerator : b.Numerator) * a.Denominator;
        var n = l + r;
        return new Exact(n.Sign < 0, BigInteger.Abs(n), a.Denominator * b.Denominator);
    }

    private static Exact Negated(Exact a) => a with { Negative = !a.Negative };

    private static Exact Product(Exact a, Exact b) => new(a.Negative != b.Negative, a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    private static Exact Quotient(Exact a, Exact b) => new(a.Negative != b.Negative, a.Numerator * b.Denominator, a.Denominator * b.Numerator);

    // operands of every kind: doubles of random bits (normal, subnormal, either end of the range), and extended values of 64 significant bits - products
    // and quotients of doubles, rounded - some scaled toward the ends of the extended range.
    private static IEnumerable<Float80> Operands(int seed, int count)
    {
        var random = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var bits = random.NextInt64() ^ ((long)random.Next() << 32);
            var d = BitConverter.Int64BitsToDouble(bits);
            if (!double.IsFinite(d))
            {
                continue;
            }

            var x = Float80.FromDouble(d);
            switch (i % 4)
            {
                case 0:
                    yield return x;
                    break;
                case 1:
                    yield return Float80.Divide(x, Float80.FromDouble(3 + random.NextDouble()), out _);
                    break;
                case 2:
                    var scale = Float80.FromDouble(Math.Pow(2, random.Next(-1000, 1000)));
                    var y = Float80.Multiply(Float80.Multiply(x, scale, out _), scale, out _);
                    if (y.IsFinite && !y.IsZero)
                    {
                        yield return Float80.Multiply(y, Float80.FromDouble(1 + random.NextDouble()), out _);
                    }

                    break;
                default:
                    yield return Float80.FromDouble(random.NextDouble() * 4 - 2);
                    break;
            }
        }
    }

    private static IEnumerable<(Float80 A, Float80 B)> Pairs(int seed)
    {
        var a = Operands(seed, 1200).ToArray();
        var b = Operands(seed + 1, 1200).ToArray();
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            yield return (a[i], b[i]);
            // operands near one another, whose difference cancels.
            yield return (a[i], Float80.Multiply(a[i], Float80.FromDouble(1 + Math.Pow(2, -(i % 70))), out _));
        }
    }

    [TestMethod]
    public void TheSumAndTheDifference_AreCorrectlyRounded()
    {
        foreach (var (a, b) in Pairs(1))
        {
            if (!a.IsFinite || !b.IsFinite)
            {
                continue;
            }

            AssertIsRounded(Sum(ExactOf(a), ExactOf(b)), Float80.Add(a, b, out _), ExtendedFmt, $"{a} + {b}");
            AssertIsRounded(Sum(ExactOf(a), Negated(ExactOf(b))), Float80.Subtract(a, b, out _), ExtendedFmt, $"{a} - {b}");
        }
    }

    // a tie in the 64 bits kept that only the bits of the smaller operand shifted out of the guard bits break: 1 + 2^-64 is a tie, 1 + 2^-64 + 2^-127
    // is more than one; 1 - (2^-65 + 2^-128) is less than the tie 1 - 2^-65.
    [TestMethod]
    [DataRow(1.0, -64, -127)]
    [DataRow(-1.0, -64, -127)]
    [DataRow(1.0, -65, -128)]
    public void ATieBrokenByTheBitsShiftedOut_IsNoTie(double one, int high, int low)
    {
        var a = Float80.FromDouble(one);
        var b = Float80.Add(Float80.FromDouble(Math.Pow(2, high)), Float80.FromDouble(Math.Pow(2, low)), out _);
        AssertIsRounded(Sum(ExactOf(a), ExactOf(b)), Float80.Add(a, b, out _), ExtendedFmt, $"{a} + {b}");
        AssertIsRounded(Sum(ExactOf(a), Negated(ExactOf(b))), Float80.Subtract(a, b, out _), ExtendedFmt, $"{a} - {b}");
    }

    [TestMethod]
    public void TheProduct_IsCorrectlyRounded()
    {
        foreach (var (a, b) in Pairs(2))
        {
            if (!a.IsFinite || !b.IsFinite)
            {
                continue;
            }

            var product = Float80.Multiply(a, b, out var exceptions);
            AssertIsRounded(Product(ExactOf(a), ExactOf(b)), product, ExtendedFmt, $"{a} * {b}");
            Assert.AreEqual(product.IsInfinity ? FloatingPointExceptions.Overflow : FloatingPointExceptions.None, exceptions);
        }
    }

    [TestMethod]
    public void TheQuotient_IsCorrectlyRounded()
    {
        foreach (var (a, b) in Pairs(3))
        {
            if (!a.IsFinite || !b.IsFinite || b.IsZero)
            {
                continue;
            }

            AssertIsRounded(Quotient(ExactOf(a), ExactOf(b)), Float80.Divide(a, b, out _), ExtendedFmt, $"{a} / {b}");
        }
    }

    [TestMethod]
    public void ADoubleAndASingle_AreTheExtendedValueRoundedOnce()
    {
        foreach (var x in Operands(4, 4000))
        {
            if (!x.IsFinite)
            {
                continue;
            }

            var exact = ExactOf(x);
            AssertIsRounded(exact, Float80.FromDouble(x.ToDouble(out _)), DoubleFmt, $"(double){x}");
            AssertIsRounded(exact, Float80.FromSingle(x.ToSingle(out _)), SingleFmt, $"(float){x}");
            AssertIsRounded(exact, x.RoundToDoublePrecision(), ExtendedFmt with { Precision = 53 }, $"53 bits of {x}");
            AssertIsRounded(exact, x.RoundToSinglePrecision(), ExtendedFmt with { Precision = 24 }, $"24 bits of {x}");
        }
    }

    [TestMethod]
    public void EveryDouble_RoundTrips()
    {
        var random = new Random(5);
        for (var i = 0; i < 20000; i++)
        {
            var d = BitConverter.Int64BitsToDouble(random.NextInt64() ^ ((long)random.Next() << 32));
            var back = Float80.FromDouble(d).ToDouble(out var exceptions);
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(d), BitConverter.DoubleToInt64Bits(back), $"{d:R}");
            Assert.AreEqual(FloatingPointExceptions.None, exceptions);
        }
    }

    [TestMethod]
    [DataRow(2.5, MidpointRounding.ToEven, 2.0)]
    [DataRow(3.5, MidpointRounding.ToEven, 4.0)]
    [DataRow(-2.5, MidpointRounding.ToEven, -2.0)]
    [DataRow(0.5, MidpointRounding.ToEven, 0.0)]
    [DataRow(0.75, MidpointRounding.ToEven, 1.0)]
    [DataRow(1E-30, MidpointRounding.ToEven, 0.0)]
    [DataRow(-1E-30, MidpointRounding.ToNegativeInfinity, -1.0)]
    [DataRow(-3.0, MidpointRounding.ToNegativeInfinity, -3.0)]
    [DataRow(-2.25, MidpointRounding.ToNegativeInfinity, -3.0)]
    [DataRow(-2.75, MidpointRounding.ToZero, -2.0)]
    [DataRow(2.25, MidpointRounding.ToPositiveInfinity, 3.0)]
    [DataRow(0.5, MidpointRounding.AwayFromZero, 1.0)]
    [DataRow(1E+300, MidpointRounding.ToZero, 1E+300)]
    public void RoundToIntegral_RoundsInItsDirection(double value, MidpointRounding mode, double expected)
        => Assert.AreEqual(expected, Float80.FromDouble(value).RoundToIntegral(mode).ToDouble(out _));

    [TestMethod]
    public void AValueJustOverAHalf_RoundsToTheNextInteger()
    {
        // 2.5 + 2^-60 is no tie in 64 bits (grid oracle\s42 C14: MS-VBA assigns it to a Long as 3).
        var x = Float80.Add(Float80.FromDouble(2.5), Float80.FromDouble(Math.Pow(2, -60)), out _);
        Assert.IsTrue(x.RoundToIntegral(MidpointRounding.ToEven).TryToInt64(out var value));
        Assert.AreEqual(3L, value);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(1L)]
    [DataRow(-1L)]
    [DataRow(long.MaxValue)]
    [DataRow(long.MinValue)]
    [DataRow(1234567890123456789L)]
    public void EveryLongLong_IsExact(long value)
    {
        Assert.IsTrue(Float80.FromInt64(value).TryToInt64(out var back));
        Assert.AreEqual(value, back);
    }

    [TestMethod]
    public void AValuePastALongLong_OrNoInteger_IsNotOne()
    {
        Assert.IsFalse(Float80.Add(Float80.FromInt64(long.MaxValue), Float80.FromInt64(1), out _).TryToInt64(out _));
        Assert.IsFalse(Float80.FromDouble(0.5).TryToInt64(out _));
        Assert.IsFalse(Float80.PositiveInfinity.TryToInt64(out _));
    }

    [TestMethod]
    public void AnInvalidOperation_IsTheIndefiniteNaN()
    {
        var inf = Float80.PositiveInfinity;
        Assert.AreEqual(Float80.Indefinite, Float80.Subtract(inf, inf, out var e1));
        Assert.AreEqual(FloatingPointExceptions.Invalid, e1);
        Assert.AreEqual(Float80.Indefinite, Float80.Divide(Float80.Zero, Float80.Zero, out var e2));
        Assert.AreEqual(FloatingPointExceptions.Invalid, e2);
        Assert.AreEqual(Float80.Indefinite, Float80.Multiply(Float80.Zero, inf, out var e3));
        Assert.AreEqual(FloatingPointExceptions.Invalid, e3);
        // VBA writes it -nan(ind): the bits of .NET's 0.0 / 0.0 on x64.
        Assert.AreEqual(unchecked((long)0xFFF8000000000000), BitConverter.DoubleToInt64Bits(Float80.Indefinite.ToDouble(out _)));
    }

    [TestMethod]
    public void ANonZeroNumberDividedByZero_IsAnInfinity()
    {
        Assert.AreEqual(Float80.NegativeInfinity, Float80.Divide(Float80.FromDouble(-1), Float80.Zero, out var exceptions));
        Assert.AreEqual(FloatingPointExceptions.DivideByZero, exceptions);
    }

    [TestMethod]
    public void AnOperationOfANaN_IsThatNaN_WithNoException()
    {
        var nan = Float80.FromDouble(double.NaN);
        Assert.IsTrue(Float80.Add(nan, Float80.FromDouble(1), out var exceptions).IsNaN);
        Assert.AreEqual(FloatingPointExceptions.None, exceptions);
        Assert.IsNull(Float80.Compare(nan, nan));
    }

    [TestMethod]
    public void Compare_OrdersAsIeee754Does()
    {
        Assert.AreEqual(0, Float80.Compare(Float80.Zero, Float80.Zero.Negate()));
        Assert.IsTrue(Float80.Compare(Float80.FromDouble(-2), Float80.FromDouble(-1)) < 0);
        Assert.IsTrue(Float80.Compare(Float80.FromDouble(double.Epsilon), Float80.Zero) > 0);
        Assert.IsTrue(Float80.Compare(Float80.NegativeInfinity, Float80.FromDouble(-1E+308)) < 0);
    }

    // the values MS-VBA computes (grid oracle\s42): what an evaluation in Double gives otherwise is in the comment.
    [TestMethod]
    public void AnIntermediatePastADouble_IsAValue()
    {
        // A01: (1E+308 * 10) / 10 - Double: an overflow.
        var x = Float80.Multiply(Float80.FromDouble(1E+308), Float80.FromDouble(10), out var e1);
        var y = Float80.Divide(x, Float80.FromDouble(10), out var e2);
        Assert.AreEqual(FloatingPointExceptions.None, e1 | e2);
        Assert.AreEqual(1E+308, y.ToDouble(out var e3));
        Assert.AreEqual(FloatingPointExceptions.None, e3);
        Assert.AreEqual(double.PositiveInfinity, x.ToDouble(out var e4));
        Assert.AreEqual(FloatingPointExceptions.Overflow, e4);
    }

    [TestMethod]
    public void TheSumOfThreeDoubles_IsRoundedOnceToSixtyFourBits()
    {
        // B04: 0.1 + 0.2 - 0.3 - Double: 5.55111512312578E-17.
        var sum = Float80.Add(Float80.FromDouble(0.1), Float80.FromDouble(0.2), out _);
        Assert.AreEqual("2.77555756156289E-17", Float80.Subtract(sum, Float80.FromDouble(0.3), out _).ToDouble(out _).ToString("G15", CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void ADoubleStored_IsRoundedTwice()
    {
        // C01: 1 + (2^-53 + 2^-78) is rounded to 64 bits, a tie under 53, which rounds to even - Double: 1 + 2^-52.
        var b = Float80.FromDouble(Math.Pow(2, -53) + Math.Pow(2, -78));
        Assert.AreEqual(1.0, Float80.Add(Float80.FromDouble(1), b, out _).ToDouble(out _));
    }

    [TestMethod]
    public void ACurrency_IsItsTenThousandthsDividedOnce()
    {
        // C23/C24: 1.1@ * 3 is 3.3, 0.3@ / 3 is 0.1 - of the Double nearest the Currency: 3.3000000000000003, 0.09999999999999999.
        Assert.AreEqual(3.3, Float80.Multiply(Float80.FromCurrency(11000), Float80.FromDouble(3), out _).ToDouble(out _));
        Assert.AreEqual(0.1, Float80.Divide(Float80.FromCurrency(3000), Float80.FromDouble(3), out _).ToDouble(out _));
    }

    [TestMethod]
    public void SinglePrecision_KeepsTheExtendedRange()
    {
        // C05: CSng(3E+38 * 10), in 24 bits - not a Single, whose greatest value is 3.4E+38.
        var x = Float80.Multiply(Float80.FromSingle(3E+38f), Float80.FromDouble(10), out _);
        Assert.AreEqual("3.00000008662739E+39", x.RoundToSinglePrecision().ToDouble(out _).ToString("G15", CultureInfo.InvariantCulture));
    }
}
