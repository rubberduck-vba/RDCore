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
        VBType vbByte = VBByteType.TypeInfo, vbBoolean = VBBooleanType.TypeInfo, vbInteger = VBIntegerType.TypeInfo,
            vbLong = VBLongType.TypeInfo, vbLongLong = VBLongLongType.TypeInfo, vbSingle = VBSingleType.TypeInfo,
            vbDouble = VBDoubleType.TypeInfo, vbCurrency = VBCurrencyType.TypeInfo, vbDecimal = VBDecimalType.TypeInfo,
            vbDate = VBDateType.TypeInfo, vbString = VBStringType.TypeInfo, vbEmpty = VBEmptyType.TypeInfo,
            vbNull = VBNullType.TypeInfo;

        (VBType lhs, VBType rhs, VBType expected)[] grid =
        [
            (vbByte, vbByte, vbByte), (vbByte, vbEmpty, vbByte), (vbEmpty, vbByte, vbByte),

            (vbBoolean, vbBoolean, vbInteger), (vbBoolean, vbByte, vbInteger), (vbByte, vbBoolean, vbInteger), (vbBoolean, vbInteger, vbInteger), (vbInteger, vbBoolean, vbInteger), (vbInteger, vbInteger, vbInteger), (vbInteger, vbEmpty, vbInteger), (vbEmpty, vbInteger, vbInteger), (vbBoolean, vbEmpty, vbInteger), (vbEmpty, vbBoolean, vbInteger),
            (vbEmpty, vbEmpty, vbInteger),

            (vbLong, vbByte, vbLong), (vbByte, vbLong, vbLong), (vbLong, vbBoolean, vbLong), (vbLong, vbInteger, vbLong), (vbInteger, vbLong, vbLong), (vbLong, vbLong, vbLong), (vbLong, vbEmpty, vbLong), (vbEmpty, vbLong, vbLong),

            (vbLongLong, vbInteger, vbLongLong), (vbInteger, vbLongLong, vbLongLong), (vbLongLong, vbLong, vbLongLong), (vbLong, vbLongLong, vbLongLong), (vbLongLong, vbLongLong, vbLongLong), (vbLongLong, vbEmpty, vbLongLong), (vbEmpty, vbLongLong, vbLongLong),

            (vbSingle, vbByte, vbSingle), (vbByte, vbSingle, vbSingle), (vbSingle, vbBoolean, vbSingle), (vbSingle, vbInteger, vbSingle), (vbInteger, vbSingle, vbSingle), (vbSingle, vbSingle, vbSingle), (vbSingle, vbEmpty, vbSingle), (vbEmpty, vbSingle, vbSingle),
            (vbSingle, vbLong, vbDouble), (vbLong, vbSingle, vbDouble), (vbSingle, vbLongLong, vbDouble), (vbLongLong, vbSingle, vbDouble),

            (vbDouble, vbInteger, vbDouble), (vbInteger, vbDouble, vbDouble), (vbDouble, vbDouble, vbDouble), (vbDouble, vbString, vbDouble), (vbString, vbDouble, vbDouble), (vbDouble, vbEmpty, vbDouble), (vbEmpty, vbDouble, vbDouble), (vbString, vbInteger, vbDouble), (vbInteger, vbString, vbDouble), (vbString, vbEmpty, vbDouble),

            (vbCurrency, vbInteger, vbCurrency), (vbInteger, vbCurrency, vbCurrency), (vbCurrency, vbCurrency, vbCurrency), (vbCurrency, vbString, vbCurrency), (vbString, vbCurrency, vbCurrency), (vbCurrency, vbEmpty, vbCurrency), (vbEmpty, vbCurrency, vbCurrency), (vbCurrency, vbDouble, vbCurrency),

            // MS-VBAL 5.6.9.3 explicitly lists Currency as a Date runtime-semantics partner in both
            // directions, alongside every other numeric/string/empty combination:
            (vbDate, vbInteger, vbDate), (vbInteger, vbDate, vbDate), (vbDate, vbDate, vbDate), (vbDate, vbString, vbDate), (vbString, vbDate, vbDate), (vbDate, vbEmpty, vbDate), (vbEmpty, vbDate, vbDate),
            (vbDate, vbCurrency, vbDate), (vbCurrency, vbDate, vbDate),

            // Decimal only ever reaches this table via a Variant holding a CDec value, but MS-VBAL
            // still specifies its own effective type here (not Currency's) and lists Date as a partner:
            (vbDecimal, vbInteger, vbDecimal), (vbInteger, vbDecimal, vbDecimal), (vbDecimal, vbDecimal, vbDecimal), (vbDecimal, vbString, vbDecimal), (vbString, vbDecimal, vbDecimal), (vbDecimal, vbEmpty, vbDecimal), (vbEmpty, vbDecimal, vbDecimal),
            (vbDecimal, vbCurrency, vbDecimal), (vbCurrency, vbDecimal, vbDecimal), (vbDecimal, vbDate, vbDecimal), (vbDate, vbDecimal, vbDecimal),

            (vbNull, vbInteger, vbNull), (vbInteger, vbNull, vbNull), (vbNull, vbDouble, vbNull), (vbDouble, vbNull, vbNull), (vbNull, vbCurrency, vbNull), (vbCurrency, vbNull, vbNull), (vbNull, vbDecimal, vbNull), (vbDecimal, vbNull, vbNull),
            (vbNull, vbDate, vbNull), (vbDate, vbNull, vbNull), (vbNull, vbString, vbNull), (vbString, vbNull, vbNull), (vbNull, vbEmpty, vbNull), (vbEmpty, vbNull, vbNull), (vbNull, vbNull, vbNull),
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
