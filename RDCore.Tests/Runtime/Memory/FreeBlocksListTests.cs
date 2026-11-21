using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests;

/// <summary>
/// The free memory of a session's address space. The contract is address order and best fit, not the
/// size order this class used to assert: two free blocks can only be merged when one ends exactly
/// where the other begins, so a list that cannot find a block's address neighbours cannot coalesce —
/// and coalescing is what actually keeps fragmentation down.
/// </summary>
[TestClass]
public class FreeBlocksListTests
{
    private static SessionMemorySegment Segment(int address = 0, int size = 4096)
        => new(new MemoryAddress(address), size, PointerSize.x64);

    private static SessionMemoryBlock Block(int address, int size) => new(new MemoryAddress(address), size);

    [TestMethod]
    public void Add_BlocksThatAreNotAdjacent_KeepsThemApart()
    {
        var sut = new FreeBlocksList();
        var segment = Segment();

        Assert.IsTrue(sut.Add(Block(42, 4), segment));
        Assert.IsTrue(sut.Add(Block(10, 2), segment));

        Assert.AreEqual(2, sut.Count);
    }

    [TestMethod]
    public void Add_ABlockAdjacentToItsPredecessor_MergesThem()
    {
        var sut = new FreeBlocksList();
        var segment = Segment();

        sut.Add(Block(0, 4), segment);
        sut.Add(Block(4, 2), segment);

        Assert.AreEqual(1, sut.Count);
        Assert.AreEqual(6, sut.LargestBlockSize);
    }

    [TestMethod]
    public void Add_ABlockAdjacentToItsSuccessor_MergesThem()
    {
        // freed in descending address order, which coalescing has to handle as readily as ascending.
        var sut = new FreeBlocksList();
        var segment = Segment();

        sut.Add(Block(4, 2), segment);
        sut.Add(Block(0, 4), segment);

        Assert.AreEqual(1, sut.Count);
        Assert.AreEqual(6, sut.LargestBlockSize);
    }

    [TestMethod]
    public void Add_ABlockBetweenTwoAdjacentOnes_MergesAllThree()
    {
        var sut = new FreeBlocksList();
        var segment = Segment();

        sut.Add(Block(0, 4), segment);
        sut.Add(Block(8, 8), segment);
        sut.Add(Block(4, 4), segment);

        Assert.AreEqual(1, sut.Count);
        Assert.AreEqual(16, sut.LargestBlockSize);
    }

    [TestMethod]
    public void Add_BlocksFromDifferentSegments_NeverMerges()
    {
        // addresses that happen to abut across a segment boundary are not one run of memory.
        var sut = new FreeBlocksList();

        sut.Add(Block(0, 4), Segment(address: 0, size: 4));
        sut.Add(Block(4, 4), Segment(address: 4, size: 4));

        Assert.AreEqual(2, sut.Count);
        Assert.AreEqual(4, sut.LargestBlockSize);
    }

    [TestMethod]
    public void Add_AnAddressThatIsAlreadyFree_IsRefused()
    {
        // a double free. Merging it would make one free block twice the size of the real one and hand
        // the same addresses to two callers.
        var sut = new FreeBlocksList();
        var segment = Segment();
        sut.Add(Block(8, 8), segment);

        Assert.IsFalse(sut.Add(Block(8, 8), segment));
        Assert.AreEqual(1, sut.Count);
        Assert.AreEqual(8, sut.LargestBlockSize);
    }

    [TestMethod]
    public void TryTakeSmallestFit_TakesTheSmallestBlockThatFits_NotTheFirst()
    {
        var sut = new FreeBlocksList();
        var segment = Segment();
        sut.Add(Block(0, 32), segment);
        sut.Add(Block(64, 8), segment);

        Assert.IsTrue(sut.TryTakeSmallestFit(8, 1, out var block, out _, out _));

        Assert.AreEqual(new MemoryAddress(64), block.Address);
        Assert.AreEqual(8, block.Size);
    }

