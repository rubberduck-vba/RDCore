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
/// Characterization matrix for <see cref="CallStackFrame"/> — the activation record of one procedure
/// call. A parameter and a <c>Dim</c> local are declared identically (MS-VBAL draws no distinction
/// between the two for name-resolution purposes): both are <see cref="CallStackFrame.Push"/>ed and
/// reserve real storage through the shared <see cref="ISessionStorage"/>.
/// </summary>
[TestClass]
[TestCategory("RDCore.Runtime.Execution.Frames.CallStackFrame")]
public sealed class CallStackFrameTests
{
    private static readonly SyntaxNodeId NodeId = new(TestUri.TestSubProcUri().AbsolutePath, [1]);
    private static readonly StaticSymbol Procedure = new("DoWork", SymbolKindExt.Procedure, VBVoidType.TypeInfo);

    private static Symbol Local(string name)
    {
        var uri = TestUri.TestVariableUri(name);
        return new VBLocalVariableSymbol(uri, uri, name, ScopeKind.Local, SourceRange.Empty, SourceRange.Empty, ResolvedType: VBLongType.TypeInfo);
    }

    private static CallStackFrame Sut(out ISessionStorage storage)
    {
        storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        return new CallStackFrame(NodeId, Procedure, [], storage);
    }

    [TestMethod]
    public void Push_ThenGetValue_ReturnsTheBoundHandle()
    {
        var sut = Sut(out _);
        var local = Local("i");
        var value = new VBLongValue(5);

        sut.Push(local, value);

        Assert.AreSame(value.Handle, sut.GetValue(local));
    }

    [TestMethod]
    public void Push_ThenTryResolve_ReturnsTheBoundHandle()
    {
        var sut = Sut(out _);
        var local = Local("i");
        var value = new VBLongValue(5);

        sut.Push(local, value);

        Assert.IsTrue(sut.TryResolve(local, out var handle));
        Assert.AreSame(value.Handle, handle);
    }

    [TestMethod]
    public void TryResolve_UndeclaredSymbol_ReturnsFalse()
        => Assert.IsFalse(Sut(out _).TryResolve(Local("i"), out _));

    [TestMethod]
    public void GetValue_UndeclaredSymbol_Throws()
        => Assert.ThrowsExactly<KeyNotFoundException>(() => Sut(out _).GetValue(Local("i")));

    [TestMethod]
    public void Push_TheSameSymbolTwice_Throws()
        // a compile-time DuplicateDeclaration should never reach a single activation at runtime.
    {
        var sut = Sut(out _);
        var local = Local("i");
        sut.Push(local, new VBLongValue(1));

        Assert.ThrowsExactly<InvalidOperationException>(() => sut.Push(local, new VBLongValue(2)));
    }

    [TestMethod]
    public void Push_DifferentSymbols_GetDistinctAddresses()
    {
        var sut = Sut(out var storage);
        var first = Local("First");
        var second = Local("Second");

        sut.Push(first, new VBLongValue(1));
        sut.Push(second, new VBByteValue(2));

        Assert.AreNotSame(sut.GetValue(first), sut.GetValue(second));
    }

    [TestMethod]
    public void ReleaseAll_FreesEveryLocal_NotJustUnlinksThem()
    {
        var sut = Sut(out var storage);
        var local = Local("i");
        var value = new VBLongValue(5);
        sut.Push(local, value);

        sut.ReleaseAll();

        Assert.IsFalse(sut.TryResolve(local, out _));
        // the address is genuinely free again in the underlying storage, not just unlinked here.
        Assert.IsTrue(storage.TryAllocate(value.Size, value.Handle, out _));
    }

    [TestMethod]
    public void TwoActivationsOfTheSameProcedure_GetIndependentBindingsForTheSameDeclaredLocal()
        // recursion: the "same" declared local, in two separate frames, must never alias.
    {
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var local = Local("i");
        var outer = new CallStackFrame(NodeId, Procedure, [], storage);
        var inner = new CallStackFrame(NodeId, Procedure, [], storage);
        var outerValue = new VBLongValue(1);
        var innerValue = new VBLongValue(2);

        outer.Push(local, outerValue);
        inner.Push(local, innerValue);

        Assert.AreSame(outerValue.Handle, outer.GetValue(local));
        Assert.AreSame(innerValue.Handle, inner.GetValue(local));

        inner.ReleaseAll();
        // the outer activation's binding survives the inner one's teardown.
        Assert.AreSame(outerValue.Handle, outer.GetValue(local));
    }
}
