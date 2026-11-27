using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// Characterization matrix for <see cref="SessionObjects"/>, the session's object lifetime manager. An object's reference count is how many times its
/// holders hold it (<see cref="ObjectHolders"/>), and an instance it does not know has none.
/// </summary>
[TestClass]
[TestCategory("RDCore.Runtime.Execution.SessionObjects")]
public sealed class SessionObjectsTests
{
    private static IBindingHandle Handle() => Substitute.For<IBindingHandle>();

    [TestMethod]
    public void CreateObject_ReturnsADistinctIdEachTime()
    {
        var sut = new SessionObjects();

        var first = sut.CreateObject();
        var second = sut.CreateObject();

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void AddRef_ThenRemoveRef_ReturnsTheRemainingCount()
    {
        var sut = new SessionObjects();
        var id = sut.CreateObject();
        var first = Handle();
        var second = Handle();
        sut.AddRef(id, first);
        sut.AddRef(id, second);

        var afterFirstRemove = sut.RemoveRef(id, first);

        Assert.AreEqual(1, afterFirstRemove);
    }

    [TestMethod]
    public void RemoveRef_UnknownInstance_ReturnsZero_DoesNotThrow()
        => Assert.AreEqual(0, new SessionObjects().RemoveRef(new VBRuntimeObjectId(), Handle()));

    [TestMethod]
    public void RemoveRef_HandleNeverAdded_DoesNotUnderflowTheCount()
    {
        var sut = new SessionObjects();
        var id = sut.CreateObject();
        sut.AddRef(id, Handle());

        var count = sut.RemoveRef(id, Handle());

        Assert.AreEqual(1, count, "removing a handle that was never a root must not affect the real roots");
    }

    [TestMethod]
    public void TryRemoveObject_WhileReferencesRemain_ReturnsFalse()
    {
        var sut = new SessionObjects();
        var id = sut.CreateObject();
        sut.AddRef(id, Handle());

        Assert.IsFalse(sut.TryRemoveObject(id));
    }

    [TestMethod]
    public void TryRemoveObject_AfterItsOnlyReferenceIsRemoved_ReturnsTrue()
    {
        var sut = new SessionObjects();
        var id = sut.CreateObject();
        var handle = Handle();
        sut.AddRef(id, handle);
        sut.RemoveRef(id, handle);

        Assert.IsTrue(sut.TryRemoveObject(id));
    }

    [TestMethod]
    public void TryRemoveObject_NeverReferenced_ReturnsTrue()
        // CreateObject starts a fresh object with zero roots - never having been referenced at all is
        // the same "nothing holds it" state as having been referenced and released.
    {
        var sut = new SessionObjects();
        var id = sut.CreateObject();

        Assert.IsTrue(sut.TryRemoveObject(id));
    }

    [TestMethod]
    public void TryRemoveObject_UnknownInstance_ReturnsFalse()
        => Assert.IsFalse(new SessionObjects().TryRemoveObject(new VBRuntimeObjectId()));

    [TestMethod]
    public void TryRemoveObject_AlreadyRemoved_ReturnsFalse_NotReusable()
    {
        var sut = new SessionObjects();
        var id = sut.CreateObject();
        Assert.IsTrue(sut.TryRemoveObject(id));

        Assert.IsFalse(sut.TryRemoveObject(id));
    }

    // an object held by a few cells, by as many as a list keeps, and by more, which the holders index: each cell is a holder until it
    // lets go, in whatever order the cells let go, and the count is how many hold the object.
    [TestMethod]
    [DataRow(3)]
    [DataRow(ObjectHolders.Listed)]
    [DataRow(ObjectHolders.Listed + 1)]
    [DataRow(10 * ObjectHolders.Listed)]
    public void ManyHolders_EachHoldsTheObject_UntilItLetsGo_InAnyOrder(int holders)
    {
        var sut = new SessionObjects();
        var id = sut.CreateObject();
        var cells = Enumerable.Range(0, holders).Select(_ => Handle()).ToArray();
        foreach (var cell in cells)
        {
            sut.AddRef(id, cell);
        }

        var left = holders;
        for (var i = 0; i < holders; i += 2)
        {
            Assert.AreEqual(--left, sut.RemoveRef(id, cells[i]), $"the count after the cell {i} let go");
        }

        for (var i = 0; i < holders; i++)
        {
            Assert.AreEqual(i % 2 == 1, sut.IsHeldBy(id, cells[i]), $"cell {i}");
        }

        Assert.AreEqual(left, sut.RefCount(id));
    }

    // a cell that takes the object twice, as a Set of a variable to the object it holds does before it lets go of the old reference, holds it until it lets
    // go twice: with few holders and with many.
    [TestMethod]
    [DataRow(0)]
    [DataRow(ObjectHolders.Listed)]
    public void ACellThatHoldsTheObjectTwice_HoldsIt_UntilItLetsGoTwice(int others)
    {
        var sut = new SessionObjects();
        var id = sut.CreateObject();
        for (var i = 0; i < others; i++)
        {
            sut.AddRef(id, Handle());
        }

        var cell = Handle();
        sut.AddRef(id, cell);
        sut.AddRef(id, cell);

        Assert.AreEqual(others + 1, sut.RemoveRef(id, cell));
        Assert.IsTrue(sut.IsHeldBy(id, cell));
        Assert.AreEqual(others, sut.RemoveRef(id, cell));
        Assert.IsFalse(sut.IsHeldBy(id, cell));
        Assert.AreEqual(others, sut.RemoveRef(id, cell), "a cell that holds nothing more lets go of nothing");
    }

    // cells of a small pool taking and letting go of the object in an order a seed fixes, the pool's cells many times each, past the size a list keeps and
    // back: after each step the count, and whether each cell holds the object, are what counting each cell's references says.
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void HoldersTakingAndLettingGo_InAnyOrder_AreCountedAsEachCellHoldsTheObject(int seed)
    {
        var sut = new SessionObjects();
        var id = sut.CreateObject();
        var pool = Enumerable.Range(0, ObjectHolders.Listed + 4).Select(_ => Handle()).ToArray();
        var held = new int[pool.Length];
        var random = new Random(seed);
        var crossed = false;
        for (var step = 0; step < 2000; step++)
        {
            var cell = random.Next(pool.Length);
            var count = held.Sum();

            // more takes than let-goes up to step 1,000, then the reverse, so that the count rises past what a list keeps and falls back.
            if (random.Next(10) < (step < 1000 ? 7 : 3))
            {
                sut.AddRef(id, pool[cell]);
                held[cell]++;
            }
            else
            {
                var left = sut.RemoveRef(id, pool[cell]);
                held[cell] = Math.Max(0, held[cell] - 1);
                Assert.AreEqual(held.Sum(), left, $"step {step}: the count RemoveRef returns");
            }

            crossed |= count > ObjectHolders.Listed;
            Assert.AreEqual(held.Sum(), sut.RefCount(id), $"step {step}");
            for (var each = 0; each < pool.Length; each++)
            {
                Assert.AreEqual(held[each] > 0, sut.IsHeldBy(id, pool[each]), $"step {step}, cell {each}");
            }
        }

        Assert.IsTrue(crossed, "the premise: the count went past what a list keeps");
    }

    // two cells are two holders however equal their values are (cells are records): with few holders and with many, a cell equal to a holder is not one.
    [TestMethod]
    [DataRow(0)]
    [DataRow(ObjectHolders.Listed)]
    public void ACellEqualToAHolder_IsNotAHolder(int others)
    {
        var sut = new SessionObjects();
        var id = sut.CreateObject();
        for (var i = 0; i < others; i++)
        {
            sut.AddRef(id, Handle());
        }

        var value = new VBRuntimeValue<VBRuntimeObjectId>(id);
        var holder = new ValueBindingHandle(value);
        var equal = new ValueBindingHandle(value);
        Assert.AreEqual(holder, equal, "the premise: the two cells compare equal by value");
        sut.AddRef(id, holder);

        Assert.IsFalse(sut.IsHeldBy(id, equal));
        Assert.AreEqual(others + 1, sut.RemoveRef(id, equal));
        Assert.IsTrue(sut.IsHeldBy(id, holder));
    }
}
