using RDCore.SDK.Model.Types;
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
    public void VBCurrencyValue_ManagedCtor_RoundTrips()
        => Assert.AreEqual(12.3456m, new VBCurrencyValue(12.3456m).Value.Value);

    [TestMethod]
    public void VBDecimalValue_ManagedCtor_RoundTrips()
        => Assert.AreEqual(12.34m, new VBDecimalValue(12.34m).Value);

    [TestMethod]
    public void VBLongPtrValue_ManagedCtor_RoundTrips()
        => Assert.AreEqual(1234L, new VBLongPtrValue(1234L).Value);

    [TestMethod]
    public void VBErrorValue_ManagedCtor_RoundTrips()
        // Value is real data (the error code), not a sentinel — it must round-trip through the
        // binding handle like any other numeric intrinsic value, not sit on a bare record property.
        => Assert.AreEqual(5, new VBErrorValue(5).Value);

    [TestMethod]
    public void VBErrorValue_BindingHandleCtor_UsesTheGivenHandle()
    {
        var handle = new ValueBindingHandle(new VBRuntimeValue<int>(9));
        Assert.AreSame(handle, new VBErrorValue(handle).Handle);
        Assert.AreEqual(9, new VBErrorValue(handle).Value);
    }

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

    [TestMethod]
    public void SentinelValues_ExposeAUniformBindingHandleCtor()
    {
        var handle = InvalidBindingHandle.Default;
        Assert.AreSame(handle, new VBEmptyValue(handle).Handle);
        Assert.AreSame(handle, new VBNullValue(handle).Handle);
        Assert.AreSame(handle, new VBUnknownValue(handle).Handle);
        Assert.AreSame(handle, new VBMissingValue(handle).Handle);
    }

    [TestMethod]
    public void ComplexValues_ExposeAUniformBindingHandleCtor()
    {
        var handle = InvalidBindingHandle.Default;
        (int, int)[] dims = [(0, 2)];
        // item type kept to a numeric to avoid the pre-existing VBVariant default-value materialization
        // bug in VBArrayDimension's ctor (out of scope — complex-value follow-up).
        Assert.AreSame(handle, new VBFixedSizeArrayValue(handle, dims, VBIntegerType.TypeInfo).Handle);
        Assert.AreSame(handle, new VBResizableArrayValue(handle, dims, VBIntegerType.TypeInfo).Handle);
        Assert.AreSame(handle, new VBResizableByteArrayValue(handle, dims).Handle);
        Assert.AreSame(handle, new VBVariantValue(handle, new VBLongValue(1)).Handle);
        // VBUserDefinedTypeValue(IBindingHandle, VBUserDefinedType) compiles; a UDT fixture needs a Symbol.
    }
}
