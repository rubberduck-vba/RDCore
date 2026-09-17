using NSubstitute;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Model.Symbols;

/// <summary>
/// Characterization matrix for <see cref="VBProjectSymbol.ResolveQualified"/> — MS-VBAL 5.6.4's type
/// binding context: <c>Project.Name</c> resolves <c>Project</c> before looking up <c>Name</c>.
/// Scoped to the enclosing project only — a qualifier that resolves to anything other than a
/// <see cref="VBProjectSymbol"/> stays unbound (no referenced-project namespace is modeled yet).
/// </summary>
[TestClass]
public sealed class VBProjectSymbolTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBModuleFieldVariableMemberSymbol Field(string name)
        => new(Root, Root, name, ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);

    [TestMethod]
    public void NoQualifier_ResolvesTheNameDirectly()
    {
        var resolver = Substitute.For<ISymbolResolver>();
        var field = Field("Total");
        resolver.Resolve("Total", ScopeKind.Global, Root).Returns(SymbolResolutionResult.Resolved(field));

        var result = VBProjectSymbol.ResolveQualified(resolver, qualifier: null, "Total", Root);

        Assert.AreEqual(field, result.Symbol);
    }

    [TestMethod]
    public void QualifierResolvesToTheProject_FallsThroughToTheOrdinaryLookup()
    {
        var project = new VBProjectSymbol(Root, "MyProject");
        var field = Field("Total");
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.Resolve("MyProject", ScopeKind.Global, Root).Returns(SymbolResolutionResult.Resolved(project));
        resolver.Resolve("Total", ScopeKind.Global, Root).Returns(SymbolResolutionResult.Resolved(field));

        var result = VBProjectSymbol.ResolveQualified(resolver, "MyProject", "Total", Root);

        Assert.AreEqual(field, result.Symbol);
    }

    [TestMethod]
    public void QualifierResolvesToSomethingOtherThanAProject_StaysUnbound()
        // e.g. "Total.Foo" where Total is a field, not a project - never falls through to resolving
        // "Foo" as if it were a bare, unqualified name.
    {
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.Resolve("Total", ScopeKind.Global, Root).Returns(SymbolResolutionResult.Resolved(Field("Total")));
        resolver.Resolve("Foo", ScopeKind.Global, Root).Returns(SymbolResolutionResult.Resolved(Field("Foo")));

        var result = VBProjectSymbol.ResolveQualified(resolver, "Total", "Foo", Root);

        Assert.IsTrue(result.IsUnbound);
    }

    [TestMethod]
    public void QualifierDoesNotResolveAtAll_StaysUnbound()
    {
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.Resolve("Unknown", ScopeKind.Global, Root).Returns(SymbolResolutionResult.Unbound);

        var result = VBProjectSymbol.ResolveQualified(resolver, "Unknown", "ClassName", Root);

        Assert.IsTrue(result.IsUnbound);
    }
}
