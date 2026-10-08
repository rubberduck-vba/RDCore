using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;

namespace RDCore.Tests;

/// <summary>
/// What a session's memory reports while blocks are allocated, released and allocated again, and what it gives back once a program has ended.
/// </summary>
[TestClass]
public class SessionMemoryReclaimTests
{
    [TestMethod]
    public void AFreeBlock_ThatIsAllocatedAgain_IsNotFreeAnyLonger_AndNotCommittedASecondTime()
    {
        var sut = new SessionMemory(new(), PointerSize.x64);
        _ = sut.TryAllocate(8, out _);
        _ = sut.TryAllocate(4, out var address);
        _ = sut.TryAllocate(8, out _);
        sut.TryDeallocate(address, out _);
        var committed = sut.Info.CommittedBytes;

        _ = sut.TryAllocate(4, out var reused);

        Assert.AreEqual(address, reused);
        Assert.AreEqual(0, sut.Info.FreeBytes);
        Assert.AreEqual(committed, sut.Info.CommittedBytes);
        Assert.AreEqual(20, sut.Info.AllocatedBytes);
    }

    [TestMethod]
    public void AllocatingAndReleasingTheSameSizeAgainAndAgain_NeverGrowsWhatIsCommittedOrFree()
    {
        var sut = new SessionMemory(new(), PointerSize.x64);
        _ = sut.TryAllocate(8, out _);

        for (var cycle = 0; cycle < 1000; cycle++)
        {
            Assert.IsTrue(sut.TryAllocate(4, out var address));
            Assert.IsTrue(sut.TryDeallocate(address, out _));
        }

        Assert.AreEqual(12, sut.Info.CommittedBytes);
        Assert.AreEqual(4, sut.Info.FreeBytes);
        Assert.AreEqual(8, sut.Info.AllocatedBytes);
    }

    [TestMethod]
    public void FreeBytes_AreAlwaysWhatIsCommittedAndNotAllocated()
    {
        var sut = new SessionMemory(new(), PointerSize.x64);
        _ = sut.TryAllocate(8, out var first);
        _ = sut.TryAllocate(16, out var second);
        _ = sut.TryAllocate(4, out _);
        sut.TryDeallocate(first, out _);
        sut.TryDeallocate(second, out _);
        _ = sut.TryAllocate(6, out _);
        _ = sut.TryAllocate(2, out _);

        Assert.AreEqual(sut.Info.CommittedBytes - sut.Info.AllocatedBytes, sut.Info.FreeBytes);
    }

    [TestMethod]
    public void Reclaim_GivesTheFreeMemoryAtTheEndBack_AsUnusedMemory()
    {
        var sut = new SessionMemory(new(), PointerSize.x64);
        _ = sut.TryAllocate(8, out _);
        _ = sut.TryAllocate(4, out var second);
        _ = sut.TryAllocate(12, out var third);
        sut.TryDeallocate(third, out _);
        sut.TryDeallocate(second, out _);

        var reclaimed = sut.Reclaim();

        Assert.AreEqual(16, reclaimed);
        Assert.AreEqual(0, sut.Info.FreeBytes);
        Assert.AreEqual(0, sut.Info.LargestFreeBlock);
        Assert.AreEqual(8, sut.Info.CommittedBytes);
        Assert.AreEqual(8, sut.Info.AllocatedBytes);
    }

    [TestMethod]
    public void Reclaim_FollowsTheFreeBlocksBackFromTheEnd_WhicheverOrderTheyWereReleasedIn()
    {
        var sut = new SessionMemory(new(), PointerSize.x64);
        _ = sut.TryAllocate(8, out _);
        _ = sut.TryAllocate(4, out var second);
        _ = sut.TryAllocate(12, out var third);
        sut.TryDeallocate(second, out _);
        sut.TryDeallocate(third, out _);

        Assert.AreEqual(16, sut.Reclaim());
        Assert.AreEqual(0, sut.Info.FreeBytes);
    }

    [TestMethod]
    public void Reclaim_LeavesAHoleWithSomethingAllocatedAfterIt_WhichIsFragmentation()
    {
        var sut = new SessionMemory(new(), PointerSize.x64);
        _ = sut.TryAllocate(8, out _);
        _ = sut.TryAllocate(4, out var hole);
        _ = sut.TryAllocate(6, out _);
        _ = sut.TryAllocate(12, out var last);
        sut.TryDeallocate(hole, out _);
        sut.TryDeallocate(last, out _);

        var reclaimed = sut.Reclaim();

        Assert.AreEqual(12, reclaimed, "the block at the end only; the hole is not at the end of anything");
        Assert.AreEqual(4, sut.Info.FreeBytes);
        Assert.AreEqual(4, sut.Info.LargestFreeBlock);
    }

    [TestMethod]
    public void Reclaim_OfNothingFree_GivesNothingBack()
    {
        var sut = new SessionMemory(new(), PointerSize.x64);
        _ = sut.TryAllocate(8, out _);

        Assert.AreEqual(0, sut.Reclaim());
        Assert.AreEqual(8, sut.Info.AllocatedBytes);
    }

    [TestMethod]
    public void WhatIsReclaimed_IsAllocatedAgainFromTheSameAddress()
    {
        var sut = new SessionMemory(new(), PointerSize.x64);
        _ = sut.TryAllocate(8, out _);
        _ = sut.TryAllocate(4, out var address);
        sut.TryDeallocate(address, out _);
        sut.Reclaim();

        _ = sut.TryAllocate(4, out var again);

        Assert.AreEqual(address, again);
        Assert.AreEqual(12, sut.Info.CommittedBytes);
        Assert.AreEqual(0, sut.Info.FreeBytes);
    }

    [TestMethod]
    public void Reclaim_DoesNotGiveBackABlockThatIsAllocated()
    {
        var sut = new SessionMemory(new(), PointerSize.x64);
        _ = sut.TryAllocate(8, out var live);
        _ = sut.TryAllocate(4, out var free);
        sut.TryDeallocate(free, out _);

        sut.Reclaim();

        Assert.IsTrue(sut.TryFindBlock(live, out var block));
        Assert.AreEqual(8, block.Size);
    }

    [TestMethod]
    public void MemoryThatCannotBeCarvedFromTheLastSegment_IsNotOfferedAgain_AfterBlocksAreReleased()
    {
        // a block is released into the free list, which is offered to a request of its size first; it does not make its segment
        // available to carve a bigger block from, for the segment's unused memory is what that takes.
        var sut = new SessionMemory(new(), PointerSize.x86);
        Assert.IsTrue(sut.TryAllocate(SessionMemorySegment.SegmentSize32 - 8, out _));
        Assert.IsTrue(sut.TryAllocate(4, out var small));
        sut.TryDeallocate(small, out _);

        Assert.IsTrue(sut.TryAllocate(64, out var big), "a request nothing free can satisfy is carved from a new segment, not refused");

        Assert.IsGreaterThanOrEqualTo(SessionMemorySegment.SegmentSize32, big.Value);
    }
}
