using RDCore.SDK.Model.Values.Interop;

namespace RDCore.Tests.Types;

[TestClass]
public class ManagedDecimalInteropValueTests
{
    [DataRow(0.0)]
    [DataRow(10)]
    [DataRow(1000000)]
    [DataRow(0.25)]
    [DataRow(-634.432)]
    [DataRow(10000000.7525)]
    [TestMethod]
    [TestCategory("ManagedInterop")]
    public void RepresentsDecimalValue(object value)
    {
        var expected = Convert.ToDecimal(value);
        var runtimeValue = new ManagedDecimalInteropValue(expected);
        Assert.AreEqual(expected, runtimeValue.ManagedValue);
    }

    [TestMethod]
    [TestCategory("ManagedInterop")]
    public void RepresentsDecimalMinValue()
    {
        var expected = decimal.MinValue;
        var runtimeValue = new ManagedDecimalInteropValue(expected);
        Assert.AreEqual(expected, runtimeValue.ManagedValue);
    }

    [TestMethod]
    [TestCategory("ManagedInterop")]
    public void RepresentsDecimalMaxValue()
    {
        var expected = decimal.MaxValue;
        var runtimeValue = new ManagedDecimalInteropValue(expected);
        Assert.AreEqual(expected, runtimeValue.ManagedValue);
    }
}
