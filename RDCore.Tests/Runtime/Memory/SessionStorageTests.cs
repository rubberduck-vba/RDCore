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
    // a value whose declared Size is 0 (Nothing, Null, Empty, an uninitialized array, a UDT with no
    // resolvable fields) is a static/global symbol with no session storage of its own - it must never
    // reach a real allocation. SessionStorage is a thin pass-through here; the actual rejection lives
    // in the allocator (SessionMemory/SessionMemorySegment), asserted directly in their own tests.
    public void TryAllocate_ZeroSize_ReturnsFalse()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));

        Assert.IsFalse(sut.TryAllocate(0, Handle(1), out _));
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
    // adversarial review, PRs #208-224, "worth knowing, second tier": SessionMemory.TryAllocate's
    // free-list fast path returned a reused address without re-registering the block in the segment's
    // own map, so a second TryDeallocate on that address found nothing to remove (segment.TryDeallocate
    // checks its _memoryMap) and silently no-op'd - the block never went back on the free list a second
    // time, surviving exactly one reuse cycle before leaking permanently. Three full alloc/dealloc
    // cycles at the same address is the minimum that actually exercises the second reuse.
    public void TryDeallocate_SurvivesMultipleReuseCyclesAtTheSameAddress()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));

        Assert.IsTrue(sut.TryAllocate(4, Handle(1), out var first));
        Assert.IsTrue(sut.TryDeallocate(first));
        Assert.IsTrue(sut.TryAllocate(4, Handle(2), out var second));
        Assert.AreEqual(first, second);

        Assert.IsTrue(sut.TryDeallocate(second));
        Assert.IsTrue(sut.TryAllocate(4, Handle(3), out var third));
        Assert.AreEqual(first, third);

        Assert.IsTrue(sut.TryDeallocate(third));
        Assert.IsFalse(sut.TryRead(third, out _));
    }

    [TestMethod]
    public void TryDeallocate_UnknownAddress_ReturnsFalse()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));

        Assert.IsFalse(sut.TryDeallocate(new MemoryAddress(42)));
    }

    [TestMethod]
    public void TryRebind_ReplacesTheBoundHandle()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));
        Assert.IsTrue(sut.TryAllocate(4, Handle(1), out var address));
        var replacement = Handle(2);

        Assert.IsTrue(sut.TryRebind(address, replacement));

        Assert.IsTrue(sut.TryRead(address, out var bound));
        Assert.AreSame(replacement, bound);
    }

    [TestMethod]
    public void TryRebind_UnallocatedAddress_ReturnsFalse()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));

        Assert.IsFalse(sut.TryRebind(new MemoryAddress(42), Handle(1)));
    }

    [TestMethod]
    public void TryAllocateBytes_ReservesAZeroInitializedBuffer()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));

        Assert.IsTrue(sut.TryAllocateBytes(4, out var address));

        Assert.IsTrue(sut.TryReadBytes(address, out var bytes));
        CollectionAssert.AreEqual(new byte[4], bytes);
    }

    [TestMethod]
    public void TryAllocateBytes_OutOfMemory_ReturnsFalse()
    {
        var allocator = Substitute.For<ISessionMemoryAllocator>();
        allocator.TryAllocate(Arg.Any<int>(), out Arg.Any<MemoryAddress>()).Returns(false);
        var sut = new SessionStorage(allocator);

        Assert.IsFalse(sut.TryAllocateBytes(4, out _));
    }

    [TestMethod]
    public void TryWriteBytes_ThenTryReadBytes_RoundTrips()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));
        Assert.IsTrue(sut.TryAllocateBytes(4, out var address));

        Assert.IsTrue(sut.TryWriteBytes(address, [1, 2, 3, 4]));

        Assert.IsTrue(sut.TryReadBytes(address, out var bytes));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, bytes);
    }

    [TestMethod]
    public void TryWriteBytes_ALengthDifferentFromTheOriginalAllocation_StillReplacesTheBuffer()
        // the storage primitive doesn't enforce a size policy - LSet's truncate/pad-to-destination-size
        // behavior belongs to the future consumer, not the raw byte buffer.
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));
        Assert.IsTrue(sut.TryAllocateBytes(4, out var address));

        Assert.IsTrue(sut.TryWriteBytes(address, [9, 9]));

        Assert.IsTrue(sut.TryReadBytes(address, out var bytes));
        CollectionAssert.AreEqual(new byte[] { 9, 9 }, bytes);
    }

    [TestMethod]
    public void TryReadBytes_ReturnsACopy_MutatingItDoesNotAffectStorage()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));
        sut.TryAllocateBytes(4, out var address);
        sut.TryWriteBytes(address, [1, 2, 3, 4]);

        sut.TryReadBytes(address, out var first);
        first![0] = 99;

        sut.TryReadBytes(address, out var second);
        Assert.AreEqual(1, second![0]);
    }

    [TestMethod]
    public void TryReadBytes_UnknownAddress_ReturnsFalse()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));

        Assert.IsFalse(sut.TryReadBytes(new MemoryAddress(42), out _));
    }

    [TestMethod]
    public void TryWriteBytes_UnknownAddress_ReturnsFalse()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));

        Assert.IsFalse(sut.TryWriteBytes(new MemoryAddress(42), [1]));
    }

    [TestMethod]
    public void TryReadBytes_OnAHandleBackedAddress_ReturnsFalse()
        // the two allocation kinds are disjoint: an address is a handle slot or a byte buffer, never both.
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));
        Assert.IsTrue(sut.TryAllocate(4, Handle(1), out var address));

        Assert.IsFalse(sut.TryReadBytes(address, out _));
    }

    [TestMethod]
    public void TryRead_OnAByteBackedAddress_ReturnsFalse()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));
        Assert.IsTrue(sut.TryAllocateBytes(4, out var address));

        Assert.IsFalse(sut.TryRead(address, out _));
    }

    [TestMethod]
    public void TryDeallocate_RemovesAByteBackedAllocation_AndFreesTheUnderlyingMemory()
    {
        var sut = new SessionStorage(new SessionMemory(new(), PointerSize.x86));
        Assert.IsTrue(sut.TryAllocateBytes(4, out var address));

        Assert.IsTrue(sut.TryDeallocate(address));

        Assert.IsFalse(sut.TryReadBytes(address, out _));
        Assert.IsTrue(sut.TryAllocateBytes(4, out var reused));
        Assert.AreEqual(address, reused);
    }
}
