using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Runtime.Execution.Frames;

/// <summary>
/// Characterization matrix for <see cref="RuntimeCallStack"/>: a plain <see cref="StackManager{TFrame}"/>
/// of <see cref="CallStackFrame"/>s that additionally frees a frame's storage the moment it's popped.
/// </summary>
[TestClass]
[TestCategory("RDCore.Runtime.Execution.Frames.RuntimeCallStack")]
public sealed class RuntimeCallStackTests
{
    private static readonly SyntaxNodeId NodeId = new(TestUri.TestSubProcUri().AbsolutePath, [1]);
    private static readonly StaticSymbol Procedure = new("DoWork", SymbolKindExt.Procedure, VBVoidType.TypeInfo);

    private static Symbol Local(string name)
    {
        var uri = TestUri.TestVariableUri(name);
        return new VBLocalVariableSymbol(uri, uri, name, ScopeKind.Local, SourceRange.Empty, SourceRange.Empty, ResolvedType: VBLongType.TypeInfo);
    }

    private static CallStackFrame Frame(ISessionStorage storage) => new(NodeId, Procedure, [], storage);

    [TestMethod]
    public void TryPush_ThenCurrent_IsTheNewlyPushedFrame()
    {
        var sut = new RuntimeCallStack();
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var frame = Frame(storage);

        Assert.IsTrue(sut.TryPush(frame));

        Assert.AreSame(frame, sut.Current);
        Assert.AreEqual(1, sut.Depth);
    }

    [TestMethod]
    public void EmptyStack_CurrentIsNull()
        => Assert.IsNull(new RuntimeCallStack().Current);

    [TestMethod]
    public void TryPop_FreesTheFramesStorage()
        // this is the whole point of RuntimeCallStack over the plain StackManager base: popping a
        // frame must not leave its locals readable, or their storage leaked.
    {
        var sut = new RuntimeCallStack();
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var frame = Frame(storage);
        var local = Local("i");
        var value = new VBLongValue(5);
        frame.Push(local, value);
        sut.TryPush(frame);

        Assert.IsTrue(sut.TryPop(out var popped));

        Assert.AreSame(frame, popped);
        Assert.IsFalse(frame.TryResolve(local, out _));
        Assert.IsTrue(storage.TryAllocate(value.Size, value.Handle, out _), "the address must be genuinely free again");
    }

    [TestMethod]
    public void NestedActivations_PopInLifoOrder_EachKeepingItsOwnLocalIndependent()
    {
        var sut = new RuntimeCallStack();
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var local = Local("i");
        var outer = Frame(storage);
        var inner = Frame(storage);
        var outerValue = new VBLongValue(1);
        var innerValue = new VBLongValue(2);
        outer.Push(local, outerValue);
        inner.Push(local, innerValue);

        sut.TryPush(outer);
        sut.TryPush(inner);
        Assert.AreSame(inner, sut.Current);

        Assert.IsTrue(sut.TryPop(out var poppedInner));
        Assert.AreSame(inner, poppedInner);
        Assert.AreSame(outer, sut.Current, "the outer activation resumes as current");
        Assert.AreEqual(outerValue.Handle.Value, outer.GetValue(local).Value, "unaffected by the inner activation's teardown");

        Assert.IsTrue(sut.TryPop(out var poppedOuter));
        Assert.AreSame(outer, poppedOuter);
        Assert.IsNull(sut.Current);
    }

