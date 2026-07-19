using RDCore.SDK.Model.Values.Interop;
using System.Runtime.CompilerServices;

namespace RDCore.Tests.Types;

[TestClass]
public class ManagedInteropValueTests
{
    [TestMethod]
    [TestCategory("ManagedInterop")]
    public void IsExpectedSize_Byte_I1()
    {
        var expected = 1;
        var size = Unsafe.SizeOf<ManagedInteropValue<byte>>();
        Assert.AreEqual(expected, size);
    }

    [TestMethod]
    [TestCategory("ManagedInterop")]
    public void IsExpectedSize_Boolean_I2()
    {
        var expected = 2;
        var size = Unsafe.SizeOf<ManagedInteropValue<ManagedBooleanInteropValue>>();
        Assert.AreEqual(expected, size);
    }

    [TestMethod]
    [TestCategory("ManagedInterop")]
    public void IsExpectedSize_Int16_I2()
    {
        var expected = 2;
        var size = Unsafe.SizeOf<ManagedInteropValue<short>>();
        Assert.AreEqual(expected, size);
    }

    [TestMethod]
    [TestCategory("ManagedInterop")]
    public void IsExpectedSize_Int32_I4()
    {
        var expected = 4;
        var size = Unsafe.SizeOf<ManagedInteropValue<int>>();
        Assert.AreEqual(expected, size);
    }

    [TestMethod]
    [TestCategory("ManagedInterop")]
    public void IsExpectedSize_Int64_I8()
    {
        var expected = 8;
        var size = Unsafe.SizeOf<ManagedInteropValue<long>>();
        Assert.AreEqual(expected, size);
    }

    [TestMethod]
    [TestCategory("ManagedInterop")]
    public void IsExpectedSize_Single()
    {
        var expected = 4;
        var size = Unsafe.SizeOf<ManagedInteropValue<float>>();
        Assert.AreEqual(expected, size);
    }

    [TestMethod]
    [TestCategory("ManagedInterop")]
    public void IsExpectedSize_Double()
    {
        var expected = 8;
        var size = Unsafe.SizeOf<ManagedInteropValue<double>>();
        Assert.AreEqual(expected, size);
    }

    [TestMethod]
    [TestCategory("ManagedInterop")]
    public void IsExpectedSize_Currency()
    {
        var expected = 8;
        var size = Unsafe.SizeOf<ManagedInteropValue<ManagedCurrencyInteropValue>>();
        Assert.AreEqual(expected, size);
    }

    [TestMethod]
    [TestCategory("ManagedInterop")]
    public void IsExpectedSize_Decimal()
    {
        var expected = 14;
        var size = Unsafe.SizeOf<ManagedInteropValue<ManagedDecimalInteropValue>>();
        Assert.AreEqual(expected, size);
    }
}
