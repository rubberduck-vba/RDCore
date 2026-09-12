using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests;

[TestClass]
public class SessionStorageTests
{
    private static IBindingHandle Handle(int value) => new ValueBindingHandle(new VBRuntimeValue<int>(value));

    [TestMethod]
    public void TryAllocate_BindsTheHandleAtTheReservedAddress()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));
        var handle = Handle(5);

        Assert.IsTrue(sut.TryAllocate(4, handle, out var address));

        Assert.IsTrue(sut.TryRead(address, out var bound));
        Assert.AreSame(handle, bound);
    }

    [TestMethod]
    public void TryAllocate_OutOfMemory_ReturnsFalse()
    {
        var allocator = Substitute.For<ISessionMemoryAllocator>();
        allocator.TryAllocate(Arg.Any<int>(), out Arg.Any<MemoryAddress>()).Returns(false);
        var sut = new SessionStorage(allocator);

        Assert.IsFalse(sut.TryAllocate(4, Handle(1), out _));
    }

    [TestMethod]
    public void TryRead_UnknownAddress_ReturnsFalse()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));

        Assert.IsFalse(sut.TryRead(new MemoryAddress(42), out _));
    }

    [TestMethod]
    public void TryDeallocate_RemovesTheBinding_AndFreesTheUnderlyingMemory()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));
        Assert.IsTrue(sut.TryAllocate(4, Handle(1), out var address));

        Assert.IsTrue(sut.TryDeallocate(address));

        Assert.IsFalse(sut.TryRead(address, out _));
        // the address is genuinely free again in the underlying allocator, not just unlinked here.
        Assert.IsTrue(sut.TryAllocate(4, Handle(2), out var reused));
        Assert.AreEqual(address, reused);
    }

    [TestMethod]
    public void TryDeallocate_UnknownAddress_ReturnsFalse()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));

        Assert.IsFalse(sut.TryDeallocate(new MemoryAddress(42)));
    }
}
