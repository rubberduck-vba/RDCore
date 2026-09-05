using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Tests.Model.Values;

[TestClass]
[TestCategory("RD-VBAL §2.5 Runtime Values")]
public sealed class IntrinsicValueConstructorTests
{
    [TestMethod]
    public void VBByteValue_ManagedCtor_RoundTrips()
        => Assert.AreEqual((byte)7, new VBByteValue((byte)7).Value);

    [TestMethod]
    public void VBLongLongValue_ManagedCtor_RoundTrips()
        => Assert.AreEqual(5_000_000_000L, new VBLongLongValue(5_000_000_000L).Value);

    [TestMethod]
    public void VBCurrencyValue_ManagedCtor_ScalesToStoredValue()
        // asserts on the raw scaled long; VBRuntimeCurrencyValue.Value descaling is a separate concern.
        => Assert.AreEqual(123456L, new VBCurrencyValue(12.3456m).Value.StoredValue);

    [TestMethod]
    public void VBDecimalValue_ManagedCtor_RoundTrips()
        => Assert.AreEqual(12.34m, new VBDecimalValue(12.34m).Value);

    [TestMethod]
    public void VBLongPtrValue_ManagedCtor_RoundTrips()
        => Assert.AreEqual(1234L, new VBLongPtrValue(1234L).Value);

    [TestMethod]
    public void BindingHandleCtor_UsesTheGivenHandle()
    {
        var handle = new ValueBindingHandle(new VBRuntimeValue<byte>(9));
        Assert.AreSame(handle, new VBByteValue(handle).Handle);
    }

    [TestMethod]
    public void BindingHandleCtor_YieldsAWritableBinding()
        => Assert.IsTrue(new VBLongLongValue(new ValueBindingHandle(new VBRuntimeValue<long>(1))).Handle
            .BindingCapabilities.HasFlag(BindingCapabilities.SetValue));
}
