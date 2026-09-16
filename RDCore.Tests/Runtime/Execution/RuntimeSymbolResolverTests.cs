using NSubstitute;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// Characterization matrix for <see cref="RuntimeSymbolResolver"/> — maps a symbol to the address an
/// <see cref="ISessionStorage"/> reserved for it, delegating the actual value binding to that storage.
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

    private static RuntimeSymbolResolver Sut(out ISymbolResolver names, out ISessionStorage storage)
    {
        names = Substitute.For<ISymbolResolver>();
        storage = new SessionStorage(new SessionMemory(new FreeListManager(), PointerSize.x86));
        return new RuntimeSymbolResolver(names, storage);
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
        var storage = Substitute.For<ISessionStorage>();
        storage.TryAllocate(Arg.Any<int>(), Arg.Any<IBindingHandle>(), out Arg.Any<MemoryAddress>()).Returns(false);
        var sut = new RuntimeSymbolResolver(names, storage);

        Assert.IsFalse(sut.TryAllocate(Symbol("Foo"), new VBLongValue(5), out _));
    }

    [TestMethod]
    public void TryDeallocate_RemovesBothBindings_AndFreesTheUnderlyingMemory()
    {
        var sut = Sut(out _, out var storage);
        var symbol = Symbol("Foo");
        var value = new VBLongValue(5);
        Assert.IsTrue(sut.TryAllocate(symbol, value, out var address));

        Assert.IsTrue(sut.TryDeallocate(symbol));

        Assert.ThrowsExactly<KeyNotFoundException>(() => sut.GetValue(symbol));
        Assert.IsFalse(sut.TryRead(address, out _));
        // the address is genuinely free again in the underlying storage, not just unlinked here.
        Assert.IsTrue(storage.TryAllocate(value.Size, value.Handle, out var reused));
        Assert.AreEqual(address, reused);
    }

    [TestMethod]
    public void TryDeallocate_UnallocatedSymbol_ReturnsFalse()
        => Assert.IsFalse(Sut(out _, out _).TryDeallocate(Symbol("Foo")));

    [TestMethod]
    public void TryAllocate_SameSymbolTwice_FreesThePreviousBlock_NoLeakNoStaleRead()
    {
        var sut = Sut(out _, out var storage);
        var symbol = Symbol("Foo");
        var first = new VBLongValue(1);
        var second = new VBLongValue(2);
        Assert.IsTrue(sut.TryAllocate(symbol, first, out var firstAddress));

        Assert.IsTrue(sut.TryAllocate(symbol, second, out var secondAddress));

        Assert.AreSame(second.Handle, sut.GetValue(symbol));
        // the first block was genuinely freed (not leaked): a same-size re-allocation reuses it
        // immediately, per SessionMemory's free-list fast path (SessionMemoryTests.TryAllocate_ReusesFreeMemory).
        Assert.AreEqual(firstAddress, secondAddress);
        // and reading it returns the CURRENT handle, not a stale leftover from the first allocation.
        Assert.IsTrue(storage.TryRead(secondAddress, out var bound));
        Assert.AreSame(second.Handle, bound);
    }

    [TestMethod]
    public void TryAllocate_SameSymbolTwice_OutOfMemoryOnSecondAllocation_RemovesTheMapping()
    {
        var names = Substitute.For<ISymbolResolver>();
        var allocator = Substitute.For<ISessionMemoryAllocator>();
        allocator.TryAllocate(Arg.Any<int>(), out Arg.Any<MemoryAddress>()).Returns(
            call => { call[1] = new MemoryAddress(1); return true; },
            call => { call[1] = default(MemoryAddress); return false; });
        allocator.TryDeallocate(Arg.Any<MemoryAddress>(), out Arg.Any<SessionMemoryBlock>()).Returns(true);
        var storage = new SessionStorage(allocator);
        var sut = new RuntimeSymbolResolver(names, storage);
        var symbol = Symbol("Foo");
        Assert.IsTrue(sut.TryAllocate(symbol, new VBLongValue(1), out _));

        Assert.IsFalse(sut.TryAllocate(symbol, new VBLongValue(2), out _));

        // freed on the way to the failed re-allocation, and not left pointing at a freed block.
        Assert.ThrowsExactly<KeyNotFoundException>(() => sut.GetValue(symbol));
    }
}
