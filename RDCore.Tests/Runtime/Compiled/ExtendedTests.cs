using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Operators;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.Runtime.LanguageCore.Errors;

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
                AssertOperatedAlike(compiled(Extended.OfFloating(left), Extended.OfFloating(right)), Operated(token, Widened(left), Widened(right)),
                    $"{left:R} {token} {right:R}");
            }
        }
    }

    [TestMethod]
    public void AnInexactSumOrProductNearAMidpoint_IsStoredAsTheExtendedFormatRoundsItTwice()
    {
        // the rows where the 64-bit rounding lands on a midpoint between two Doubles, which the store rounds to even, away from the nearest Double.
        var roundedTwice = new Dictionary<string, int> { [Tokens.AdditionOp] = 0, [Tokens.SubtractionOp] = 0, [Tokens.MultiplicationOp] = 0 };
        foreach (var (left, right) in NearMidpoints())
        {
            foreach (var (token, compiled) in Operations.Where(operation => roundedTwice.ContainsKey(operation.Token)))
            {
                var expected = Operated(token, Widened(left), Widened(right));
                AssertOperatedAlike(compiled(Extended.OfFloating(left), Extended.OfFloating(right)), expected, $"{left:R} {token} {right:R}");

                var nearest = token switch { Tokens.AdditionOp => left + right, Tokens.SubtractionOp => left - right, _ => left * right };
                if (double.IsFinite(nearest) && BitConverter.DoubleToInt64Bits(expected.Value.ToDouble(out _)) != BitConverter.DoubleToInt64Bits(nearest))
                {
                    roundedTwice[token]++;
                }
            }
        }

        foreach (var (token, count) in roundedTwice)
        {
            Assert.IsTrue(count > 100, $"{token}: {count} rows rounded twice");
        }
    }

    [TestMethod]
    public void AnInexactResult_IsWidenedAsTheOperationRoundsIt()
    {
        var random = new Random(20261116);
        var operands = Pairs().Take(Edges.Length * Edges.Length + 2000).Concat(NearMidpoints().Where(_ => random.Next(8) == 0));
        foreach (var (left, right) in operands)
        {
            foreach (var (token, compiled) in Operations)
            {
                var result = compiled(Extended.OfFloating(left), Extended.OfFloating(right));
                var expected = Operated(token, Widened(left), Widened(right));
                var what = $"({left:R} {token} {right:R})";
                Assert.AreEqual(expected.Value, result.AsWide(ScalarKind.Double).Value, what);
                Assert.AreEqual(expected.Pending, result.AsWide(ScalarKind.Double).Pending, what);

                AssertOperatedAlike(Extended.Negate(result), ExtendedIntermediates.Negate(expected, VBDoubleType.TypeInfo), $"-{what}");
                foreach (var function in new[] { "IStdConversionModule.Int/1", "IStdConversionModule.CDbl/1" })
                {
                    AssertOperatedAlike(Extended.Apply(function, result), ExtendedIntermediates.Apply(function, expected)!.Value, $"{function} {what}");
                }

                foreach (var next in new[] { 0.1, 3, -1e-5, 36526 })
                {
                    foreach (var (nextToken, nextCompiled) in Operations)
                    {
                        AssertOperatedAlike(nextCompiled(result, Extended.OfFloating(next)), Operated(nextToken, expected, Widened(next)), $"{what} {nextToken} {next:R}");
                    }
                }
            }
        }
    }

    // sums whose exact value lies near a midpoint between two Doubles, at magnitudes of every kind (a Date's serial among them), from a unit's 2^-2 to 2^-53
    // away; and products of two Doubles 1 + i * 2^-52 and 1 + j * 2^-52 whose i * j is near 2^51 (the product's half unit), scaled by powers of two.
    private static IEnumerable<(double Left, double Right)> NearMidpoints()
    {
        var random = new Random(20261116);
        for (var i = 0; i < 4000; i++)
        {
            var nearest = random.Next(3) switch
            {
                0 => 36526 + random.NextDouble() * 10000,
                1 => random.NextDouble() * 1e6,
                _ => (1 + random.NextDouble()) * Math.Pow(2, random.Next(-960, 1020)),
            };

            var unit = Math.BitIncrement(nearest) - nearest;
            var distance = Math.ScaleB(unit, -random.Next(2, 54)) * random.Next(-1, 2);
            var sign = random.Next(2) == 0 ? 1 : -1;
            yield return (sign * nearest, sign * (random.Next(2) == 0 ? 1 : -1) * (unit / 2 + distance));
        }

        // i * j = 2^51 + 2^bits * r2 + 2^(51 - bits) * r1 + r1 * r2: within 2^-26 to 2^-9 of a half unit, through the band where the 64-bit
        // rounding lands on it.
        for (var i = 0; i < 4000; i++)
        {
            var bits = random.Next(20, 32);
            var spread = 1 << random.Next(0, 17);
            var left = 1 + Math.ScaleB((1L << bits) + random.Next(-spread, spread + 1), -52);
            var right = 1 + Math.ScaleB((1L << (51 - bits)) + random.Next(-spread, spread + 1), -52);
            var scale = random.Next(3) == 0 ? (0, 15) : (random.Next(-450, 450), random.Next(-450, 450));
            yield return (Math.ScaleB(left, scale.Item1) * (random.Next(2) == 0 ? 1 : -1), Math.ScaleB(right, scale.Item2) * (random.Next(2) == 0 ? 1 : -1));
        }
    }

    private static WideValue Widened(double value) => ExtendedIntermediates.Widened(new ScalarPayload(VBDoubleType.TypeInfo, 0, value), VBDoubleType.TypeInfo);

    private static WideValue Operated(string token, WideValue left, WideValue right) => ExtendedIntermediates.Operate(token, left, right, VBDoubleType.TypeInfo);

    // stored in each floating-point kind and in a Long, and compared, as the interpreter's extended value is.
    private static void AssertOperatedAlike(Extended result, WideValue expected, string what)
    {
        foreach (var kind in FloatingKinds)
        {
            AssertStoredAlike(expected, kind, Extended.ToFloating(result, kind, out var stored), stored, $"{what} as {kind}");
        }

        AssertStoredIntegralAlike(expected, Extended.ToIntegral(result, ScalarKind.Long, out var integral), integral, $"{what} as Long");
        AssertComparedAlike(result, expected, $"{what} > 0");
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
