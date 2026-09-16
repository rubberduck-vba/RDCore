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
    public void ReferenceBindingHandle_GetValue_FollowsTheReferenceThroughTheResolver()
    {
        var address = new MemoryAddress(42);
        var target = Substitute.For<IBindingHandle>();
        target.GetValue(Arg.Any<ISymbolResolver>()).Returns(new VBRuntimeValue<int>(9));
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.TryRead(address, out Arg.Any<IBindingHandle?>()).Returns(call =>
        {
            call[1] = target;
            return true;
        });

        var result = new ReferenceBindingHandle(new VBRuntimeReference(address)).GetValue(resolver);

        Assert.AreEqual(9, ((VBRuntimeValue<int>)result).StoredValue);
    }

    [TestMethod]
    public void ReferenceBindingHandle_GetValue_UnresolvableAddress_FallsBackToTheReferenceItself()
        // a dangling/not-yet-bound reference (e.g. Nothing, or an address the resolver doesn't know
        // about) yields itself rather than throwing.
        => Assert.AreEqual(new VBRuntimeReference(new MemoryAddress(42)),
            new ReferenceBindingHandle(new VBRuntimeReference(new MemoryAddress(42))).GetValue(Resolver));

    [TestMethod]
    public void ReferenceBindingHandle_GetValue_SelfReference_StopsInsteadOfOverflowing()
        // VBA "Set x = x": x's own storage holds a reference to its own address.
    {
        var address = new MemoryAddress(1);
        var sut = new ReferenceBindingHandle(new VBRuntimeReference(address));
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.TryRead(address, out Arg.Any<IBindingHandle?>()).Returns(call =>
        {
            call[1] = sut;
            return true;
        });

        var result = sut.GetValue(resolver);

        Assert.AreEqual(new VBRuntimeReference(address), result);
    }

    [TestMethod]
    public void ReferenceBindingHandle_GetValue_TwoCycle_StopsInsteadOfOverflowing()
        // VBA "Set a = b : Set b = a": each variable's storage references the other's address.
    {
        var addressA = new MemoryAddress(1);
        var addressB = new MemoryAddress(2);
        var a = new ReferenceBindingHandle(new VBRuntimeReference(addressB));
        var b = new ReferenceBindingHandle(new VBRuntimeReference(addressA));
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.TryRead(addressA, out Arg.Any<IBindingHandle?>()).Returns(call => { call[1] = a; return true; });
        resolver.TryRead(addressB, out Arg.Any<IBindingHandle?>()).Returns(call => { call[1] = b; return true; });

        var result = a.GetValue(resolver);

        Assert.AreEqual(new VBRuntimeReference(addressA), result);
    }

    [TestMethod]
    public void ReferenceBindingHandle_GetValue_NonCyclicChain_StillFollowsThroughToTheFinalValue()
        // a reference to a reference to a real value is not a cycle and must still resolve correctly.
    {
        var addressA = new MemoryAddress(1);
        var addressB = new MemoryAddress(2);
        var final = Substitute.For<IBindingHandle>();
        final.GetValue(Arg.Any<ISymbolResolver>()).Returns(new VBRuntimeValue<int>(9));
        var a = new ReferenceBindingHandle(new VBRuntimeReference(addressB));
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.TryRead(addressA, out Arg.Any<IBindingHandle?>()).Returns(call => { call[1] = a; return true; });
        resolver.TryRead(addressB, out Arg.Any<IBindingHandle?>()).Returns(call => { call[1] = final; return true; });

        var result = new ReferenceBindingHandle(new VBRuntimeReference(addressA)).GetValue(resolver);

        Assert.AreEqual(9, ((VBRuntimeValue<int>)result).StoredValue);
    }

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
