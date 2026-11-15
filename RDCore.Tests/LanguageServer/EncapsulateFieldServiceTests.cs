using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Encapsulate Field asked of a document: the edit of the field's module, asked from its declaration or from a reference in another module, indented and
/// verified; the refusals, where the reference that is why is; and the calls that may write through a <c>ByRef</c> parameter, which the user accepts first.
/// </summary>
[TestClass]
public sealed class EncapsulateFieldServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-encapsulatefield-ws");

    private const string Fields =
        "Option Explicit\r\n" +
        "Public Type TPoint\r\n" +
        "    X As Long\r\n" +
        "End Type\r\n" +
        "\r\n" +
        "Public count As Long\r\n" +
        "Private hidden As Long\r\n" +
        "Public Origin As TPoint\r\n" +
        "\r\n" +
        "Public Sub Reset()\r\n" +
        "    count = 0\r\n" +
        "End Sub\r\n";

    private const string Main =
        "Option Explicit\r\n" +
        "Private Sub Bump(ByRef n As Long)\r\n" +
        "    n = n + 1\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub Run()\r\n" +
        "    Bump count\r\n" +
        "    Origin.X = 5\r\n" +
        "End Sub\r\n";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly EncapsulateFieldService _service;

    public EncapsulateFieldServiceTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        var documents = new[] { new WorkspaceDocument("Fields.bas", Root, Fields, version: 3), new WorkspaceDocument("Main.bas", Root, Main, version: 1) };
        _documents.GetAllDocuments().Returns(documents);
        foreach (var document in documents)
        {
            var uri = document.Id.Uri.ToUri();
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, document.Text)));
        }

        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var rename = new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
        _service = new EncapsulateFieldService(rename, _parsing, options);
    }

    private static Uri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName)).ToUri();

    [TestMethod]
    [DataRow("Fields.bas", 5, 8, DisplayName = "from its declaration")]
    [DataRow("Fields.bas", 10, 5, DisplayName = "from a reference in its module")]
    public async Task AField_IsEncapsulatedInItsModule_AtTheVersionOfItsDocument(string document, int line, int character)
    {
        var position = new SourcePosition(line, character);

        var answer = await _service.ApplyAsync(Document(document), position, [EncapsulateFieldService.PassedByRefCode], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var edit = answer.Edit!.Documents.Single();
        Assert.AreEqual(Document("Fields.bas"), edit.Uri);
        Assert.AreEqual(3, edit.Version);
        var text = DeclarationEdits.ApplyTo(Fields, edit.Edits);
        StringAssert.Contains(text, "Private Type TFields\r\n    count As Long\r\nEnd Type\r\n\r\nPrivate this As TFields\r\n");
        StringAssert.Contains(text, "Public Sub Reset()\r\n    this.count = 0\r\nEnd Sub\r\n");
        StringAssert.Contains(text, "Public Property Let count(ByVal value As Long)\r\n    this.count = value\r\nEnd Property\r\n");
        Assert.DoesNotContain("Public count As Long", text);
    }

    [TestMethod]
    public async Task AField_IsEncapsulatedInItsModule_WhenAskedFromAReferenceInAnotherModule()
    {
        var answer = await _service.ApplyAsync(Document("Main.bas"), new SourcePosition(6, 10), [EncapsulateFieldService.PassedByRefCode], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        Assert.AreEqual(Document("Fields.bas"), answer.Edit!.Documents.Single().Uri);
    }

    [TestMethod]
    public async Task ACallThatMayWriteThroughAByRefParameter_IsAChange_ThatIsMadeOnlyWhenAccepted()
    {
        var prepared = await _service.PrepareAsync(Document("Fields.bas"), new SourcePosition(5, 8), CancellationToken.None);
        var declined = await _service.ApplyAsync(Document("Fields.bas"), new SourcePosition(5, 8), [], CancellationToken.None);

        var change = prepared.Notices.Single();
        Assert.AreEqual(RefactorNoticeKind.Change, change.Kind);
        Assert.AreEqual(EncapsulateFieldService.PassedByRefCode, change.Code);
        Assert.AreEqual(new SourceLocation(Document("Main.bas"), new SourceRange(6, 9, 6, 14)), change.Location);
        Assert.IsNull(declined.Edit);
        StringAssert.Contains(declined.Refusal, change.Message);
    }

    [TestMethod]
    public async Task APrivateField_IsRefused()
    {
        var prepared = await _service.PrepareAsync(Document("Fields.bas"), new SourcePosition(6, 9), CancellationToken.None);
        var applied = await _service.ApplyAsync(Document("Fields.bas"), new SourcePosition(6, 9), [], CancellationToken.None);

        var refusal = prepared.Notices.Single();
        Assert.AreEqual(RefactorNoticeKind.Refusal, refusal.Kind);
        Assert.AreEqual("encapsulateField.notPublic", refusal.Code);
        StringAssert.Contains(refusal.Message, "'hidden'");
        Assert.AreEqual(refusal.Message, applied.Refusal);
    }

    [TestMethod]
    public async Task AUserDefinedTypeFieldWrittenInAnotherModule_IsRefused_WhereItIsWritten()
    {
        var prepared = await _service.PrepareAsync(Document("Fields.bas"), new SourcePosition(7, 8), CancellationToken.None);

        var refusal = prepared.Notices.Single();
        Assert.AreEqual("encapsulateField.writesThroughACopy", refusal.Code);
        Assert.AreEqual(Document("Main.bas"), refusal.Location?.Uri);
        StringAssert.Contains(refusal.Message, "'Origin'");
    }

    [TestMethod]
    public async Task APositionThatNamesNoField_IsRefused()
    {
        var prepared = await _service.PrepareAsync(Document("Main.bas"), new SourcePosition(5, 12), CancellationToken.None);

        Assert.AreEqual("encapsulateField.notAField", prepared.Notices.Single().Code);
    }
}
