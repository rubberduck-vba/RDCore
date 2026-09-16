using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.Reflection;

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

    private static readonly VBType VbByte = VBByteType.TypeInfo, VbBoolean = VBBooleanType.TypeInfo,
        VbInteger = VBIntegerType.TypeInfo, VbLong = VBLongType.TypeInfo, VbLongLong = VBLongLongType.TypeInfo,
        VbSingle = VBSingleType.TypeInfo, VbDouble = VBDoubleType.TypeInfo, VbCurrency = VBCurrencyType.TypeInfo,
        VbDecimal = VBDecimalType.TypeInfo, VbDate = VBDateType.TypeInfo, VbString = VBStringType.TypeInfo,
        VbEmpty = VBEmptyType.TypeInfo;

    public static IEnumerable<object[]> Grid()
    {
        // this pair alone used to fail: the effective-type override read its own left operand's
        // type in place of the right operand's, so this row could never match and always fell
        // through to the base table's (wrong, for '\'/'Mod') Byte-stays-Byte rule instead.
        yield return [VbByte, VbEmpty, VbInteger];
        yield return [VbEmpty, VbByte, VbInteger];

        yield return [VbBoolean, VbSingle, VbInteger];
        yield return [VbBoolean, VbDouble, VbInteger];
        yield return [VbBoolean, VbString, VbInteger];
        yield return [VbBoolean, VbCurrency, VbInteger];
        yield return [VbBoolean, VbDate, VbInteger];
        yield return [VbBoolean, VbDecimal, VbInteger];

        // row 3 (Boolean/Integer, Single/Double/…) is asymmetric in MS-VBAL — there is no
        // mirrored "RHS is Boolean/Integer" row, so Integer \ Single lands here (Integer), while
        // Single \ Integer falls through to the general rule below (Long).
        yield return [VbInteger, VbSingle, VbInteger];

        yield return [VbSingle, VbInteger, VbLong];
        yield return [VbString, VbLong, VbLong];
        yield return [VbDate, VbCurrency, VbLong];
        yield return [VbLong, VbString, VbLong];
        yield return [VbCurrency, VbDate, VbLong];

        yield return [VbLongLong, VbInteger, VbLongLong];
        yield return [VbInteger, VbLongLong, VbLongLong];
        yield return [VbLongLong, VbString, VbLongLong];
        yield return [VbString, VbLongLong, VbLongLong];
        yield return [VbLongLong, VbEmpty, VbLongLong];
        yield return [VbEmpty, VbLongLong, VbLongLong];
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
}
