using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.Reflection;

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

    private static readonly VBType VbByte = VBByteType.TypeInfo, VbBoolean = VBBooleanType.TypeInfo,
        VbInteger = VBIntegerType.TypeInfo, VbLong = VBLongType.TypeInfo, VbLongLong = VBLongLongType.TypeInfo,
        VbSingle = VBSingleType.TypeInfo, VbDouble = VBDoubleType.TypeInfo, VbCurrency = VBCurrencyType.TypeInfo,
        VbDecimal = VBDecimalType.TypeInfo, VbDate = VBDateType.TypeInfo, VbString = VBStringType.TypeInfo,
        VbEmpty = VBEmptyType.TypeInfo, VbNull = VBNullType.TypeInfo;

    public static IEnumerable<object[]> Grid()
    {
        yield return [VbByte, VbByte, VbByte];
        yield return [VbByte, VbEmpty, VbByte];
        yield return [VbEmpty, VbByte, VbByte];

        yield return [VbBoolean, VbBoolean, VbInteger];
        yield return [VbBoolean, VbByte, VbInteger];
        yield return [VbByte, VbBoolean, VbInteger];
        yield return [VbBoolean, VbInteger, VbInteger];
        yield return [VbInteger, VbBoolean, VbInteger];
        yield return [VbInteger, VbInteger, VbInteger];
        yield return [VbInteger, VbEmpty, VbInteger];
        yield return [VbEmpty, VbInteger, VbInteger];
        yield return [VbBoolean, VbEmpty, VbInteger];
        yield return [VbEmpty, VbBoolean, VbInteger];
        yield return [VbEmpty, VbEmpty, VbInteger];

        yield return [VbLong, VbByte, VbLong];
        yield return [VbByte, VbLong, VbLong];
        yield return [VbLong, VbBoolean, VbLong];
        yield return [VbLong, VbInteger, VbLong];
        yield return [VbInteger, VbLong, VbLong];
        yield return [VbLong, VbLong, VbLong];
        yield return [VbLong, VbEmpty, VbLong];
        yield return [VbEmpty, VbLong, VbLong];

        yield return [VbLongLong, VbInteger, VbLongLong];
        yield return [VbInteger, VbLongLong, VbLongLong];
        yield return [VbLongLong, VbLong, VbLongLong];
        yield return [VbLong, VbLongLong, VbLongLong];
        yield return [VbLongLong, VbLongLong, VbLongLong];
        yield return [VbLongLong, VbEmpty, VbLongLong];
        yield return [VbEmpty, VbLongLong, VbLongLong];

        yield return [VbSingle, VbByte, VbSingle];
        yield return [VbByte, VbSingle, VbSingle];
        yield return [VbSingle, VbBoolean, VbSingle];
        yield return [VbSingle, VbInteger, VbSingle];
        yield return [VbInteger, VbSingle, VbSingle];
        yield return [VbSingle, VbSingle, VbSingle];
        yield return [VbSingle, VbEmpty, VbSingle];
        yield return [VbEmpty, VbSingle, VbSingle];
        yield return [VbSingle, VbLong, VbDouble];
        yield return [VbLong, VbSingle, VbDouble];
        yield return [VbSingle, VbLongLong, VbDouble];
        yield return [VbLongLong, VbSingle, VbDouble];

        yield return [VbDouble, VbInteger, VbDouble];
        yield return [VbInteger, VbDouble, VbDouble];
        yield return [VbDouble, VbDouble, VbDouble];
        yield return [VbDouble, VbString, VbDouble];
        yield return [VbString, VbDouble, VbDouble];
        yield return [VbDouble, VbEmpty, VbDouble];
        yield return [VbEmpty, VbDouble, VbDouble];
        yield return [VbString, VbInteger, VbDouble];
        yield return [VbInteger, VbString, VbDouble];
        yield return [VbString, VbEmpty, VbDouble];

        yield return [VbCurrency, VbInteger, VbCurrency];
        yield return [VbInteger, VbCurrency, VbCurrency];
        yield return [VbCurrency, VbCurrency, VbCurrency];
        yield return [VbCurrency, VbString, VbCurrency];
        yield return [VbString, VbCurrency, VbCurrency];
        yield return [VbCurrency, VbEmpty, VbCurrency];
        yield return [VbEmpty, VbCurrency, VbCurrency];
        yield return [VbCurrency, VbDouble, VbCurrency];

        // MS-VBAL 5.6.9.3 explicitly lists Currency as a Date runtime-semantics partner in both
        // directions, alongside every other numeric/string/empty combination:
        yield return [VbDate, VbInteger, VbDate];
        yield return [VbInteger, VbDate, VbDate];
        yield return [VbDate, VbDate, VbDate];
        yield return [VbDate, VbString, VbDate];
        yield return [VbString, VbDate, VbDate];
        yield return [VbDate, VbEmpty, VbDate];
        yield return [VbEmpty, VbDate, VbDate];
        yield return [VbDate, VbCurrency, VbDate];
        yield return [VbCurrency, VbDate, VbDate];

        // Decimal only ever reaches this table via a Variant holding a CDec value, but MS-VBAL
        // still specifies its own effective type here (not Currency's) and lists Date as a partner:
        yield return [VbDecimal, VbInteger, VbDecimal];
        yield return [VbInteger, VbDecimal, VbDecimal];
        yield return [VbDecimal, VbDecimal, VbDecimal];
        yield return [VbDecimal, VbString, VbDecimal];
        yield return [VbString, VbDecimal, VbDecimal];
        yield return [VbDecimal, VbEmpty, VbDecimal];
        yield return [VbEmpty, VbDecimal, VbDecimal];
        yield return [VbDecimal, VbCurrency, VbDecimal];
        yield return [VbCurrency, VbDecimal, VbDecimal];
        yield return [VbDecimal, VbDate, VbDecimal];
        yield return [VbDate, VbDecimal, VbDecimal];

        yield return [VbNull, VbInteger, VbNull];
        yield return [VbInteger, VbNull, VbNull];
        yield return [VbNull, VbDouble, VbNull];
        yield return [VbDouble, VbNull, VbNull];
        yield return [VbNull, VbCurrency, VbNull];
        yield return [VbCurrency, VbNull, VbNull];
        yield return [VbNull, VbDecimal, VbNull];
        yield return [VbDecimal, VbNull, VbNull];
        yield return [VbNull, VbDate, VbNull];
        yield return [VbDate, VbNull, VbNull];
        yield return [VbNull, VbString, VbNull];
        yield return [VbString, VbNull, VbNull];
        yield return [VbNull, VbEmpty, VbNull];
        yield return [VbEmpty, VbNull, VbNull];
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
}
