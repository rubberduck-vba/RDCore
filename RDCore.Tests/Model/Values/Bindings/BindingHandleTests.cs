using NSubstitute;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Model.Values.Bindings;

/// <summary>
/// Characterization matrix for the <see cref="IBindingHandle"/> implementations underlying every
/// <c>VBTypedValue</c>'s storage.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL value model — IBindingHandle implementations")]
public sealed class BindingHandleTests
{
    private static readonly ISymbolResolver Resolver = Substitute.For<ISymbolResolver>();

    [TestMethod]
    public void ValueBindingHandle_RoundTripsThroughValueProperty()
    {
        var sut = new ValueBindingHandle(new VBRuntimeValue<int>(5));

        Assert.AreEqual(5, ((VBRuntimeValue<int>)sut.Value).StoredValue);
        Assert.AreEqual(BindingCapabilities.GetValue | BindingCapabilities.SetValue, sut.BindingCapabilities);
    }

    [TestMethod]
    public void ValueBindingHandle_RoundTripsThroughGetSetValue()
    {
        var sut = new ValueBindingHandle(new VBRuntimeValue<int>(5));

        sut.SetValue(Resolver, new VBRuntimeValue<int>(9));

        Assert.AreEqual(9, ((VBRuntimeValue<int>)sut.GetValue(Resolver)).StoredValue);
        // .Value and GetValue(resolver) read the same underlying storage.
        Assert.AreEqual(9, ((VBRuntimeValue<int>)sut.Value).StoredValue);
    }

    [TestMethod]
    public void ValueBindingHandle_Invoke_IsNotSupported()
        => Assert.ThrowsExactly<NotSupportedException>(() => new ValueBindingHandle(new VBRuntimeValue<int>(5)).Invoke(Resolver, []));

    [TestMethod]
    public void ConstantBindingHandle_RoundTripsThroughValueProperty()
    {
        var sut = new ConstantBindingHandle(new VBRuntimeValue<int>(5));

        Assert.AreEqual(5, ((VBRuntimeValue<int>)sut.Value).StoredValue);
        Assert.AreEqual(BindingCapabilities.GetValue, sut.BindingCapabilities);
    }

    [TestMethod]
    public void ConstantBindingHandle_GetValue_ReadsTheConstant()
        => Assert.AreEqual(5, ((VBRuntimeValue<int>)new ConstantBindingHandle(new VBRuntimeValue<int>(5)).GetValue(Resolver)).StoredValue);

    [TestMethod]
    public void ConstantBindingHandle_SetValue_IsNotSupported()
        // the whole point of this handle: it's read-only, unlike ValueBindingHandle.
        => Assert.ThrowsExactly<NotSupportedException>(() => new ConstantBindingHandle(new VBRuntimeValue<int>(5)).SetValue(Resolver, new VBRuntimeValue<int>(9)));

    [TestMethod]
    public void ConstantBindingHandle_Invoke_IsNotSupported()
        => Assert.ThrowsExactly<NotSupportedException>(() => new ConstantBindingHandle(new VBRuntimeValue<int>(5)).Invoke(Resolver, []));

    [TestMethod]
    public void ReferenceBindingHandle_RoundTripsThroughValueProperty()
    {
        var reference = new VBRuntimeReference(new MemoryAddress(42));
        var sut = new ReferenceBindingHandle(reference);

        Assert.AreEqual(reference, sut.Value);
        Assert.AreEqual(BindingCapabilities.GetValue | BindingCapabilities.SetValue, sut.BindingCapabilities);
    }

    [TestMethod]
    public void ReferenceBindingHandle_GetValue_YieldsTheReferenceItself()
        // acknowledged gap (see the TODO on ReferenceBindingHandle.GetValue): this should eventually
        // follow the reference through the resolver instead of returning the reference itself, once a
        // resolver read API for it exists. Pinning today's actual behavior, not the eventual one.
        => Assert.AreEqual(new VBRuntimeReference(new MemoryAddress(42)),
            new ReferenceBindingHandle(new VBRuntimeReference(new MemoryAddress(42))).GetValue(Resolver));

    [TestMethod]
    public void ReferenceBindingHandle_SetValue_AcceptsAVBRuntimeReference()
    {
        var sut = new ReferenceBindingHandle(new VBRuntimeReference(new MemoryAddress(1)));
        var replacement = new VBRuntimeReference(new MemoryAddress(2));

        sut.SetValue(Resolver, replacement);

        Assert.AreEqual(replacement, sut.Value);
    }

    [TestMethod]
    public void ReferenceBindingHandle_SetValue_RejectsAnyOtherRuntimeValue()
        => Assert.ThrowsExactly<ArgumentException>(
            () => new ReferenceBindingHandle(new VBRuntimeReference(new MemoryAddress(1))).SetValue(Resolver, new VBRuntimeValue<int>(5)));

    [TestMethod]
    public void ReferenceBindingHandle_Invoke_IsNotSupported()
        => Assert.ThrowsExactly<NotSupportedException>(
            () => new ReferenceBindingHandle(new VBRuntimeReference(new MemoryAddress(1))).Invoke(Resolver, []));

    [TestMethod]
    public void InvalidBindingHandle_HasNoCapabilities()
        => Assert.AreEqual(BindingCapabilities.None, InvalidBindingHandle.Default.BindingCapabilities);

    [TestMethod]
    public void InvalidBindingHandle_EveryOperation_IsNotSupported()
    {
        var sut = InvalidBindingHandle.Default;

        Assert.ThrowsExactly<NotSupportedException>(() => sut.Value);
        Assert.ThrowsExactly<NotSupportedException>(() => sut.GetValue(Resolver));
        Assert.ThrowsExactly<NotSupportedException>(() => sut.SetValue(Resolver, new VBRuntimeValue<int>(5)));
        Assert.ThrowsExactly<NotSupportedException>(() => sut.Invoke(Resolver, []));
    }
}
