using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Source;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class DocumentSymbolHandlerTests
{
    private const string WorkspaceRoot = @"c:\ws";
    private const string RelativePath = "Mod1.bas";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();

    private static readonly WorkspaceDocument Document = new(RelativePath, WorkspaceRoot);
    private static Uri DocUri => Document.Id.Uri.ToUri();

    private DocumentSymbolHandler Sut() => new(_documents, _parsing, new IntrinsicSymbolResolver());

    private static DocumentSymbolParams Request() => new()
    {
        TextDocument = new TextDocumentIdentifier(DocUri),
    };

    private void WorkspaceContains(WorkspaceDocument? document)
        => _documents.GetAllDocuments().Returns(document is null ? [] : [document]);

    private static ModuleParseResult Parse(params string[] lines)
        => new ModuleParser().Parse(DocUri, string.Join("\n", lines));

    private static DocumentSymbol AsDocumentSymbol(SymbolInformationOrDocumentSymbol symbol)
    {
        Assert.IsTrue(symbol.IsDocumentSymbol, "the handler must prefer hierarchical DocumentSymbol results over flat SymbolInformation.");
        return symbol.DocumentSymbol!;
    }

    [TestMethod]
    public async Task ACachedModule_ProjectsTheModuleAndItsMembers()
    {
        WorkspaceContains(Document);
        _parsing.ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>())
            .Returns(Parse(
                "Attribute VB_Name = \"Mod1\"",
                "Public Sub Alpha()",
                "    Dim i As Long",
                "End Sub"));

        var result = await Sut().Handle(Request(), CancellationToken.None);

        Assert.IsNotNull(result);
        var module = AsDocumentSymbol(result.Single());
        Assert.AreEqual("Mod1", module.Name);
        Assert.AreEqual(SymbolKind.Module, module.Kind);
        var member = module.Children!.Single();
        Assert.AreEqual("Alpha", member.Name);
        Assert.AreEqual(SymbolKind.Method, member.Kind);
    }

    [TestMethod]
    public async Task AClassModule_ProjectsWithTheClassKind()
    {
        // ModuleTypeOf reads the module kind off the raw source (the VERSION header), not off the
        // parsed tree — the class-module document must carry that header in its own Text.
        var classDocument = Document.WithText("VERSION 1.0 CLASS\r\nAttribute VB_Name = \"Mod1\"\r\n");
        WorkspaceContains(classDocument);
        _parsing.ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>())
            .Returns(Parse(
                "Attribute VB_Name = \"Mod1\"",
                "Public Sub Alpha()",
                "End Sub"));

        var result = await Sut().Handle(Request(), CancellationToken.None);

        var module = AsDocumentSymbol(result!.Single());
        Assert.AreEqual(SymbolKind.Class, module.Kind);
    }

    [TestMethod]
    public async Task ADocumentOutsideTheWorkspace_AnswersEmptyWithoutParsing()
    {
        // Parsing an unknown document would put its result into the shared cache under a uri the
        // workspace does not own.
        WorkspaceContains(null);

        var result = await Sut().Handle(Request(), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsEmpty(result);
        await _parsing.DidNotReceive().ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AParseThatProducedNoTree_AnswersEmptyRatherThanNull()
    {
        WorkspaceContains(Document);
        _parsing.ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>())
            .Returns(ModuleParseResult.Failed(new SourceLocation(DocUri, SourceRange.Empty), "boom"));

        var result = await Sut().Handle(Request(), CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task AModuleWithSyntaxErrors_StillProjectsWhatRecovered()
    {
        // An outline is worth most on a document that does not compile, so the handler does not gate
        // on IsSuccess — the same rationale as FoldingRangeHandler.
        WorkspaceContains(Document);
        var parsed = Parse(
            "Attribute VB_Name = \"Mod1\"",
            "Public Sub Alpha()",
            "    Dim i As",
            "End Sub");
        Assert.IsFalse(parsed.IsSuccess, "this source is meant to carry a syntax error");

        _parsing.ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>()).Returns(parsed);

        var result = await Sut().Handle(Request(), CancellationToken.None);

        Assert.IsNotNull(result);
        var module = AsDocumentSymbol(result.Single());
        Assert.IsNotEmpty(module.Children!);
    }

    [TestMethod]
    public async Task AKnownDocumentIsParsedRatherThanReadFromTheCache()
    {
        WorkspaceContains(Document);
        _parsing.ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>())
            .Returns(Parse("Attribute VB_Name = \"Mod1\"", "Public Sub Alpha()", "End Sub"));

        await Sut().Handle(Request(), CancellationToken.None);

        await _parsing.Received(1).ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task TheModuleRange_SpansItsDeclaredMembers()
    {
        WorkspaceContains(Document);
        _parsing.ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>())
            .Returns(Parse(
                "Attribute VB_Name = \"Mod1\"",
                "Public Sub Alpha()",
                "End Sub"));

        var result = await Sut().Handle(Request(), CancellationToken.None);

        var module = AsDocumentSymbol(result!.Single());
        Assert.AreEqual(0, module.Range.Start.Line);
        Assert.AreEqual(2, module.Range.End.Line);
    }

    [TestMethod]
    public async Task AnEmptyModule_ProjectsAValidTopLevelSymbol()
    {
        WorkspaceContains(Document);
        _parsing.ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>())
            .Returns(Parse());

        var result = await Sut().Handle(Request(), CancellationToken.None);

        var module = AsDocumentSymbol(result!.Single());
        Assert.AreEqual("Mod1", module.Name);
        Assert.AreEqual(module.Range, module.SelectionRange);
        Assert.AreEqual(0, module.Range.Start.Line);
        Assert.AreEqual(0, module.Range.Start.Character);
        Assert.AreEqual(module.Range.Start, module.Range.End);
        Assert.IsNull(module.Children);
    }

    [TestMethod]
    public async Task SameNamePropertyAccessors_AllAppearInTheOutline()
    {
        WorkspaceContains(Document);
        _parsing.ParseDocumentAsync(DocUri, Arg.Any<CancellationToken>())
            .Returns(Parse(
                "Attribute VB_Name = \"Mod1\"",
                "Public Property Get Value() As Object",
                "End Property",
                "Public Property Let Value(ByVal RHS As Variant)",
                "End Property",
                "Public Property Set Value(ByVal RHS As Object)",
                "End Property"));

        var result = await Sut().Handle(Request(), CancellationToken.None);

        var module = AsDocumentSymbol(result!.Single());
        var accessors = module.Children!.ToArray();
        Assert.HasCount(3, accessors);
        Assert.IsTrue(accessors.All(child => child.Name == "Value"));
        Assert.IsTrue(accessors.All(child => child.Kind == SymbolKind.Property));
    }
}
