using RDCore.Runtime.Semantics.Operators.Logical;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.Reflection;

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

    private static readonly VBType VbByte = VBByteType.TypeInfo, VbBoolean = VBBooleanType.TypeInfo,
        VbInteger = VBIntegerType.TypeInfo, VbLong = VBLongType.TypeInfo, VbLongLong = VBLongLongType.TypeInfo,
        VbSingle = VBSingleType.TypeInfo, VbDouble = VBDoubleType.TypeInfo, VbCurrency = VBCurrencyType.TypeInfo,
        VbDecimal = VBDecimalType.TypeInfo, VbDate = VBDateType.TypeInfo, VbString = VBStringType.TypeInfo,
        VbEmpty = VBEmptyType.TypeInfo, VbNull = VBNullType.TypeInfo;

    public static IEnumerable<object[]> Grid()
    {
        yield return [VbByte, VbByte, VbByte];
        yield return [VbByte, VbNull, VbByte];
        yield return [VbNull, VbByte, VbByte];
        yield return [VbBoolean, VbBoolean, VbBoolean];
        yield return [VbBoolean, VbNull, VbBoolean];
        yield return [VbNull, VbBoolean, VbBoolean];
        yield return [VbByte, VbInteger, VbInteger];
        yield return [VbInteger, VbInteger, VbInteger];
        yield return [VbBoolean, VbInteger, VbInteger];
        yield return [VbInteger, VbBoolean, VbInteger];
        yield return [VbBoolean, VbByte, VbInteger];
        yield return [VbByte, VbBoolean, VbInteger];
        yield return [VbInteger, VbEmpty, VbInteger];
        yield return [VbEmpty, VbEmpty, VbInteger];
        yield return [VbNull, VbInteger, VbInteger];
        yield return [VbInteger, VbNull, VbInteger];
        yield return [VbEmpty, VbNull, VbInteger];
        yield return [VbInteger, VbLong, VbLong];
        yield return [VbLong, VbInteger, VbLong];
        yield return [VbLong, VbLong, VbLong];
        yield return [VbSingle, VbSingle, VbLong];
        yield return [VbDouble, VbDouble, VbLong];
        yield return [VbDouble, VbInteger, VbLong];
        yield return [VbSingle, VbInteger, VbLong];
        yield return [VbInteger, VbSingle, VbLong];
        yield return [VbCurrency, VbCurrency, VbLong];
        yield return [VbDecimal, VbInteger, VbLong];
        yield return [VbInteger, VbDecimal, VbLong];
        yield return [VbDate, VbLong, VbLong];
        yield return [VbDate, VbInteger, VbLong];
        yield return [VbString, VbInteger, VbLong];
        yield return [VbInteger, VbString, VbLong];
        yield return [VbSingle, VbNull, VbLong];
        yield return [VbNull, VbDouble, VbLong];
        yield return [VbInteger, VbLongLong, VbLongLong];
        yield return [VbLongLong, VbInteger, VbLongLong];
        yield return [VbLongLong, VbDouble, VbLongLong];
        yield return [VbLongLong, VbDate, VbLongLong];
        yield return [VbLongLong, VbNull, VbLongLong];
        yield return [VbNull, VbLongLong, VbLongLong];
        yield return [VbNull, VbNull, VbNull];
    }

    public static string GetTestName(MethodInfo method, object[] data)
        => $"({((VBType)data[0]).Name}, {((VBType)data[1]).Name}):{((VBType)data[2]).Name}";

    [TestMethod]
    [DynamicData(nameof(Grid), DynamicDataDisplayName = nameof(GetTestName))]
    public void ResolvesEffectiveValueType(VBType lhs, VBType rhs, VBType expected)
    {
        var result = DetermineEffectiveType(Op(), lhs, rhs);
        Assert.IsTrue(result.IsApplicable, $"({lhs.Name}, {rhs.Name}) expected {expected.Name}, got type mismatch");
        Assert.AreEqual(expected, result.Result);
    }

    [TestMethod]
    public void NonNumericNonNullOperand_IsTypeMismatch()
    {
        var result = DetermineEffectiveType(Op(), VBObjectType.TypeInfo, VBLongType.TypeInfo);
        Assert.IsNotNull(result.ErrorInfo);
    }
}
