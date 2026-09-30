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
    public void NonZeroAnchor_OffsetsDeclarationLocations_ByTheAnchor()
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
        Assert.AreEqual(zeroRange.Start.AnchoredAt(Anchor), anchoredRange.Start);
        Assert.AreEqual(zeroRange.End.AnchoredAt(Anchor), anchoredRange.End);
    }

    [TestMethod]
    public void NonZeroAnchor_AppliesItsColumn_ToTheFirstLineOnly()
    {
        // this test's neighbour above read `+ Anchor`, which is what the parser did: the anchor's
        // column added to every line, so the member's `End Sub` — at the start of the fragment's
        // second line — reported L6C4 rather than L6C0. The anchor's column is where the fragment
        // begins on the one line it shares with what precedes it; every line after that begins at the
        // document's column 0.
        const string content = "Public Sub Foo()\r\nEnd Sub";

        var anchored = new ModuleParser().Parse(Uri, content, Anchor);

        Assert.IsTrue(anchored.IsSuccess);
        var range = anchored.SyntaxTree!.Children[0].SourceLocation.Range;
        Assert.AreEqual(Anchor, range.Start);
        Assert.AreEqual(Anchor.Line + 1, range.End.Line);
        Assert.AreEqual(0, range.End.Character);
    }

    [TestMethod]
    public void NonZeroAnchor_OnASyntaxErrorPastTheFirstLine_OffsetsTheLineOnly()
    {
        const string content = "Public Sub Foo()\r\nDim value As\r\nEnd Sub";

        var zero = new ModuleParser().Parse(Uri, content);
        var anchored = new ModuleParser().Parse(Uri, content, Anchor);

        Assert.IsNotEmpty(zero.SyntaxErrors);
        Assert.IsNotEmpty(anchored.SyntaxErrors);

        var zeroStart = zero.SyntaxErrors[0].Location.Range.Start;
        Assert.AreNotEqual(0, zeroStart.Line, "the error has to be past the fragment's first line to test anything");

        var anchoredStart = anchored.SyntaxErrors[0].Location.Range.Start;
        Assert.AreEqual(zeroStart.AnchoredAt(Anchor), anchoredStart);
        Assert.AreEqual(zeroStart.Character, anchoredStart.Character);
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
