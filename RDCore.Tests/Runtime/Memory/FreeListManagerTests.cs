using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests;

[TestClass]
public class FreeListManagerTests
{
    [TestMethod]
    public void EmptyLists_TryGetFreeListBlock_IsFalse()
    {
        var sut = new FreeListManager();

        var result = sut.TryGetFreeListBlock(4, 1, out var freeBlock, out _, out var freeBlockSegment);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void Add_SmallBlock_CanGetSmallFreeBlock()
    {
        var sut = new FreeListManager();

        var segment = new SessionMemorySegment(new MemoryAddress(0), 2048, PointerSize.x86);
        var block = new SessionMemoryBlock(new MemoryAddress(0), 4); // <= 8
        sut.Add(block, segment);

        var result = sut.TryGetFreeListBlock(4, 1, out var freeBlock, out _, out var freeBlockSegment);

        Assert.IsTrue(result);
        Assert.IsNotNull(freeBlock);
        Assert.IsNotNull(freeBlockSegment);
        Assert.AreEqual(block, freeBlock);
        Assert.AreEqual(segment, freeBlockSegment);
    }

    [TestMethod]
    public void AddLargeBlock_TryGetSmallFreeBlock_SplitsIt()
    {
        // this used to be refused, because the free list was partitioned by block size and a small
        // request never looked at the large list. Refusing it reserved new address space while free
        // bytes sat unusable; splitting is what a free list is for, and best-fit is what keeps a large
        // block whole when a smaller one would do (see TryGetSmallFreeBlock_PrefersTheSmallestFit).
        var sut = new FreeListManager();

        var segment = new SessionMemorySegment(new MemoryAddress(0), 2048, PointerSize.x86);
        sut.Add(new SessionMemoryBlock(new MemoryAddress(0), 24), segment);

        Assert.IsTrue(sut.TryGetFreeListBlock(4, 1, out var freeBlock, out _, out var freeBlockSegment));

        Assert.AreEqual(4, freeBlock!.Value.Size);
        Assert.AreEqual(new MemoryAddress(0), freeBlock!.Value.Address);
        Assert.AreEqual(segment, freeBlockSegment);
        // and the 20 bytes left over are still free.
        Assert.AreEqual(20, sut.LargestFreeBlockSize);
    }

    [TestMethod]
    public void TryGetSmallFreeBlock_PrefersTheSmallestFit()
    {
        var sut = new FreeListManager();

        var segment = new SessionMemorySegment(new MemoryAddress(0), 2048, PointerSize.x86);
        sut.Add(new SessionMemoryBlock(new MemoryAddress(0), 24), segment);
        sut.Add(new SessionMemoryBlock(new MemoryAddress(64), 8), segment);

        Assert.IsTrue(sut.TryGetFreeListBlock(4, 1, out var freeBlock, out _, out _));

        Assert.AreEqual(new MemoryAddress(64), freeBlock!.Value.Address);
        // the 24-byte block was left whole.
        Assert.AreEqual(24, sut.LargestFreeBlockSize);
    }

    [TestMethod]
    public void AddLargeBlock_TryGetLargeFreeBlock_IsTrue()
    {
        var sut = new FreeListManager();

        var segment = new SessionMemorySegment(new MemoryAddress(0), 2048, PointerSize.x86);
        var block = new SessionMemoryBlock(new MemoryAddress(0), 24); // > 8 
        sut.Add(block, segment);

        var result = sut.TryGetFreeListBlock(14, 1, out var freeBlock, out _, out var freeBlockSegment);

        Assert.IsTrue(result);
        Assert.AreEqual(block.Address, freeBlock!.Value.Address);
    }
}
