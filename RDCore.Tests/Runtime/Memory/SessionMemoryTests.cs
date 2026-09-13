using NSubstitute.ReceivedExtensions;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;

namespace RDCore.Tests;

[TestClass]
public class SessionMemoryTests
{
    [TestMethod]
    public void TryAllocate_ReturnsMemoryAddress()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);

        var result1 = sut.TryAllocate(4, out var address1);
        var result2 = sut.TryAllocate(24, out var address2);
        var result3 = sut.TryAllocate(2, out var address3);

        Assert.IsTrue(result1);
        Assert.AreEqual(0, address1.Value);

        Assert.IsTrue(result2);
        Assert.AreEqual(4, address2.Value);

        Assert.IsTrue(result3);
        Assert.AreEqual(28, address3.Value);
    }

    [TestMethod]
    public void TryDeallocate_ReturnsMemoryBlock()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);

        var alloc = sut.TryAllocate(4, out var address1)
            && sut.TryAllocate(24, out var address2) 
            && sut.TryAllocate(2, out var address3);
        if (!alloc)
        {
            Assert.Inconclusive();
        }

        var result = sut.TryDeallocate(address1, out var block1);

        Assert.IsTrue(result);
        Assert.AreEqual(address1, block1.Address);
    }

    [TestMethod]
    public void TryAllocate_ReusesFreeMemory()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);
        _ = sut.TryAllocate(8, out _);

        if (sut.TryAllocate(4, out var address1) &&
            sut.TryDeallocate(address1, out _))
        {
            _ = sut.TryAllocate(4, out var address2);
            Assert.AreEqual(address1, address2);
        }
        else
        {
            Assert.Inconclusive();
        }
    }

    [TestMethod]
    public void TryAllocate_UpdatesLargestFreeBlockSize()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);
        _ = sut.TryAllocate(4, out _);

        if (sut.TryAllocate(4, out var address1) &&
            sut.TryDeallocate(address1, out _))
        {
            _ = sut.TryAllocate(8, out var address2) &&
            sut.TryDeallocate(address2, out _);
        }
        else
        {
            Assert.Inconclusive();
        }

        Assert.AreEqual(12, sut.Info.LargestFreeBlock); // 4+8=12, merged because adjacent
    }

    [TestMethod]
    public void SessionMemorySegmentSize32()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);
        Assert.AreEqual(SessionMemorySegment.SegmentSize32, sut.Info.ReservedSegmentBytes);
    }
    [TestMethod]
    public void SessionMemorySegmentSize64()
    {
        var sut = new SessionMemory(new(), PointerSize.x64);
        Assert.AreEqual(SessionMemorySegment.SegmentSize64, sut.Info.ReservedSegmentBytes);
    }

    [TestMethod]
    public void MemoryInfoCommittedBytes_MatchTotalAllocated()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);
        var alloc = 8;

        _ = sut.TryAllocate(alloc, out _);

        Assert.AreEqual(alloc, sut.Info.CommittedBytes);
    }

    [TestMethod]
    public void MemoryInfoAllocatedBytes_MatchTotalAllocated()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);
        var alloc = 8;

        _ = sut.TryAllocate(alloc, out _);

        Assert.AreEqual(alloc, sut.Info.AllocatedBytes);
    }

    [TestMethod]
    public void MemoryInfoReservedBytes_DeallocatedBytesRemainReserved()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);
        var alloc = 8;

        _ = sut.TryAllocate(alloc, out var address);
        sut.TryDeallocate(address, out _);

        Assert.AreEqual(SessionMemorySegment.SegmentSize32, sut.Info.ReservedSegmentBytes);
        Assert.AreEqual(0, sut.Info.AllocatedBytes);
    }

    [TestMethod]
    public void MemoryInfoFreeBytes_MatchDeallocatedBytes()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);
        var alloc = 8;

        _ = sut.TryAllocate(alloc, out var address);
        sut.TryDeallocate(address, out _);

        Assert.AreEqual(alloc, sut.Info.FreeBytes);
        Assert.AreEqual(0, sut.Info.AllocatedBytes);
    }

    [TestMethod]
    public void MemoryInfoLargestFreeBlockSize_MatchLargestDeallocatedBytes()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);
        var allocSmall = 2;
        var allocLarge = 14;

        _ = sut.TryAllocate(allocLarge, out var largeBlockAddress);
        _ = sut.TryAllocate(allocSmall, out _);

        sut.TryDeallocate(largeBlockAddress, out _);

        Assert.AreEqual(allocLarge, sut.Info.FreeBytes);
        Assert.AreEqual(allocSmall, sut.Info.AllocatedBytes);
    }

    [TestMethod]
    public void TryAllocate_SegmentFull_ReservesNewSegment()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);
        var allocSmall = 2;

        var didAllocateSegmentSize = sut.TryAllocate(SessionMemorySegment.SegmentSize32, out _);
        var didAllocateAdditional = sut.TryAllocate(allocSmall, out var smallAllocAddress);

        Assert.IsTrue(didAllocateSegmentSize);
        Assert.IsTrue(didAllocateAdditional);
        Assert.AreEqual(SessionMemorySegment.SegmentSize32 * 2, sut.Info.ReservedSegmentBytes);
        Assert.AreEqual(SessionMemorySegment.SegmentSize32 + allocSmall, sut.Info.AllocatedBytes);
    }

    [TestMethod]
    public void NewSegment_DoesNotOverlapExisting()
    {
        var sut = new SessionMemory(new(), PointerSize.x86, offset: 0);
        var allocSmall = 4;

        var didAllocateDynamicSegment = sut.TryAllocate(SessionMemorySegment.SegmentSize32 + 42, out var largeAlloc);
        var didAllocateSmallSegment = sut.TryAllocate(allocSmall, out var alloc);

        Assert.IsTrue(didAllocateDynamicSegment && didAllocateSmallSegment);
        Assert.HasCount(2, sut.Segments);

        // weak: Segments is a stack, enumerates current / top-most segment first:
        var allocSegment = sut.Segments.Last(); 
        var largeAllocSegment = sut.Segments.First();

        Assert.AreEqual(SessionMemorySegment.SegmentSize32, allocSegment.Size);
        Assert.AreEqual(allocSegment.NextSegment, largeAllocSegment.Address);

        var didAllocateFiller = sut.TryAllocate(SessionMemorySegment.SegmentSize32 - allocSmall - allocSmall, out var fillerAlloc);
        Assert.IsTrue(didAllocateFiller);
        Assert.HasCount(2, sut.Segments);
        Assert.AreEqual(allocSmall, sut.Info.UncommittedBytes);

        var didAllocateThird = sut.TryAllocate(allocSmall * 3, out var thirdAlloc);
        Assert.IsTrue(didAllocateThird);
        Assert.AreEqual(largeAllocSegment.NextSegment, thirdAlloc);
        Assert.HasCount(3, sut.Segments);
        Assert.AreEqual(SessionMemorySegment.SegmentSize32 - allocSmall - allocSmall, sut.Info.UncommittedBytes);
    }

    [TestMethod]
    // adversarial review, PRs #208-224, item 5's exact repro: free 8 bytes, TryAllocate(0) used to
    // succeed at that address (FreeListManager's `freeList[i].Size >= size` is trivially true for
    // size 0), splitting a "fragment" identical in address AND size to the original block (size - 0 ==
    // size) - so the free list re-offered the SAME address to the next real allocation while the
    // 0-byte one was still logically live. Author's resolution: there is no such thing as a 0-byte
    // allocation, reject it before the free list is ever consulted.
    public void TryAllocate_ZeroSize_ReturnsFalse_AndDoesNotConsumeAFreeBlock()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);
        Assert.IsTrue(sut.TryAllocate(8, out var address));
        Assert.IsTrue(sut.TryDeallocate(address, out _));

        Assert.IsFalse(sut.TryAllocate(0, out _));

        // the freed 8-byte block must still be intact and reusable.
        Assert.IsTrue(sut.TryAllocate(8, out var reused));
        Assert.AreEqual(address, reused);
    }

    [TestMethod]
    public void TryDeallocate_EmptyMemorySpace_ReturnsFalse()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);

        var result = sut.TryDeallocate(new(42), out var block);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryDeallocate_UnallocatedAddress_ReturnsFalse()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);
        _ = sut.TryAllocate(4, out _);

        var result = sut.TryDeallocate(new(42), out var block);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryDeallocate_UntrackedAddress_ReturnsFalse()
    {
        var sut = new SessionMemory(new(), PointerSize.x86);
        _ = sut.TryAllocate(4, out _);

        var result = sut.TryDeallocate(new(8196), out var block);

        Assert.IsFalse(result);
    }
}