    [TestMethod]
    public void TryTakeSmallestFit_ABlockBiggerThanTheRequest_ReturnsTheRemainder()
    {
        var sut = new FreeBlocksList();
        var segment = Segment();
        sut.Add(Block(0, 16), segment);

        Assert.IsTrue(sut.TryTakeSmallestFit(4, 1, out var block, out _, out _));

        Assert.AreEqual(4, block.Size);
        Assert.AreEqual(1, sut.Count);
        Assert.AreEqual(12, sut.LargestBlockSize);
    }

    [TestMethod]
    public void TryTakeSmallestFit_NothingBigEnough_TakesNothing()
    {
        var sut = new FreeBlocksList();
        sut.Add(Block(0, 4), Segment());

        Assert.IsFalse(sut.TryTakeSmallestFit(8, 1, out _, out _, out var segment));
        Assert.IsNull(segment);
        Assert.AreEqual(1, sut.Count);
    }

    [TestMethod]
    public void LargestBlockSize_FallsWhenABlockIsTaken()
    {
        // the maximum this replaced was only ever raised, so it over-reported for the rest of the
        // session as soon as anything was allocated out of the free list.
        var sut = new FreeBlocksList();
        var segment = Segment();
        sut.Add(Block(0, 4), segment);
        sut.Add(Block(64, 32), segment);

        Assert.AreEqual(32, sut.LargestBlockSize);
        Assert.IsTrue(sut.TryTakeSmallestFit(32, 1, out _, out _, out _));
        Assert.AreEqual(4, sut.LargestBlockSize);
    }

    [TestMethod]
    public void ABlockTakenThenFreedThenTakenAgain_RoundTripsCleanly()
    {
        // the regression: a split left a remainder whose address collided with a stale entry in the
        // block-to-segment map this list used to be paired with, and the next split threw out of the
        // allocator. Reached in practice by running any procedure with locals a second time.
        var sut = new FreeBlocksList();
        var segment = Segment();
        sut.Add(Block(0, 16), segment);

        for (var round = 0; round < 3; round++)
        {
            Assert.IsTrue(sut.TryTakeSmallestFit(8, 1, out var first, out _, out _));
            Assert.IsTrue(sut.TryTakeSmallestFit(8, 1, out var second, out _, out _));

            Assert.IsTrue(sut.Add(first, segment));
            Assert.IsTrue(sut.Add(second, segment));

            // and the two halves are one whole block again, ready for a 16-byte request.
            Assert.AreEqual(1, sut.Count);
            Assert.AreEqual(16, sut.LargestBlockSize);
        }
    }

    [TestMethod]
    public void TryTakeSmallestFit_OnABoundary_TakesTheBytesBeforeIt_AsItsPadding()
    {
        var sut = new FreeBlocksList();
        var segment = Segment();
        sut.Add(Block(2, 14), segment);

        Assert.IsTrue(sut.TryTakeSmallestFit(4, 4, out var block, out var padding, out _));

        Assert.AreEqual(new MemoryAddress(4), block.Address);
        Assert.AreEqual(4, block.Size);
        Assert.AreEqual(2, padding);
        Assert.AreEqual(1, sut.Count, "the bytes after the block are free, and the two before it are its padding");
        Assert.AreEqual(8, sut.LargestBlockSize);
    }

    [TestMethod]
    public void TryTakeSmallestFit_OnABoundary_PassesOverABlockThatIsBigEnoughOnlyOffTheBoundary()
    {
        var sut = new FreeBlocksList();
        var segment = Segment();
        sut.Add(Block(1, 8), segment);
        sut.Add(Block(32, 16), segment);

        Assert.IsTrue(sut.TryTakeSmallestFit(8, 8, out var block, out var padding, out _));

        Assert.AreEqual(new MemoryAddress(32), block.Address);
        Assert.AreEqual(0, padding);
        Assert.AreEqual(2, sut.Count);
    }
}
