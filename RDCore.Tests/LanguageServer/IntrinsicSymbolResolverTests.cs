using RDCore.LanguageServer.Symbols;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.Symbols;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class IntrinsicSymbolResolverTests
{
    private static readonly Uri Handle = new("file:///c:/ws/#Mod1.Foo");
    private readonly IntrinsicSymbolResolver _sut = new();

    [TestMethod]
    public void ResolveType_ReservedTypeName_BindsIntrinsicType()
    {
        var symbol = _sut.ResolveType("Long", ScopeKind.Global, Handle).Symbol;

        Assert.IsInstanceOfType<UnboundTypedSymbol>(symbol);
        Assert.IsInstanceOfType<VBLongType>(((StaticSymbol)symbol!).ResolvedType);
    }

    [TestMethod]
    public void ResolveType_IsCaseInsensitive()
        => Assert.IsInstanceOfType<VBStringType>(((StaticSymbol)_sut.ResolveType("STRING", ScopeKind.Global, Handle).Symbol!).ResolvedType);

    [TestMethod]
    public void ResolveType_TypeDeclarationCharacter_BindsIntrinsicType()
        => Assert.IsInstanceOfType<VBIntegerType>(((StaticSymbol)_sut.ResolveType("%", ScopeKind.Global, Handle).Symbol!).ResolvedType);

    [TestMethod]
    public void ResolveType_NonIntrinsicName_IsUnbound()
        => Assert.IsTrue(_sut.ResolveType("CWidget", ScopeKind.Global, Handle).IsUnbound);

    [TestMethod]
    public void ResolveValue_ReservedTypeName_IsUnbound()
        // a reserved data-type name is a keyword, not a name a simple name expression can bind (MS-VBAL 5.6.16.7).
        => Assert.IsTrue(_sut.ResolveValue("Long", ScopeKind.Global, Handle).IsUnbound);

    // it resolves type names, and holds no run-time bindings: it is no storage to read one from.
    [TestMethod]
    public void ItBindsNamesOnly_AndHoldsNoStorage()
        => Assert.IsNotInstanceOfType<ISymbolStorage>(_sut);
}
