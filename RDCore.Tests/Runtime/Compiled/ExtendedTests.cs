using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;

namespace RDCore.Tests.Runtime.Compiled;

/// <summary>
/// <see cref="Extended"/> against the interpreter's extended intermediates (ExtendedIntermediates): an operation computed in <c>Double</c> when its result
/// is exact must give what the 80-bit evaluation gives, stored in every floating-point kind and compared, and raise what it raises.
/// </summary>
[TestClass]
public sealed class ExtendedTests
{
    private static readonly double[] Edges =
    [
        0.0, -0.0, 1, -1, 0.5, 0.1, 0.2, 0.3, 3, 10, 1e15, 1e16, 9007199254740992, 9007199254740993, 1e300, -1e300, 1.7976931348623157e308, -1.7976931348623157e308,
        4.9e-324, 2.2250738585072014e-308, 1e-310, 4.0083367200179456e-292, 1.1754943508222875e-38, 3.4028234663852886e38, 3.4028235677973366e38, 16777216,
        16777217, 2.5, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.0 / 3, 2.0 / 3, Math.PI, 1e-5, 12345.678,
    ];

    private static IEnumerable<(double Left, double Right)> Pairs()
    {
        foreach (var left in Edges)
        {
            foreach (var right in Edges)
            {
                yield return (left, right);
            }
        }

        var random = new Random(20261028);
        for (var i = 0; i < 20000; i++)
        {
            yield return (Draw(random), Draw(random));
        }
    }

    // a mix of whole numbers, which sum exactly, of short decimals, and of values of any magnitude.
    private static double Draw(Random random) => random.Next(4) switch
    {
        0 => random.Next(-1_000_000, 1_000_000),
        1 => Math.Round(random.NextDouble() * 1000, 3),
        2 => BitConverter.Int64BitsToDouble(random.NextInt64()),
        _ => random.NextDouble() * Math.Pow(2, random.Next(-1074, 1023)) * (random.Next(2) == 0 ? 1 : -1),
    };

    private static readonly (string Token, Func<Extended, Extended, Extended> Compiled)[] Operations =
    [
        (Tokens.AdditionOp, Extended.Add),
        (Tokens.SubtractionOp, Extended.Subtract),
        (Tokens.MultiplicationOp, Extended.Multiply),
        (Tokens.DivisionOp, Extended.Divide),
    ];

    private static readonly ScalarKind[] FloatingKinds = [ScalarKind.Double, ScalarKind.Single, ScalarKind.Date];

    [TestMethod]
    public void AnOperationOfTwoDoubles_StoresAndComparesAsTheExtendedFormatDoes()
    {
        foreach (var (left, right) in Pairs())
        {
            foreach (var (token, compiled) in Operations)
            {
                var result = compiled(Extended.OfFloating(left), Extended.OfFloating(right));
                var expected = ExtendedIntermediates.Operate(token, ExtendedIntermediates.Widened(new ScalarPayload(VBDoubleType.TypeInfo, 0, left), VBDoubleType.TypeInfo),
                    ExtendedIntermediates.Widened(new ScalarPayload(VBDoubleType.TypeInfo, 0, right), VBDoubleType.TypeInfo), VBDoubleType.TypeInfo);

                foreach (var kind in FloatingKinds)
                {
                    AssertStoredAlike(expected, kind, Extended.ToFloating(result, kind, out var stored), stored, $"{left:R} {token} {right:R} as {kind}");
                }

                AssertStoredIntegralAlike(expected, Extended.ToIntegral(result, ScalarKind.Long, out var integral), integral, $"{left:R} {token} {right:R} as Long");
                AssertComparedAlike(result, expected, $"{left:R} {token} {right:R} > 0");
            }
        }
    }

    [TestMethod]
    public void AWholeNumber_IsWidenedExactly()
    {
        foreach (var value in new long[] { 0, 1, -1, 1L << 53, -(1L << 53), (1L << 53) + 1, long.MaxValue, long.MinValue, int.MaxValue, 123456789 })
        {
            var expected = ExtendedIntermediates.Widened(new ScalarPayload(VBLongLongType.TypeInfo, value, 0), VBDoubleType.TypeInfo);
            AssertStoredAlike(expected, ScalarKind.Double, Extended.ToFloating(Extended.OfIntegral(ScalarKind.LongLong, value), ScalarKind.Double, out var stored), stored,
                $"{value}");
        }
    }

    [TestMethod]
    public void ACurrency_IsWidenedAsItsTenThousandthsDividedOnce()
    {
        var expected = ExtendedIntermediates.Widened(new ScalarPayload(VBCurrencyType.TypeInfo, 12345, 0), VBDoubleType.TypeInfo);
        AssertStoredAlike(expected, ScalarKind.Double, Extended.ToFloating(Extended.OfIntegral(ScalarKind.Currency, 12345), ScalarKind.Double, out var stored), stored, "1.2345@");
    }

    private static void AssertStoredAlike(WideValue expected, ScalarKind kind, PayloadStatus status, double stored, string what)
    {
        var converted = ExtendedIntermediates.Convert(expected, ScalarKinds.TypeOf(kind), out var error);
        if (error is { } raised)
        {
            Assert.AreEqual(raised is VBRuntimeErrorId.DivisionByZero ? PayloadStatus.DivisionByZero : PayloadStatus.Overflow, status, what);
            return;
        }

        Assert.AreEqual(PayloadStatus.Ok, status, what);
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(converted!.Value.Floating), BitConverter.DoubleToInt64Bits(stored), what);
    }

    private static void AssertStoredIntegralAlike(WideValue expected, PayloadStatus status, long stored, string what)
    {
        var converted = ExtendedIntermediates.Convert(expected, VBLongType.TypeInfo, out var error);
        if (error is not null || converted is null)
        {
            Assert.AreNotEqual(PayloadStatus.Ok, status, what);
            return;
        }

        Assert.AreEqual(PayloadStatus.Ok, status, what);
        Assert.AreEqual(converted.Value.Integral, stored, what);
    }

    private static void AssertComparedAlike(Extended result, WideValue expected, string what)
    {
        var zero = ExtendedIntermediates.Widened(new ScalarPayload(VBDoubleType.TypeInfo, 0, 0), VBDoubleType.TypeInfo);
        foreach (var kind in new[] { ScalarKind.Double, ScalarKind.Single })
        {
            var type = ScalarKinds.TypeOf(kind);
            var isTrue = ExtendedIntermediates.Compare(Tokens.CompareGreaterThanOp, expected with { Type = type }, zero with { Type = type }, type, out var error);
            var status = Extended.Compare(ComparisonOperator.Greater, result, Extended.OfFloating(0), kind, out var compared);
            if (error is not null || isTrue is null)
            {
                Assert.AreNotEqual(PayloadStatus.Ok, status, what);
                continue;
            }

            Assert.AreEqual(PayloadStatus.Ok, status, what);
            Assert.AreEqual(isTrue.Value, compared, what);
        }
    }
}
