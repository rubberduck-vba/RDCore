using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Folding;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class FoldingRangeHandlerTests
{
    private const string WorkspaceRoot = @"c:\ws";
    private const string RelativePath = "Mod1.bas";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();

    private static readonly WorkspaceDocument Document = new(RelativePath, WorkspaceRoot);
    private static Uri DocUri => Document.Id.Uri.ToUri();

    private FoldingRangeHandler Sut() => new(_documents, _parsing);

    private static FoldingRangeRequestParam Request() => new()
    {
        TextDocument = new TextDocumentIdentifier(DocUri),
    };

    private void WorkspaceContains(WorkspaceDocument? document)
        => _documents.GetAllDocuments().Returns(document is null ? [] : [document]);

    private static ModuleParseResult Parse(params string[] lines)
        => new ModuleParser().Parse(DocUri, string.Join("\n", lines));

    [TestMethod]
    public async Task ACachedModule_ProjectsItsMembers()
    {
        WorkspaceContains(Document);
        _parsing.ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>())
            .Returns(Parse(
                "Attribute VB_Name = \"Mod1\"",
                "Public Sub Alpha()",
                "    Dim i As Long",
                "End Sub"));

        var ranges = await Sut().Handle(Request(), CancellationToken.None);

        Assert.IsNotNull(ranges);
        var range = ranges.Single();
        Assert.AreEqual(1, range.StartLine);
        Assert.AreEqual(3, range.EndLine);
    }

    [TestMethod]
    public async Task ADocumentOutsideTheWorkspace_AnswersEmptyWithoutParsing()
    {
        // Parsing an unknown document would put its result into the shared cache under a uri the
        // workspace does not own.
        WorkspaceContains(null);

        var ranges = await Sut().Handle(Request(), CancellationToken.None);

        Assert.IsNotNull(ranges);
        Assert.IsEmpty(ranges);
        await _parsing.DidNotReceive().ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AKnownDocumentIsParsedRatherThanReadFromTheCache()
    {
        // The workspace parse is a background bring-up, so the cache is usually still empty when a client
        // asks for ranges on the document it has just opened. Going through ParseDocumentAsync waits for
        // the parser instead of answering nothing.
        WorkspaceContains(Document);
        _parsing.ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>())
            .Returns(Parse("Attribute VB_Name = \"Mod1\"", "Public Sub Alpha()", "End Sub"));

        await Sut().Handle(Request(), CancellationToken.None);

        await _parsing.Received(1).ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AParseThatProducedNoTree_AnswersEmptyRatherThanNull()
    {
        WorkspaceContains(Document);
        _parsing.ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>())
            .Returns(ModuleParseResult.Failed(new SourceLocation(DocUri, SourceRange.Empty), "boom"));

        var ranges = await Sut().Handle(Request(), CancellationToken.None);

        Assert.IsNotNull(ranges);
        Assert.IsEmpty(ranges);
    }

    [TestMethod]
    public async Task AModuleWithSyntaxErrors_StillFoldsWhatRecovered()
    {
        // An outline is worth most on a document that does not compile, so the handler does not gate on
        // IsSuccess.
        WorkspaceContains(Document);
        var parsed = Parse(
            "Attribute VB_Name = \"Mod1\"",
            "Public Sub Alpha()",
            "    Dim i As",
            "End Sub");
        Assert.IsFalse(parsed.IsSuccess, "this source is meant to carry a syntax error");

        _parsing.ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>()).Returns(parsed);

        var ranges = await Sut().Handle(Request(), CancellationToken.None);

        Assert.IsNotNull(ranges);
        Assert.IsNotEmpty(ranges);
    }

    [TestMethod]
    public async Task ACallWithADollarTypeHint_FoldsTheSameRegions()
    {
        // a name's type-declaration character rides on its node, which reaches the language server as the parse server's
        // JSON: a document calling Hex$ still parses cleanly, reads back, and folds as the same document calling Hex.
        var typed = await FoldsOfAsync("    Debug.Print Hex$(255)", "    Hex$ 255");
        var untyped = await FoldsOfAsync("    Debug.Print Hex(255)", "    Hex 255");

        Assert.IsNotEmpty(typed);
        CollectionAssert.AreEqual(untyped, typed);
    }

    // the folds of a one-procedure module with the body given, after the parse has been through the wire format.
    private async Task<string[]> FoldsOfAsync(params string[] body)
    {
        var parsed = PlatformJsonEnvelope.Of(Parse(["Attribute VB_Name = \"Mod1\"", "Public Sub Alpha()", .. body, "End Sub"]))
            .Unwrap<ModuleParseResult>();
        Assert.IsTrue(parsed.IsSuccess, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        WorkspaceContains(Document);
        _parsing.ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>()).Returns(parsed);

        var ranges = await Sut().Handle(Request(), CancellationToken.None);

        return [.. ranges!.Select(range => $"{range.StartLine}:{range.StartCharacter}-{range.EndLine}:{range.EndCharacter} {range.Kind}")];
    }
}
