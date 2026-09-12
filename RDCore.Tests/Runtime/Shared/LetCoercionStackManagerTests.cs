using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;

namespace RDCore.Tests.Runtime.Shared;

/// <summary>
/// Characterization matrix for <see cref="LetCoercionStackManager"/> — the recursion guard that lets
/// a <c>LetCoercionRuntimeSemantics</c> strategy detect re-entering an identical (source, destination)
/// coercion and abort instead of looping forever.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL LetCoercionStackManager recursion guard")]
public sealed class LetCoercionStackManagerTests
{
    private static readonly SyntaxNodeId NodeId = new(TestUri.TestModuleUri().AbsolutePath, [42]);

    private static LetCoercionStackFrame Frame(int value = 5, VBType? destination = null)
        => new(NodeId, InputIndex.CoercionSourceValue, new VBLongValue(value), new VBTypeDescValue(destination ?? VBLongType.TypeInfo));

    [TestMethod]
    public void TryPush_IncreasesDepth()
    {
        var sut = new LetCoercionStackManager();

        Assert.IsTrue(sut.TryPush(Frame()));
        Assert.AreEqual(1, sut.Depth);
    }

    [TestMethod]
    public void TryPush_SameFrameTwice_IsRejected_AndDoesNotIncreaseDepth()
        // this is the actual recursion guard: re-entering an identical (source, destination) coercion
        // must be detected and refused, not silently accepted a second time.
    {
        var sut = new LetCoercionStackManager();
        sut.TryPush(Frame());

        Assert.IsFalse(sut.TryPush(Frame()));
        Assert.AreEqual(1, sut.Depth);
    }

    [TestMethod]
    public void TryPush_DifferentFrames_BothSucceed()
    {
        var sut = new LetCoercionStackManager();

        Assert.IsTrue(sut.TryPush(Frame(5)));
        Assert.IsTrue(sut.TryPush(Frame(6)));
        Assert.AreEqual(2, sut.Depth);
    }

    [TestMethod]
    public void TryPop_EmptyStack_ReturnsFalse()
        => Assert.IsFalse(new LetCoercionStackManager().TryPop(out _));

    [TestMethod]
    public void TryPop_YieldsTheMostRecentlyPushedFrame_AndDecreasesDepth()
    {
        var sut = new LetCoercionStackManager();
        var frame = Frame();
        sut.TryPush(frame);

        Assert.IsTrue(sut.TryPop(out var popped));
        Assert.AreEqual(frame, popped);
        Assert.AreEqual(0, sut.Depth);
    }

    [TestMethod]
    public void PushPopOrder_IsLastInFirstOut()
    {
        var sut = new LetCoercionStackManager();
        sut.TryPush(Frame(5));
        sut.TryPush(Frame(6));

        Assert.IsTrue(sut.TryPop(out var first));
        Assert.AreEqual(Frame(6), first);
        Assert.IsTrue(sut.TryPop(out var second));
        Assert.AreEqual(Frame(5), second);
    }

    [TestMethod]
    public void TryPush_AfterPoppingTheSameFrame_IsAcceptedAgain()
        // popping must release the frame from the duplicate-rejection guard, not just the stack --
        // otherwise a coercion that legitimately re-runs later (not a recursive re-entry) would be
        // permanently and incorrectly refused for the lifetime of the manager.
    {
        var sut = new LetCoercionStackManager();
        var frame = Frame();
        sut.TryPush(frame);
        sut.TryPop(out _);

        Assert.IsTrue(sut.TryPush(frame));
        Assert.AreEqual(1, sut.Depth);
    }

    [TestMethod]
    public void Clear_ResetsDepthToZero_AndReleasesAllFramesFromTheGuard()
    {
        var sut = new LetCoercionStackManager();
        var frame = Frame();
        sut.TryPush(frame);
        sut.TryPush(Frame(6));

        sut.Clear();

        Assert.AreEqual(0, sut.Depth);
        Assert.IsTrue(sut.TryPush(frame));
    }
}
