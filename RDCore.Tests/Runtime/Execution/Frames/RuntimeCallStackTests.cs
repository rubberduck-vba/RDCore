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
        Assert.AreSame(outerValue.Handle, outer.GetValue(local), "unaffected by the inner activation's teardown");

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
}
