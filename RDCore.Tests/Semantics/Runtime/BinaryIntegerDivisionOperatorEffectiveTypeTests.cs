using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for the <c>\</c> and <c>Mod</c> operator-specific effective-type table
/// (MS-VBAL §5.6.9.3.6, runtime semantics) that overrides the general binary arithmetic table.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.2.1 Operator Evaluation")]
[TestCategory("MS-VBAL 5.6.9.3.6 \\ Operator and Mod Operator (runtime semantics)")]
public sealed class BinaryIntegerDivisionOperatorEffectiveTypeTests : OperatorArithmeticRuntimeSemanticsTests
{
    // '\' and 'Mod' share the same effective-type determination; either vehicle exercises it.
    private static BinaryIntegerDivisionOperatorRuntimeSemantics Op() => new(FakeProvider(), Formatter());

    [TestMethod]
    public void ResolvesEffectiveValueType_AcrossTheOperandGrid()
    {
        VBType b = VBByteType.TypeInfo, boo = VBBooleanType.TypeInfo, i = VBIntegerType.TypeInfo,
            l = VBLongType.TypeInfo, ll = VBLongLongType.TypeInfo, s = VBSingleType.TypeInfo,
            d = VBDoubleType.TypeInfo, cur = VBCurrencyType.TypeInfo, dec = VBDecimalType.TypeInfo,
            dt = VBDateType.TypeInfo, str = VBStringType.TypeInfo, e = VBEmptyType.TypeInfo;

        (VBType lhs, VBType rhs, VBType expected)[] grid =
        [
            // this pair alone used to fail: the effective-type override read its own left operand's
            // type in place of the right operand's, so this row could never match and always fell
            // through to the base table's (wrong, for '\'/'Mod') Byte-stays-Byte rule instead.
            (b, e, i), (e, b, i),

            (boo, s, i), (boo, d, i), (boo, str, i), (boo, cur, i), (boo, dt, i), (boo, dec, i),

            // row 3 (Boolean/Integer, Single/Double/…) is asymmetric in MS-VBAL — there is no
            // mirrored "RHS is Boolean/Integer" row, so Integer \ Single lands here (Integer), while
            // Single \ Integer falls through to the general rule below (Long).
            (i, s, i),

            (s, i, l), (str, l, l), (dt, cur, l),
            (l, str, l), (cur, dt, l),

            (ll, i, ll), (i, ll, ll), (ll, str, ll), (str, ll, ll), (ll, e, ll), (e, ll, ll),
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