    [TestMethod]
    public void ICallStack_TryPush_OnlyAcceptsTheConcreteFrameType()
    {
        ICallStack sut = new RuntimeCallStack();
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));

        Assert.IsTrue(sut.TryPush(Frame(storage)));
        Assert.AreEqual(1, sut.Depth);
    }

    [TestMethod]
    public void ICallStack_TryPop_ReturnsTheInterfaceTypedFrame()
    {
        ICallStack sut = new RuntimeCallStack();
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var frame = Frame(storage);
        sut.TryPush(frame);

        Assert.IsTrue(sut.TryPop(out var popped));

        Assert.AreSame(frame, popped);
    }

    // an activation compiled code put on the stack without a frame, whose frame can be made, or not.
    private sealed class Light(CallStackFrame frame) : ILightActivation
    {
        public bool Fails { get; set; }

        public CallStackFrame? Held { get; private set; }

        public CallStackFrame MakeFrame() => Fails ? throw new InvalidOperationException("no frame") : frame;

        public void HeldIn(CallStackFrame made) => Held = made;
    }

    [TestMethod]
    public void ActivationsWithoutAFrame_AreGivenTheirFrames_BottomFirst_WhenTheStackIsLookedAt()
    {
        var sut = new RuntimeCallStack();
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var (bottomFrame, topFrame) = (Frame(storage), Frame(storage));
        var (bottom, top) = (new Light(bottomFrame), new Light(topFrame));
        Assert.IsTrue(sut.TryPushLight(bottom));
        Assert.IsTrue(sut.TryPushLight(top));
        Assert.AreEqual(2, sut.Depth);

        // Frames is innermost first.
        var frames = sut.Frames.ToArray();
        CollectionAssert.AreEqual(new[] { topFrame, bottomFrame }, frames);
        Assert.AreSame(bottomFrame, bottom.Held);
        Assert.AreSame(topFrame, top.Held);
        Assert.AreEqual(2, sut.Depth);
    }

    [TestMethod]
    public void AFrameThatCantBeMade_LeavesTheStackAsItWas()
    {
        var sut = new RuntimeCallStack();
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var (bottomFrame, topFrame) = (Frame(storage), Frame(storage));
        var (bottom, top) = (new Light(bottomFrame), new Light(topFrame) { Fails = true });
        sut.TryPushLight(bottom);
        sut.TryPushLight(top);

        Assert.ThrowsExactly<InvalidOperationException>(() => sut.MaterializeLights());

        Assert.AreEqual(2, sut.Depth, "both still on the stack, each counted once");
        Assert.IsNull(bottom.Held, "the frame made of the bottom one is not the stack's");

        // made, they are given them as if nothing had failed.
        top.Fails = false;
        var frames = sut.Frames.ToArray();
        CollectionAssert.AreEqual(new[] { topFrame, bottomFrame }, frames);
        Assert.AreEqual(2, sut.Depth);
    }

    // a CLR exception going by the activations compiled code put on the stack without a frame takes each off as it goes by it, innermost first: one that is
    // not on top is not taken off.
    [TestMethod]
    public void AnActivationWithoutAFrame_IsTakenOffByAnException_OnlyWhenOnTop()
    {
        var sut = new RuntimeCallStack();
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var (bottom, top) = (new Light(Frame(storage)), new Light(Frame(storage)));
        sut.TryPushLight(bottom);
        sut.TryPushLight(top);

        Assert.IsFalse(sut.TryPopLight(bottom));
        Assert.IsTrue(sut.TryPopLight(top));
        Assert.IsTrue(sut.TryPopLight(bottom));

        Assert.AreEqual(0, sut.Depth);
        Assert.IsNull(sut.TakeSpare(), "an activation an exception went by is made again for no call");
    }

    // what a program a CLR exception ended leaves on the stack, without a frame or with one, is let go of when the next starts: no activation of it is there
    // to be seen, nor counted against the next one's depth.
    [TestMethod]
    public void WhatAProgramLeftOnTheStack_IsLetGoOf_WhenTheNextStarts()
    {
        var sut = new RuntimeCallStack();
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var session = Substitute.For<IRuntimeSession>();
        session.CallStack.Returns(sut);
        sut.TryPush(Frame(storage));
        sut.TryPushLight(new Light(Frame(storage)));

        SessionWipe.Abandon(session);

        Assert.AreEqual(0, sut.Depth);
        Assert.IsEmpty(sut.Frames);
    }
}
