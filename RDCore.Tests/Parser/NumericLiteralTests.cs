using RDCore.Parsing.Syntax;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Parser;

/// <summary>
/// The shared numeric-literal resolver — MS-VBAL §3.3.2 / RD-VBAL §3.2.0.1. One resolver for the
/// declaration pass and the conditional-compilation pass.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §3.2.0 Literal Expressions")]
public sealed class NumericLiteralTests
{
    private static (Type Type, decimal? Value, bool Overflow) Resolve(string token)
    {
        var (value, overflow) = NumericLiteral.Resolve(token);
        decimal? magnitude = value switch
        {
            VBIntegerValue i => i.Value,
            VBLongValue l => l.Value,
            VBLongLongValue ll => ll.Value,
            VBSingleValue s => (decimal)s.Value,
            VBDoubleValue d => double.IsFinite(d.Value) && Math.Abs(d.Value) < 7.9e28 ? (decimal)d.Value : null,
            VBCurrencyValue c => c.Value.StoredValue,
            _ => null,
        };
        return (value.GetType(), magnitude, overflow);
    }

    [TestMethod]
    // type-declaration character forces the type
    [DataRow("1%", typeof(VBIntegerValue), 1)]
    [DataRow("1&", typeof(VBLongValue), 1)]
    [DataRow("1^", typeof(VBLongLongValue), 1)]
    [DataRow("1!", typeof(VBSingleValue), 1)]
    [DataRow("1#", typeof(VBDoubleValue), 1)]
    [DataRow("1@", typeof(VBCurrencyValue), null)]
    // unsuffixed decimal integer — smallest of Integer / Long / Double
    [DataRow("32767", typeof(VBIntegerValue), 32767)]
    [DataRow("32768", typeof(VBLongValue), 32768)]
    [DataRow("2147483647", typeof(VBLongValue), 2147483647)]
    [DataRow("2147483648", typeof(VBDoubleValue), null)]
    // unsuffixed floating-point — always Double (D and E exponents equal)
    [DataRow("1.5", typeof(VBDoubleValue), null)]
    [DataRow("1.5e3", typeof(VBDoubleValue), 1500)]
    [DataRow("1.5d3", typeof(VBDoubleValue), 1500)]
    [DataRow("15D2", typeof(VBDoubleValue), 1500)]
    // hexadecimal — typed by bit width, two's-complement
    [DataRow("&HFF", typeof(VBIntegerValue), 255)]
    [DataRow("&hff", typeof(VBIntegerValue), 255)]
    [DataRow("&HFFFF", typeof(VBIntegerValue), -1)]
    [DataRow("&H8000", typeof(VBIntegerValue), -32768)]
    [DataRow("&H10000", typeof(VBLongValue), 65536)]
    [DataRow("&HFFFFFFFF", typeof(VBLongValue), -1)]
    [DataRow("&HFFFF%", typeof(VBIntegerValue), -1)]
    [DataRow("&HFFFFFFFF&", typeof(VBLongValue), -1)]
    [DataRow("&HFFFFFFFFFFFFFFFF^", typeof(VBLongLongValue), -1)]
    // octal — same width rule (&O37777777777 == 0xFFFFFFFF)
    [DataRow("&O777", typeof(VBIntegerValue), 511)]
    [DataRow("&O37777777777", typeof(VBLongValue), -1)]
    public void ResolvesTypeAndValue(string token, Type expectedType, int? expectedValue)
    {
        var (type, value, overflow) = Resolve(token);

        Assert.IsFalse(overflow, $"{token} unexpectedly overflowed");
        Assert.AreEqual(expectedType, type);
        if (expectedValue is int expected)
        {
            Assert.AreEqual((decimal)expected, value);
        }
    }

    [TestMethod]
    // MS-VBAL §3.3.2: a value that does not fit its forced or inferred type is an overflow.
    [DataRow("32768%", DisplayName = "Integer suffix overflow")]
    [DataRow("2147483648&", DisplayName = "Long suffix overflow")]
    [DataRow("99999999999999999999^", DisplayName = "LongLong suffix overflow")]
    [DataRow("1E400", DisplayName = "unsuffixed float overflows to infinity")]
    [DataRow("1E400#", DisplayName = "Double suffix overflows to infinity")]
    [DataRow("1E40!", DisplayName = "Single suffix overflows to infinity")]
    [DataRow("&H100000000", DisplayName = "hex literal exceeds 32 bits without a ^ suffix")]
    [DataRow("&O777777777777", DisplayName = "octal literal exceeds 32 bits without a ^ suffix")]
    public void FlagsOverflow_AndStillYieldsAnUnknownValue(string token)
    {
        var (value, overflow) = NumericLiteral.Resolve(token);

        Assert.IsTrue(overflow);
        Assert.IsInstanceOfType<VBUnknownValue>(value);
    }

    [TestMethod]
    public void UnsuffixedIntegerPastUInt64_WidensToDouble()
    {
        var (value, overflow) = NumericLiteral.Resolve("999999999999999999999999999");
        Assert.IsFalse(overflow);
        Assert.IsInstanceOfType<VBDoubleValue>(value);
    }

    [TestMethod]
    // `Const N = -1` — VBA has no negative-literal token; the caller negates the resolved value.
    // Negate keeps the MS-VBAL type and flips the sign for every numeric intrinsic Resolve produces.
    [DataRow("1", typeof(VBIntegerValue), -1)]
    [DataRow("32768", typeof(VBLongValue), -32768)]
    [DataRow("1!", typeof(VBSingleValue), -1)]
    [DataRow("1#", typeof(VBDoubleValue), -1)]
    [DataRow("1^", typeof(VBLongLongValue), -1)]
    [DataRow("&HFF", typeof(VBIntegerValue), -255)]
    [DataRow("1@", typeof(VBCurrencyValue), null)]
    [DataRow("2147483648", typeof(VBDoubleValue), null)]
    public void Negate_FlipsTheSignAndKeepsTheType(string token, Type expectedType, int? expectedValue)
    {
        var (resolved, overflow) = NumericLiteral.Resolve(token);
        Assert.IsFalse(overflow);

        var negated = NumericLiteral.Negate(resolved);

        Assert.IsNotNull(negated);
        Assert.AreEqual(expectedType, negated!.GetType());
        if (expectedValue is int expected)
        {
            decimal? magnitude = negated switch
            {
                VBIntegerValue i => i.Value,
                VBLongValue l => l.Value,
                VBLongLongValue ll => ll.Value,
                VBSingleValue s => (decimal)s.Value,
                VBDoubleValue d => (decimal)d.Value,
                _ => null,
            };
            Assert.AreEqual((decimal)expected, magnitude);
        }
    }

    [TestMethod]
    public void Negate_ReturnsNull_ForANonNumericValue()
        => Assert.IsNull(NumericLiteral.Negate(VBUnknownValue.DefaultValue));
}
