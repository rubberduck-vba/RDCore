using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// Characterization matrix for <see cref="RuntimeSymbolResolver"/> — the missing link between a
/// session's compile-time name resolution, its memory allocator (which only accounts for size and
/// fragmentation, not values), and the actual <c>IBindingHandle</c> a symbol or address resolves to.
/// </summary>
[TestClass]
[TestCategory("RDCore.Runtime.Execution.RuntimeSymbolResolver")]
public sealed class RuntimeSymbolResolverTests
{
    private static Symbol Symbol(string name)
    {
        var uri = TestUri.TestModuleUri(name);
        return new VBUserDefinedTypeMemberSymbol(uri, uri, name, ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
    }

    private static RuntimeSymbolResolver Sut(out ISymbolResolver names, out SessionMemory memory)
    {
        names = Substitute.For<ISymbolResolver>();
        memory = new SessionMemory(new FreeListManager(), PointerSize.x86);
        return new RuntimeSymbolResolver(names, memory);
    }

    [TestMethod]
    public void Resolve_DelegatesToTheInnerResolver()
    {
        var sut = Sut(out var names, out _);
        var expected = SymbolResolutionResult.Unbound;
        names.Resolve("Foo", ScopeKind.Module, StaticSymbol.GlobalUri).Returns(expected);

        var result = sut.Resolve("Foo", ScopeKind.Module, StaticSymbol.GlobalUri);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void TryAllocate_ThenGetValue_ReturnsTheBoundHandle()
    {
        var sut = Sut(out _, out _);
        var symbol = Symbol("Foo");
        var value = new VBLongValue(5);

        Assert.IsTrue(sut.TryAllocate(symbol, value, out _));

        Assert.AreSame(value.Handle, sut.GetValue(symbol));
    }

    [TestMethod]
    public void TryAllocate_ThenTryRead_ReturnsTheBoundHandle()
    {
        var sut = Sut(out _, out _);
        var symbol = Symbol("Foo");
        var value = new VBLongValue(5);

        Assert.IsTrue(sut.TryAllocate(symbol, value, out var address));

        Assert.IsTrue(sut.TryRead(address, out var handle));
        Assert.AreSame(value.Handle, handle);
    }

    [TestMethod]
    public void TryAllocate_DifferentSymbols_GetDistinctAddresses()
    {
        var sut = Sut(out _, out _);

        Assert.IsTrue(sut.TryAllocate(Symbol("Foo"), new VBLongValue(1), out var first));
        Assert.IsTrue(sut.TryAllocate(Symbol("Bar"), new VBByteValue(2), out var second));

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void GetValue_UnallocatedSymbol_Throws()
        => Assert.ThrowsExactly<KeyNotFoundException>(() => Sut(out _, out _).GetValue(Symbol("Foo")));

    [TestMethod]
    public void TryRead_UnknownAddress_ReturnsFalse()
        => Assert.IsFalse(Sut(out _, out _).TryRead(new MemoryAddress(42), out _));

    [TestMethod]
    public void TryAllocate_OutOfMemory_ReturnsFalse()
    {
        var names = Substitute.For<ISymbolResolver>();
        var memory = Substitute.For<ISessionMemoryAllocator>();
        memory.TryAllocate(Arg.Any<int>(), out Arg.Any<MemoryAddress>()).Returns(false);
        var sut = new RuntimeSymbolResolver(names, memory);

        Assert.IsFalse(sut.TryAllocate(Symbol("Foo"), new VBLongValue(5), out _));
    }

    [TestMethod]
    public void TryDeallocate_RemovesBothBindings_AndFreesTheUnderlyingMemory()
    {
        var sut = Sut(out _, out var memory);
        var symbol = Symbol("Foo");
        var value = new VBLongValue(5);
        Assert.IsTrue(sut.TryAllocate(symbol, value, out var address));

        Assert.IsTrue(sut.TryDeallocate(symbol));

        Assert.ThrowsExactly<KeyNotFoundException>(() => sut.GetValue(symbol));
        Assert.IsFalse(sut.TryRead(address, out _));
        // the address is genuinely free again in the underlying allocator, not just unlinked here.
        Assert.IsTrue(memory.TryAllocate(value.Size, out var reused));
        Assert.AreEqual(address, reused);
    }

    [TestMethod]
    public void TryDeallocate_UnallocatedSymbol_ReturnsFalse()
        => Assert.IsFalse(Sut(out _, out _).TryDeallocate(Symbol("Foo")));
}
