using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RDCore.Parsing;
using RDCore.Parsing.Handlers;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.Parser;

/// <summary>
/// A non-zero <c>AnchorOffset</c> is how a fragment of a larger document reports positions in that
/// document's coordinates rather than positions local to the fragment itself.
/// </summary>
[TestClass]
public sealed class AnchoredFragmentParseTests
{
    private static readonly Uri Uri = TestUri.TestModuleUri();
    private static readonly SourcePosition Anchor = new(5, 4);

    [TestMethod]
    public void NonZeroAnchor_OffsetsDeclarationLocations_ByExactlyTheAnchor()
    {
        const string content = "Public Sub Foo()\r\nEnd Sub";

        var zero = new ModuleParser().Parse(Uri, content);
        var anchored = new ModuleParser().Parse(Uri, content, Anchor);

        Assert.IsTrue(zero.IsSuccess);
        Assert.IsTrue(anchored.IsSuccess);
        Assert.IsNotEmpty(zero.SyntaxTree!.Children);
        Assert.IsNotEmpty(anchored.SyntaxTree!.Children);

        var zeroRange = zero.SyntaxTree.Children[0].SourceLocation.Range;
        var anchoredRange = anchored.SyntaxTree.Children[0].SourceLocation.Range;
        Assert.AreEqual(zeroRange.Start + Anchor, anchoredRange.Start);
        Assert.AreEqual(zeroRange.End + Anchor, anchoredRange.End);
    }

    [TestMethod]
    public void NonZeroAnchor_OffsetsSyntaxErrorLocations_ByExactlyTheAnchor()
    {
        const string content = "Dim value As";

        var zero = new ModuleParser().Parse(Uri, content);
        var anchored = new ModuleParser().Parse(Uri, content, Anchor);

        Assert.IsFalse(zero.IsSuccess);
        Assert.IsFalse(anchored.IsSuccess);
        Assert.IsNotEmpty(zero.SyntaxErrors);
        Assert.IsNotEmpty(anchored.SyntaxErrors);

        Assert.AreEqual(
            zero.SyntaxErrors[0].Location.Range.Start + Anchor,
            anchored.SyntaxErrors[0].Location.Range.Start);
    }

    [TestMethod]
    public void NonZeroAnchor_OffsetsPrecompilerTrivia_ByExactlyTheAnchor()
    {
        const string content = "#If VBA7 And Win64 Then\r\nPublic X As Long\r\n#End If";

        var zero = new ModuleParser().Parse(Uri, content);
        var anchored = new ModuleParser().Parse(Uri, content, Anchor);

        Assert.IsNotEmpty(zero.PrecompilerTrivia);
        Assert.IsNotEmpty(anchored.PrecompilerTrivia);

        Assert.AreEqual(
            zero.PrecompilerTrivia[0].SourceLocation.Range.Start + Anchor,
            anchored.PrecompilerTrivia[0].SourceLocation.Range.Start);
    }

    [TestMethod]
    public void NonZeroAnchor_OnAnEmptyFragment_LocatesTheEmptyModuleAtTheAnchor()
    {
        var result = new ModuleParser().Parse(Uri, "", Anchor);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(new SourceRange(Anchor, Anchor), result.SyntaxTree!.SourceLocation.Range);
    }

    [TestMethod]
    public async Task ParseFullDocumentHandler_ForwardsTheRequestsAnchorOffset_ToTheParser()
    {
        const string content = "Public Sub Foo()\r\nEnd Sub";
        var zero = new ModuleParser().Parse(Uri, content);

        var handler = new ParseFullDocumentHandler(
            new ModuleParser(), NullLogger<ParseFullDocumentHandler>.Instance,
            Options.Create(new SdkServerOptions()));
        var request = new ParseDocumentParams { DocumentUri = Uri, Fragment = content, AnchorOffset = Anchor };

        var envelope = await handler.Handle(request, CancellationToken.None);
        var result = envelope.Unwrap<ModuleParseResult>();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            zero.SyntaxTree!.Children[0].SourceLocation.Range.Start + Anchor,
            result.SyntaxTree!.Children[0].SourceLocation.Range.Start);
    }
}
