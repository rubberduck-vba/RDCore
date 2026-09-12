using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;

namespace RDCore.Tests.Model.Symbols;

/// <summary>
/// Coverage for <see cref="SemanticId"/>: a fragment-aware identity for <see cref="Symbol"/>, safe to
/// use as a dictionary key or set member where <see cref="Uri"/>'s own <c>Equals</c>/<c>GetHashCode</c>
/// (which ignore <see cref="Uri.Fragment"/>) would silently collide two distinct symbols.
/// </summary>
[TestClass]
public sealed class SemanticIdTests
{
    private static Uri Uri(string fragment) => new UriBuilder("file://rdcore-test") { Fragment = fragment }.Uri;

    [TestMethod]
    public void SameUri_AreEqual()
    {
        var first = new SemanticId(Uri("Foo"));
        var second = new SemanticId(Uri("Foo"));

        Assert.IsTrue(first == second);
        Assert.IsTrue(first.Equals(second));
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    [TestMethod]
    public void UrisDifferingOnlyByFragment_AreNotEqual()
    {
        var foo = new SemanticId(Uri("Foo"));
        var bar = new SemanticId(Uri("Bar"));

        // the very case plain Uri.Equals gets wrong: these two Uris share everything but the
        // fragment, which Uri's own equality ignores but is where a symbol's identity actually lives.
        Assert.IsFalse(foo == bar);
        Assert.IsFalse(foo.Equals(bar));
        Assert.AreNotEqual(foo.GetHashCode(), bar.GetHashCode());
    }

    [TestMethod]
    public void ToString_ReturnsTheAbsoluteUri()
        => Assert.AreEqual(Uri("Foo").AbsoluteUri, new SemanticId(Uri("Foo")).ToString());

    [TestMethod]
    public void Symbol_SemanticId_WrapsItsOwnUri()
    {
        var uri = Uri("Foo");
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, "Foo", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

        // Symbol.Uri is derived (parentUri.Fragment + "." + name), not necessarily the ctor's raw
        // uri argument — SemanticId only needs to track whatever Uri the symbol actually settled on.
        Assert.AreEqual(new SemanticId(symbol.Uri), symbol.SemanticId);
    }
}
