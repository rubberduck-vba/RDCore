using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// Characterization matrix for <see cref="CallStackAwareSymbolResolver"/> — layers the local stack
/// frame heap tier over the session-wide (module/global) resolver (<strong>RD-VBAL §2.3.1.2</strong>).
/// </summary>
[TestClass]
[TestCategory("RDCore.Runtime.Execution.CallStackAwareSymbolResolver")]
public sealed class CallStackAwareSymbolResolverTests
{
    private static readonly SyntaxNodeId NodeId = new(TestUri.TestSubProcUri().AbsolutePath, [1]);
    private static readonly StaticSymbol Procedure = new("DoWork", SymbolKindExt.Procedure, VBVoidType.TypeInfo);

    private static Symbol Local(string name)
    {
        var uri = TestUri.TestVariableUri(name);
        return new VBLocalVariableSymbol(uri, uri, name, ScopeKind.Local, SourceRange.Empty, SourceRange.Empty, ResolvedType: VBLongType.TypeInfo);
    }

    private static Symbol ModuleField(string name)
    {
        var uri = TestUri.TestModuleVariableUri(name);
        return new VBModuleFieldVariableMemberSymbol(uri, uri, name, ScopeKind.Module, VBLongType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);
    }

    [TestMethod]
    public void GetValue_LocalSymbol_NoActiveFrame_FallsBackToTheInnerResolver()
    {
        var callStack = new RuntimeCallStack();
        var inner = Substitute.For<ISymbolResolver>();
        var local = Local("i");
        var expected = new VBLongValue(1).Handle;
        inner.GetValue(local).Returns(expected);
        var sut = new CallStackAwareSymbolResolver(callStack, inner);

        Assert.AreSame(expected, sut.GetValue(local));
    }

    [TestMethod]
    public void GetValue_LocalSymbol_DeclaredOnTheCurrentFrame_ReadsFromTheFrame_NotTheInnerResolver()
    {
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var callStack = new RuntimeCallStack();
        var frame = new CallStackFrame(NodeId, Procedure, [], storage);
        var local = Local("i");
        var value = new VBLongValue(5);
        frame.Push(local, value);
        callStack.TryPush(frame);
        var inner = Substitute.For<ISymbolResolver>();
        inner.GetValue(local).Returns(_ => throw new InvalidOperationException("should not consult the inner resolver"));
        var sut = new CallStackAwareSymbolResolver(callStack, inner);

        Assert.AreEqual(value.Handle, sut.GetValue(local));
    }

    [TestMethod]
    public void GetValue_LocalSymbol_NotDeclaredOnTheCurrentFrame_FallsBackToTheInnerResolver()
        // e.g. a sibling procedure's local, briefly reachable if a caller passed the wrong scope —
        // the frame simply doesn't have it, so the session-wide resolver gets the final say.
    {
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var callStack = new RuntimeCallStack();
        callStack.TryPush(new CallStackFrame(NodeId, Procedure, [], storage));
        var local = Local("i");
        var inner = Substitute.For<ISymbolResolver>();
        var expected = new VBLongValue(9).Handle;
        inner.GetValue(local).Returns(expected);
        var sut = new CallStackAwareSymbolResolver(callStack, inner);

        Assert.AreSame(expected, sut.GetValue(local));
    }

    [TestMethod]
    public void GetValue_ModuleScopedSymbol_ActiveFrameIgnored_AlwaysUsesTheInnerResolver()
    {
        var storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        var callStack = new RuntimeCallStack();
        callStack.TryPush(new CallStackFrame(NodeId, Procedure, [], storage));
        var field = ModuleField("Total");
        var inner = Substitute.For<ISymbolResolver>();
        var expected = new VBLongValue(42).Handle;
        inner.GetValue(field).Returns(expected);
        var sut = new CallStackAwareSymbolResolver(callStack, inner);

        Assert.AreSame(expected, sut.GetValue(field));
    }

    [TestMethod]
    public void Resolve_AlwaysDelegatesToTheInnerResolver()
    {
        var callStack = new RuntimeCallStack();
        var inner = Substitute.For<ISymbolResolver>();
        var expected = SymbolResolutionResult.Unbound;
        inner.Resolve("Foo", ScopeKind.Module, StaticSymbol.GlobalUri).Returns(expected);
        var sut = new CallStackAwareSymbolResolver(callStack, inner);

        Assert.AreEqual(expected, sut.Resolve("Foo", ScopeKind.Module, StaticSymbol.GlobalUri));
    }

    [TestMethod]
    public void TryRead_AlwaysDelegatesToTheInnerResolver()
        // an address is globally unique regardless of which table registered it (every table shares
        // the same underlying ISessionStorage), so no frame-awareness is needed here.
    {
        var callStack = new RuntimeCallStack();
        var inner = Substitute.For<ISymbolResolver>();
        var address = new MemoryAddress(7);
        var expected = new VBLongValue(1).Handle;
        inner.TryRead(address, out Arg.Any<IBindingHandle?>()).Returns(call => { call[1] = expected; return true; });
        var sut = new CallStackAwareSymbolResolver(callStack, inner);

        Assert.IsTrue(sut.TryRead(address, out var value));
        Assert.AreSame(expected, value);
    }
}
