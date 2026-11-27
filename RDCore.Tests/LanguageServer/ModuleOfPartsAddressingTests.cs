using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A module written in several documents (RD-VBAL §5.1) is renamed and navigated in the document each name is written in: a name in one part is bound in
/// that part, and found where each part writes it, though the parts write the same lines.
/// </summary>
[TestClass]
public sealed class ModuleOfPartsAddressingTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-parts-addressing-ws");

    // Program's plain part declares count and writes it on line 4; its Partial part, a block of Extra.bas, writes it twice on line 2.
    private const string PlainPart = "Option Explicit\r\nPrivate count As Long\r\n\r\nPublic Sub Reset()\r\n    count = 0\r\nEnd Sub\r\n";

    private const string PartialPart = "Partial Module Program\r\nPublic Sub Bump()\r\n    count = count + 1\r\nEnd Sub\r\nEnd Module\r\n";

    // every place count is written: its declaration and its reference in Program.bas, and the two references of Extra.bas.
    private static readonly string[] Everywhere =
        ["Extra.bas (2,12)-(2,17)", "Extra.bas (2,4)-(2,9)", "Program.bas (1,8)-(1,13)", "Program.bas (4,4)-(4,9)"];

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();

    public ModuleOfPartsAddressingTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        var documents = new[]
        {
            new WorkspaceDocument("Program.bas", Root, PlainPart, version: 2), new WorkspaceDocument("Extra.bas", Root, PartialPart, version: 5),
        };
        _documents.GetAllDocuments().Returns(documents);
        _documents.IsOpen(Arg.Any<TextDocumentIdentifier>()).Returns(true);
        foreach (var document in documents)
        {
            var uri = document.Id.Uri.ToUri();
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, document.Text)));
        }

        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));
    }

    private RenameService Sut()
    {
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(
                call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        return new RenameService(
            _documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
    }

    private static Uri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName)).ToUri();

    private async Task<RenameWorkspace> WorkspaceAsync() => (await Sut().ComposeAsync(CancellationToken.None))!;

    // a place of a document, as the file and the range: what the assertions compare.
    private static string Place(DocumentUri document, Range range)
        => $"{Path.GetFileName(document.GetFileSystemPath())} ({range.Start.Line},{range.Start.Character})-({range.End.Line},{range.End.Character})";

    [TestMethod]
    [DataRow("Extra.bas", 2, 5, DisplayName = "from a reference in the Partial part")]
    [DataRow("Program.bas", 1, 9, DisplayName = "from its declaration in the plain part")]
    public async Task ARename_EditsEachPartInItsOwnDocument(string file, int line, int character)
    {
        var answer = await Sut().RenameAsync(Document(file), new Position(line, character), "total", CancellationToken.None);

        Assert.IsNotNull(answer.Value, answer.Refusal);
        var edits = answer.Value.DocumentChanges!.Select(change => change.TextDocumentEdit!)
            .SelectMany(change => change.Edits.Select(edit => (Place: Place(change.TextDocument.Uri, edit.Range), edit.NewText))).ToList();
        CollectionAssert.AreEqual(Everywhere, edits.Select(edit => edit.Place).Order(StringComparer.Ordinal).ToArray());
        Assert.IsTrue(edits.All(edit => edit.NewText == "total"), string.Join("; ", edits));
    }

    [TestMethod]
    public async Task TheReferences_OfANameInThePartialPart_AreFoundInEachDocument()
    {
        var (locations, _) = (await WorkspaceAsync()).References(Document("Extra.bas"), new Position(2, 5), includeDeclaration: true);

        CollectionAssert.AreEqual(Everywhere, locations.Select(location => Place(location.Uri, location.Range)).Order(StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public async Task TheDefinition_OfANameInThePartialPart_IsItsDeclarationInThePlainPart()
    {
        var definitions = (await WorkspaceAsync()).Definitions(Document("Extra.bas"), new Position(2, 5));

        Assert.AreEqual("Program.bas (1,8)-(1,13)", Place(definitions.Single().TargetUri, definitions.Single().TargetSelectionRange));
    }

    [TestMethod]
    public async Task TheHighlights_OfANameInThePartialPart_AreTheNamesOfItsDocument()
    {
        var highlights = (await WorkspaceAsync()).Highlights(Document("Extra.bas"), new Position(2, 13));

        CollectionAssert.AreEquivalent(new[] { new Range(2, 4, 2, 9), new Range(2, 12, 2, 17) }, highlights.Select(highlight => highlight.Range).ToArray());
    }
}
