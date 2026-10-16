using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Tests.Model.Types;

[TestClass]
[TestCategory("RD-VBAL §2.4 Static Types")]
public sealed class NumericTypeMetadataTests
{
    [TestMethod]
    public void LongLong_IsNamedLongLong_NotLong()
        => Assert.AreEqual("LongLong", VBLongLongType.TypeInfo.Name);

    [TestMethod]
    public void EachNumericType_HasItsOwnName()
    {
        var names = new[]
        {
            VBByteType.TypeInfo.Name, VBIntegerType.TypeInfo.Name, VBLongType.TypeInfo.Name, VBLongLongType.TypeInfo.Name,
            VBSingleType.TypeInfo.Name, VBDoubleType.TypeInfo.Name, VBCurrencyType.TypeInfo.Name, VBDecimalType.TypeInfo.Name,
        };

        Assert.HasCount(names.Length, names.Distinct());
    }

    // the range a value is checked against before it is converted is the range of the type's own MinValue and MaxValue, as a Double.
    [TestMethod]
    public void TheManagedRangeOfEachType_IsItsMinValueToItsMaxValue()
    {
        (VBNumericType Type, object Min, object Max)[] types =
        [
            (VBByteType.TypeInfo, VBByteType.MinValue.RuntimeValue.BoxedValue, VBByteType.MaxValue.RuntimeValue.BoxedValue),
            (VBIntegerType.TypeInfo, VBIntegerType.MinValue.RuntimeValue.BoxedValue, VBIntegerType.MaxValue.RuntimeValue.BoxedValue),
            (VBLongType.TypeInfo, VBLongType.MinValue.RuntimeValue.BoxedValue, VBLongType.MaxValue.RuntimeValue.BoxedValue),
            (VBLongLongType.TypeInfo, VBLongLongType.MinValue.RuntimeValue.BoxedValue, VBLongLongType.MaxValue.RuntimeValue.BoxedValue),
            (VBSingleType.TypeInfo, VBSingleType.MinValue.RuntimeValue.BoxedValue, VBSingleType.MaxValue.RuntimeValue.BoxedValue),
            (VBDoubleType.TypeInfo, VBDoubleType.MinValue.RuntimeValue.BoxedValue, VBDoubleType.MaxValue.RuntimeValue.BoxedValue),
            (VBDecimalType.TypeInfo, VBDecimalType.MinValue.RuntimeValue.BoxedValue, VBDecimalType.MaxValue.RuntimeValue.BoxedValue),
        ];

        foreach (var (type, min, max) in types)
        {
            Assert.AreEqual(Convert.ToDouble(min), type.ManagedMinValue, type.Name);
            Assert.AreEqual(Convert.ToDouble(max), type.ManagedMaxValue, type.Name);
        }
    }

    // MS-VBAL 2.1.1: a Decimal's integer range is +/-79,228,162,514,264,337,593,543,950,335, which is not a Currency's.
    [TestMethod]
    public void TheRangeOfADecimal_IsThatOfADecimal()
    {
        Assert.AreEqual(7.9228162514264338e28, VBDecimalType.TypeInfo.ManagedMaxValue, 1e13);
        Assert.AreEqual(-7.9228162514264338e28, VBDecimalType.TypeInfo.ManagedMinValue, 1e13);
    }

    [TestMethod]
    public void ADecimal_HoldsEveryValueOfACurrency_AndACurrencyDoesNotHoldEveryDecimal()
    {
        Assert.IsGreaterThan(VBCurrencyType.TypeInfo.ManagedMaxValue, VBDecimalType.TypeInfo.ManagedMaxValue);
        Assert.IsLessThan(VBCurrencyType.TypeInfo.ManagedMinValue, VBDecimalType.TypeInfo.ManagedMinValue);
    }

    // MS-VBAL 2.3: a fixed-length string starts as n null characters (U+0000), which is not the empty string an unbounded String starts as.
    [TestMethod]
    public void AFixedLengthString_StartsAsLengthNullCharacters()
        => Assert.AreEqual(new string('\0', 5), ((RDCore.SDK.Model.Values.Intrinsic.VBStringValue)new VBFixedStringType(5).DefaultValue).Value);
}
