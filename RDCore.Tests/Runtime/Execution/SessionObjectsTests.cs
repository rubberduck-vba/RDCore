using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// Characterization matrix for <see cref="SessionObjects"/> — the session's object lifetime manager.
/// Reference count is the roots list's own <c>Count</c>, not a separately maintained integer: two
/// bookkeeping structures for the same fact can only drift from each other, which is exactly what
/// happened here before (a disabled consistency check, and <c>RemoveRef</c> throwing
/// <see cref="KeyNotFoundException"/> for an unknown instance).
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
}
