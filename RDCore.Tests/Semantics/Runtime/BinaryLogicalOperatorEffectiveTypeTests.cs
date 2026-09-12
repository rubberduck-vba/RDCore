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
        VBType vbByte = VBByteType.TypeInfo, vbBoolean = VBBooleanType.TypeInfo, vbInteger = VBIntegerType.TypeInfo,
            vbLong = VBLongType.TypeInfo, vbLongLong = VBLongLongType.TypeInfo, vbSingle = VBSingleType.TypeInfo,
            vbDouble = VBDoubleType.TypeInfo, vbCurrency = VBCurrencyType.TypeInfo, vbDecimal = VBDecimalType.TypeInfo,
            vbDate = VBDateType.TypeInfo, vbString = VBStringType.TypeInfo, vbEmpty = VBEmptyType.TypeInfo,
            vbNull = VBNullType.TypeInfo;

        (VBType lhs, VBType rhs, VBType expected)[] grid =
        [
            (vbByte, vbByte, vbByte),
            (vbByte, vbNull, vbByte), (vbNull, vbByte, vbByte),
            (vbBoolean, vbBoolean, vbBoolean),
            (vbBoolean, vbNull, vbBoolean), (vbNull, vbBoolean, vbBoolean),
            (vbByte, vbInteger, vbInteger), (vbInteger, vbInteger, vbInteger), (vbBoolean, vbInteger, vbInteger), (vbInteger, vbBoolean, vbInteger), (vbBoolean, vbByte, vbInteger), (vbByte, vbBoolean, vbInteger),
            (vbInteger, vbEmpty, vbInteger), (vbEmpty, vbEmpty, vbInteger), (vbNull, vbInteger, vbInteger), (vbInteger, vbNull, vbInteger), (vbEmpty, vbNull, vbInteger),
            (vbInteger, vbLong, vbLong), (vbLong, vbInteger, vbLong), (vbLong, vbLong, vbLong),
            (vbSingle, vbSingle, vbLong), (vbDouble, vbDouble, vbLong), (vbDouble, vbInteger, vbLong), (vbSingle, vbInteger, vbLong), (vbInteger, vbSingle, vbLong),
            (vbCurrency, vbCurrency, vbLong), (vbDecimal, vbInteger, vbLong), (vbInteger, vbDecimal, vbLong),
            (vbDate, vbLong, vbLong), (vbDate, vbInteger, vbLong), (vbString, vbInteger, vbLong), (vbInteger, vbString, vbLong),
            (vbSingle, vbNull, vbLong), (vbNull, vbDouble, vbLong),
            (vbInteger, vbLongLong, vbLongLong), (vbLongLong, vbInteger, vbLongLong), (vbLongLong, vbDouble, vbLongLong), (vbLongLong, vbDate, vbLongLong), (vbLongLong, vbNull, vbLongLong), (vbNull, vbLongLong, vbLongLong),
            (vbNull, vbNull, vbNull),
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
