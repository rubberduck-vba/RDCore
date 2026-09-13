using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests;

[TestClass]
public class SessionMemorySegmentTests
{
    [TestMethod]
    public void TryAllocate_PositiveSize_Succeeds()
    {
        var sut = new SessionMemorySegment(new MemoryAddress(0), 2048, PointerSize.x86);

        Assert.IsTrue(sut.TryAllocate(4, out var address));
        Assert.AreEqual(0, address.Value);
    }

    [TestMethod]
    // adversarial review, PRs #208-224, item 5: `_currentAddress.Value + size > _nextSegmentAddress.Value`
    // does not catch a negative size (an unchecked overflow upstream can produce one, e.g.
    // VBArrayValue.Size's unchecked multiply) - it would rewind the bump pointer backward over live
    // allocations instead, and a byte-backed allocation's later `new byte[size]` throws out of a
    // Try*-named method. TryAllocate must reject it the same way it rejects "segment full": return
    // false, never throw, never move the pointer.
    public void TryAllocate_NegativeSize_ReturnsFalse_AndDoesNotMoveThePointer()
    {
        var sut = new SessionMemorySegment(new MemoryAddress(0), 2048, PointerSize.x86);

        Assert.IsFalse(sut.TryAllocate(-1, out _));

        // the pointer must not have moved backward - a subsequent real allocation still lands at 0.
        Assert.IsTrue(sut.TryAllocate(4, out var address));
        Assert.AreEqual(0, address.Value);
    }
}
