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
/// Characterization matrix for <see cref="VBProjectSymbol.ResolveQualifiedType"/> — MS-VBAL 5.6.4's type
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
        resolver.ResolveType("Total", ScopeKind.Global, Root).Returns(SymbolResolutionResult.Resolved(field));

        var result = VBProjectSymbol.ResolveQualifiedType(resolver, qualifier: null, "Total", Root);

        Assert.AreEqual(field, result.Symbol);
    }

    [TestMethod]
    public void QualifierResolvesToTheProject_LooksTheNameUpFromTheProjectsOwnScope()
        // not from the caller's scope: nothing declared in the enclosing module can hide the module or
        // type the project-qualified name refers to.
    {
        var project = new VBProjectSymbol(Root, "MyProject");
        var callerScope = new Uri("file://rdcore-test#Caller.Run");
        var field = Field("Total");
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.ResolveQualifier("MyProject", ScopeKind.Global, callerScope).Returns(SymbolResolutionResult.Resolved(project));
        resolver.ResolveType("Total", ScopeKind.Global, Root).Returns(SymbolResolutionResult.Resolved(field));

        var result = VBProjectSymbol.ResolveQualifiedType(resolver, "MyProject", "Total", callerScope);

        Assert.AreEqual(field, result.Symbol);
    }

    [TestMethod]
    public void TheLookupIsPositional_TheQualifierIsANamespace_TheLastPartAType()
        // a Type of the caller's module named like the project is what the bare name means (ResolveType), but it is
        // no namespace - neither a Type nor an Enum can contain a type - so it is not what the qualifier means.
    {
        var project = new VBProjectSymbol(Root, "MyProject");
        var callerScope = new Uri("file://rdcore-test#Caller.Run");
        var field = Field("Total");
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.ResolveType("MyProject", ScopeKind.Global, callerScope).Returns(SymbolResolutionResult.Resolved(Field("MyProject")));
        resolver.ResolveQualifier("MyProject", ScopeKind.Global, callerScope).Returns(SymbolResolutionResult.Resolved(project));
        resolver.ResolveType("Total", ScopeKind.Global, Root).Returns(SymbolResolutionResult.Resolved(field));

        var result = VBProjectSymbol.ResolveQualifiedType(resolver, "MyProject", "Total", callerScope);

        Assert.AreEqual(field, result.Symbol);
        resolver.DidNotReceive().ResolveType("MyProject", Arg.Any<ScopeKind>(), Arg.Any<Uri>());
        resolver.DidNotReceive().ResolveQualifier("Total", Arg.Any<ScopeKind>(), Arg.Any<Uri>());
    }

    [TestMethod]
    public void QualifierResolvesToSomethingOtherThanAProject_StaysUnbound()
        // e.g. "Total.Foo" where Total is a field, not a project - never falls through to resolving
        // "Foo" as if it were a bare, unqualified name.
    {
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.ResolveQualifier("Total", ScopeKind.Global, Root).Returns(SymbolResolutionResult.Resolved(Field("Total")));
        resolver.ResolveType("Foo", ScopeKind.Global, Root).Returns(SymbolResolutionResult.Resolved(Field("Foo")));

        var result = VBProjectSymbol.ResolveQualifiedType(resolver, "Total", "Foo", Root);

        Assert.IsTrue(result.IsUnbound);
    }

    [TestMethod]
    public void QualifierDoesNotResolveAtAll_StaysUnbound()
    {
        var resolver = Substitute.For<ISymbolResolver>();
        resolver.ResolveQualifier("Unknown", ScopeKind.Global, Root).Returns(SymbolResolutionResult.Unbound);

        var result = VBProjectSymbol.ResolveQualifiedType(resolver, "Unknown", "ClassName", Root);

        Assert.IsTrue(result.IsUnbound);
    }
}
