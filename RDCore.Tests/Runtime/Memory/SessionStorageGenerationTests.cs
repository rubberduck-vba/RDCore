using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Runtime.Memory;

/// <summary>
/// What is bound at an address changes only by an allocation, a deallocation or a rebinding (a <c>POKE</c>), and each of them is counted: a binding read
/// through an address is the one there for as long as the count has not changed, which is what a variable of the session is resolved once for.
/// </summary>
[TestClass]
public sealed class SessionStorageGenerationTests
{
    private static SessionStorage Storage() => new(new SessionMemory(new FreeListManager(), PointerSize.x86));

    [TestMethod]
    public void AnAllocation_ChangesTheGeneration()
    {
        var storage = Storage();
        var before = storage.Generation;

        Assert.IsTrue(storage.TryAllocate(4, new VBLongValue(1).Handle, out _));

        Assert.AreNotEqual(before, storage.Generation);
    }

    [TestMethod]
    public void APoke_ChangesTheGeneration()
    {
        var storage = Storage();
        Assert.IsTrue(storage.TryAllocate(4, new VBLongValue(1).Handle, out var address));
        var before = storage.Generation;

        Assert.IsTrue(storage.TryPoke(address, 7));

        Assert.AreNotEqual(before, storage.Generation);
    }

    [TestMethod]
    public void ADeallocation_ChangesTheGeneration()
    {
        var storage = Storage();
        Assert.IsTrue(storage.TryAllocate(4, new VBLongValue(1).Handle, out var address));
        var before = storage.Generation;

        Assert.IsTrue(storage.TryDeallocate(address));

        Assert.AreNotEqual(before, storage.Generation);
    }

    [TestMethod]
    public void AReadOrAWriteThroughTheBinding_DoesNotChangeTheGeneration()
    {
        var storage = Storage();
        Assert.IsTrue(storage.TryAllocate(4, new VBLongValue(1).Handle, out var address));
        var before = storage.Generation;

        Assert.IsTrue(storage.TryRead(address, out var handle));
        ((IKnowledgeBinding)handle).Store(new VBLongValue(2).RuntimeValue, isKnown: true);

        Assert.AreEqual(before, storage.Generation);
    }
}
