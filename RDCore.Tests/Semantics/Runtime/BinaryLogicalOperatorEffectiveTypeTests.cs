using RDCore.Runtime.Semantics.Operators.Logical;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Pins step 1 of the logical-operator pipeline: the effective value type resolved from operand value
/// types, per <strong>MS-VBAL §5.6.9.8</strong> (binary). The effective type is always
/// <c>Byte</c>/<c>Boolean</c>/<c>Integer</c>/<c>Long</c>/<c>LongLong</c>/<c>Null</c> — floating-,
/// fixed-point and <c>Date</c> operands resolve to <c>Long</c> (or <c>LongLong</c>), never to their
/// own type.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.2.1 Operator Evaluation")]
[TestCategory("MS-VBAL 5.6.9.8 Logical Operators (runtime semantics)")]
public sealed class BinaryLogicalOperatorEffectiveTypeTests : OperatorLogicalRuntimeSemanticsTests
{
    private static BinaryAndLogicalOperatorRuntimeSemantics Op() => new(FakeProvider(), Formatter());

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
            (b, b, b),
            (b, n, b), (n, b, b),
            (boo, boo, boo),
            (boo, n, boo), (n, boo, boo),
            (b, i, i), (i, i, i), (boo, i, i), (i, boo, i), (boo, b, i), (b, boo, i),
            (i, e, i), (e, e, i), (n, i, i), (i, n, i), (e, n, i),
            (i, l, l), (l, i, l), (l, l, l),
            (s, s, l), (d, d, l), (d, i, l), (s, i, l), (i, s, l),
            (cur, cur, l), (dec, i, l), (i, dec, l),
            (dt, l, l), (dt, i, l), (str, i, l), (i, str, l),
            (s, n, l), (n, d, l),
            (i, ll, ll), (ll, i, ll), (ll, d, ll), (ll, dt, ll), (ll, n, ll), (n, ll, ll),
            (n, n, n),
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

    [TestMethod]
    public void NonNumericNonNullOperand_IsTypeMismatch()
    {
        var result = DetermineEffectiveType(Op(), VBObjectType.TypeInfo, VBLongType.TypeInfo);
        Assert.IsNotNull(result.ErrorInfo);
    }
}
