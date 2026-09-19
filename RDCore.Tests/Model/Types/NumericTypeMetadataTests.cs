using RDCore.SDK.Model.Types;

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
}
