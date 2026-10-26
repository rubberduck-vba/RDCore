using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.Symbols;

namespace RDCore.Tests.Symbols;

/// <summary>
/// A composition of the workspace is reused for as long as it is made of the very same parses, libraries and resolver, and made again when any is another.
/// </summary>
[TestClass]
public sealed class WorkspaceCompositionCacheTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly IntrinsicSymbolResolver Fallback = new();

    private static (Uri, ModuleType, ModuleParseResult) Module(string name, string body = "")
        => (new UriBuilder(Root) { Fragment = name }.Uri, ModuleType.StdModule,
            new ModuleParser().Parse(new Uri($"file:///c:/ws/{name}.bas"), $"Attribute VB_Name = \"{name}\"\r\nPublic Sub Run{name}()\r\n{body}End Sub\r\n"));

    [TestMethod]
    public void TheSameParses_AreTheSameComposition()
    {
        var cache = new WorkspaceCompositionCache();
        var main = Module("Main");
        var other = Module("Other");

        var first = cache.GetOrCompose(Root, [main, other], Fallback);
        var again = cache.GetOrCompose(Root, [main, other], Fallback);

        Assert.AreSame(first.ScopeTree, again.ScopeTree);
        Assert.AreEqual(1, cache.Composed);
    }

    [TestMethod]
    public void AModuleParsedAgain_IsComposedAgain_EvenAsTheSameText()
    {
        var cache = new WorkspaceCompositionCache();
        var other = Module("Other");

        var first = cache.GetOrCompose(Root, [Module("Main"), other], Fallback);
        var again = cache.GetOrCompose(Root, [Module("Main", "    x = 1\r\n"), other], Fallback);

        Assert.AreNotSame(first.ScopeTree, again.ScopeTree, "a parse is the same parse, and not one equal to it");
        Assert.AreEqual(2, cache.Composed);
        Assert.IsNotNull(again.Resolver.ResolveValue("RunMain", ScopeKind.Global, Root).Symbol, "and the new one is composed of what was parsed");
    }

    [TestMethod]
    public void OtherLibrariesOrAnotherResolver_AreAnotherComposition()
    {
        var cache = new WorkspaceCompositionCache();
        var main = Module("Main");

        cache.GetOrCompose(Root, [main], Fallback);
        cache.GetOrCompose(Root, [main], Fallback, libraries: []);
        cache.GetOrCompose(Root, [main], new IntrinsicSymbolResolver());
        cache.GetOrCompose(Root, [main], Fallback, implicitScope: RDCore.SDK.Model.ImplicitDeclarationScope.Module);

        Assert.AreEqual(4, cache.Composed);
    }

    [TestMethod]
    public void OnlyTheCompositionsUsedLast_AreKept()
    {
        var cache = new WorkspaceCompositionCache(capacity: 2);
        var (a, b, c) = (Module("A"), Module("B"), Module("C"));

        cache.GetOrCompose(Root, [a], Fallback);
        cache.GetOrCompose(Root, [b], Fallback);
        cache.GetOrCompose(Root, [a], Fallback);
        cache.GetOrCompose(Root, [c], Fallback);
        cache.GetOrCompose(Root, [a], Fallback);
        cache.GetOrCompose(Root, [b], Fallback);

        Assert.AreEqual(4, cache.Composed, "a, b, c, and b again: b was the one used least when c came");
    }
}
