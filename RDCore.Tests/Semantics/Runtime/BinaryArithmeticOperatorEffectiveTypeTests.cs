using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for the general binary arithmetic effective-type table shared by every
/// arithmetic operator that has no operator-specific override (MS-VBAL §5.6.9.3, runtime semantics).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.2.1 Operator Evaluation")]
[TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators (runtime semantics)")]
public sealed class BinaryArithmeticOperatorEffectiveTypeTests : OperatorArithmeticRuntimeSemanticsTests
{
    // subtraction has no operator-specific override, so it exercises the shared base table verbatim.
    private static BinarySubtractionOperatorRuntimeSematics Op() => new(FakeProvider(), Formatter());

    [TestMethod]
    public void ResolvesEffectiveValueType_AcrossTheOperandGrid()
    {
        VBType b = VBByteType.TypeInfo, boo = VBBooleanType.TypeInfo, i = VBIntegerType.TypeInfo,
            l = VBLongType.TypeInfo, ll = VBLongLongType.TypeInfo, s = VBSingleType.TypeInfo,
            d = VBDoubleType.TypeInfo, cur = VBCurrencyType.TypeInfo, dec = VBDecimalType.TypeInfo,
            dt = VBDateType.TypeInfo, str = VBStringType.TypeInfo, e = VBEmptyType.TypeInfo,
            n = VBNullType.TypeInfo;

        (VBType lhs, VBType rhs, VBType expected)[] grid =
        [
            (b, b, b), (b, e, b), (e, b, b),

            (boo, boo, i), (boo, b, i), (b, boo, i), (boo, i, i), (i, boo, i), (i, i, i), (i, e, i), (e, i, i), (boo, e, i), (e, boo, i),
            (e, e, i),

            (l, b, l), (b, l, l), (l, boo, l), (l, i, l), (i, l, l), (l, l, l), (l, e, l), (e, l, l),

            (ll, i, ll), (i, ll, ll), (ll, l, ll), (l, ll, ll), (ll, ll, ll), (ll, e, ll), (e, ll, ll),

            (s, b, s), (b, s, s), (s, boo, s), (s, i, s), (i, s, s), (s, s, s), (s, e, s), (e, s, s),
            (s, l, d), (l, s, d), (s, ll, d), (ll, s, d),

            (d, i, d), (i, d, d), (d, d, d), (d, str, d), (str, d, d), (d, e, d), (e, d, d), (str, i, d), (i, str, d), (str, e, d),

            (cur, i, cur), (i, cur, cur), (cur, cur, cur), (cur, str, cur), (str, cur, cur), (cur, e, cur), (e, cur, cur), (cur, d, cur),

            // MS-VBAL 5.6.9.3 explicitly lists Currency as a Date runtime-semantics partner in both
            // directions, alongside every other numeric/string/empty combination:
            (dt, i, dt), (i, dt, dt), (dt, dt, dt), (dt, str, dt), (str, dt, dt), (dt, e, dt), (e, dt, dt),
            (dt, cur, dt), (cur, dt, dt),

            // Decimal only ever reaches this table via a Variant holding a CDec value, but MS-VBAL
            // still specifies its own effective type here (not Currency's) and lists Date as a partner:
            (dec, i, dec), (i, dec, dec), (dec, dec, dec), (dec, str, dec), (str, dec, dec), (dec, e, dec), (e, dec, dec),
            (dec, cur, dec), (cur, dec, dec), (dec, dt, dec), (dt, dec, dec),

            (n, i, n), (i, n, n), (n, d, n), (d, n, n), (n, cur, n), (cur, n, n), (n, dec, n), (dec, n, n),
            (n, dt, n), (dt, n, n), (n, str, n), (str, n, n), (n, e, n), (e, n, n), (n, n, n),
        ];

        var failures = new List<string>();
        foreach (var (lhs, rhs, expected) in grid)
        {
            var result = DetermineEffectiveType(Op(), lhs, rhs);
            if (!result.IsApplicable || !Equals(result.Result, expected))
            {
                failures.Add($"({lhs.Name}, {rhs.Name}) expected {expected.Name}, got {(result.IsApplicable ? result.Result!.Name : "type mismatch")}");
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }
}
