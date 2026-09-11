using NSubstitute;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Model.Symbols;

[TestClass]
public sealed class CompositeSymbolResolverTests
{
    private static readonly Uri Handle = new("file://rdcore-test#Mod1.Foo");
    private static readonly Symbol ASymbol = new StaticSymbol("A", SymbolKindExt.Ignored, VBUnknownType.TypeInfo);
    private static readonly Symbol BSymbol = new StaticSymbol("B", SymbolKindExt.Ignored, VBUnknownType.TypeInfo);

    private static ISymbolResolver Resolving(string name, SymbolResolutionResult result)
    {
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.Resolve(Arg.Any<string>(), Arg.Any<ScopeKind>(), Arg.Any<Uri>()).Returns(SymbolResolutionResult.Unbound);
        resolver.Resolve(name, Arg.Any<ScopeKind>(), Arg.Any<Uri>()).Returns(result);
        return resolver;
    }

    [TestMethod]
    public void Resolve_ReturnsTheFirstNonUnboundResult()
    {
        var composite = new CompositeSymbolResolver(
            Resolving("x", SymbolResolutionResult.Resolved(ASymbol)),
            Resolving("x", SymbolResolutionResult.Resolved(BSymbol)));

        Assert.AreSame(ASymbol, composite.Resolve("x", ScopeKind.Global, Handle).Symbol);
    }

    [TestMethod]
    public void Resolve_FallsThroughAnUnboundResolver()
    {
        var composite = new CompositeSymbolResolver(
            Substitute.For<ISymbolResolver>(), // returns default(SymbolResolutionResult) == Unbound
            Resolving("x", SymbolResolutionResult.Resolved(BSymbol)));

        Assert.AreSame(BSymbol, composite.Resolve("x", ScopeKind.Global, Handle).Symbol);
    }

    [TestMethod]
    public void Resolve_DoesNotMaskAnEarlyErrorWithALaterFallback()
    {
        var composite = new CompositeSymbolResolver(
            Resolving("x", SymbolResolutionResult.Ambiguous([ASymbol, BSymbol])),
            Resolving("x", SymbolResolutionResult.Resolved(BSymbol)));

        var result = composite.Resolve("x", ScopeKind.Global, Handle);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.AmbiguousName, result.ErrorId);
    }

    [TestMethod]
    public void Resolve_IsUnbound_WhenEveryResolverIsUnbound()
        => Assert.IsTrue(new CompositeSymbolResolver(Substitute.For<ISymbolResolver>(), Substitute.For<ISymbolResolver>())
            .Resolve("nope", ScopeKind.Global, Handle).IsUnbound);

    [TestMethod]
    public void GetValue_Throws()
        => Assert.ThrowsExactly<NotSupportedException>(
            () => new CompositeSymbolResolver().GetValue(ASymbol));
}
