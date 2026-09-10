using RDCore.LanguageServer.Symbols;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class IntrinsicSymbolResolverTests
{
    private static readonly Uri Handle = new("file:///c:/ws/#Mod1.Foo");
    private readonly IntrinsicSymbolResolver _sut = new();

    [TestMethod]
    public void Resolve_ReservedTypeName_BindsIntrinsicType()
    {
        var symbol = _sut.Resolve("Long", ScopeKind.Global, Handle).Symbol;

        Assert.IsInstanceOfType<UnboundTypedSymbol>(symbol);
        Assert.IsInstanceOfType<VBLongType>(((StaticSymbol)symbol!).ResolvedType);
    }

    [TestMethod]
    public void Resolve_IsCaseInsensitive()
        => Assert.IsInstanceOfType<VBStringType>(((StaticSymbol)_sut.Resolve("STRING", ScopeKind.Global, Handle).Symbol!).ResolvedType);

    [TestMethod]
    public void Resolve_TypeDeclarationCharacter_BindsIntrinsicType()
        => Assert.IsInstanceOfType<VBIntegerType>(((StaticSymbol)_sut.Resolve("%", ScopeKind.Global, Handle).Symbol!).ResolvedType);

    [TestMethod]
    public void Resolve_NonIntrinsicName_IsUnbound()
        => Assert.IsTrue(_sut.Resolve("CWidget", ScopeKind.Global, Handle).IsUnbound);

    [TestMethod]
    public void GetValue_Throws()
        => Assert.ThrowsExactly<NotSupportedException>(() => _sut.GetValue(new StaticSymbol("x", SymbolKindExt.Ignored, VBUnknownType.TypeInfo)));

    [TestMethod]
    public void TryRead_ReturnsFalse()
    {
        Assert.IsFalse(_sut.TryRead(new MemoryAddress(0), out var value));
        Assert.IsNull(value);
    }
}
