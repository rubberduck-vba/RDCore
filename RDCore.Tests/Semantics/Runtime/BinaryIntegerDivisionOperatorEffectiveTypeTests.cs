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
        VBType vbByte = VBByteType.TypeInfo, vbBoolean = VBBooleanType.TypeInfo, vbInteger = VBIntegerType.TypeInfo,
            vbLong = VBLongType.TypeInfo, vbLongLong = VBLongLongType.TypeInfo, vbSingle = VBSingleType.TypeInfo,
            vbDouble = VBDoubleType.TypeInfo, vbCurrency = VBCurrencyType.TypeInfo, vbDecimal = VBDecimalType.TypeInfo,
            vbDate = VBDateType.TypeInfo, vbString = VBStringType.TypeInfo, vbEmpty = VBEmptyType.TypeInfo;

        (VBType lhs, VBType rhs, VBType expected)[] grid =
        [
            // this pair alone used to fail: the effective-type override read its own left operand's
            // type in place of the right operand's, so this row could never match and always fell
            // through to the base table's (wrong, for '\'/'Mod') Byte-stays-Byte rule instead.
            (vbByte, vbEmpty, vbInteger), (vbEmpty, vbByte, vbInteger),

            (vbBoolean, vbSingle, vbInteger), (vbBoolean, vbDouble, vbInteger), (vbBoolean, vbString, vbInteger), (vbBoolean, vbCurrency, vbInteger), (vbBoolean, vbDate, vbInteger), (vbBoolean, vbDecimal, vbInteger),

            // row 3 (Boolean/Integer, Single/Double/…) is asymmetric in MS-VBAL — there is no
            // mirrored "RHS is Boolean/Integer" row, so Integer \ Single lands here (Integer), while
            // Single \ Integer falls through to the general rule below (Long).
            (vbInteger, vbSingle, vbInteger),

            (vbSingle, vbInteger, vbLong), (vbString, vbLong, vbLong), (vbDate, vbCurrency, vbLong),
            (vbLong, vbString, vbLong), (vbCurrency, vbDate, vbLong),

            (vbLongLong, vbInteger, vbLongLong), (vbInteger, vbLongLong, vbLongLong), (vbLongLong, vbString, vbLongLong), (vbString, vbLongLong, vbLongLong), (vbLongLong, vbEmpty, vbLongLong), (vbEmpty, vbLongLong, vbLongLong),
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
