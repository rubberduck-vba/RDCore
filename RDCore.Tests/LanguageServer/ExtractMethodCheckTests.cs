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
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What the name the user is typing for an extraction raises before it is applied: a name the extraction refuses, as it refuses it when it is applied, and a
/// name it takes that hides a declaration of another module, with that declaration. No other answer is checked.
/// </summary>
[TestClass]
public sealed class ExtractMethodCheckTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-extractmethod-check-ws");

    // line 5 computes b from a, which line 6 prints.
    private const string Main =
        "Option Explicit\r\n" +
        "Public Sub Run(ByVal n As Long)\r\n" +
        "    Dim a As Long\r\n" +
        "    Dim b As Long\r\n" +
        "    a = n\r\n" +
        "    b = a + 1\r\n" +
        "    Debug.Print b\r\n" +
        "End Sub\r\n";

    private const string Other =
        "Public Sub Report()\r\n" +
        "End Sub\r\n";

    private static readonly SourceRange Selection = new(5, 0, 6, 0);

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly ExtractMethodService _service;

    public ExtractMethodCheckTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        var documents = new[] { new WorkspaceDocument("Main.bas", Root, Main, version: 1), new WorkspaceDocument("Other.bas", Root, Other, version: 1) };
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
        _service = new ExtractMethodService(rename, _parsing, options);
    }

    private static Uri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName)).ToUri();

    private static RefactorAnswer Answer(string id, string value) => new() { Id = id, Value = value };

    [TestMethod]
    [DataRow("If", DisplayName = "a keyword")]
    [DataRow("Run", DisplayName = "a name the module declares")]
    [DataRow("a b", DisplayName = "no identifier")]
    public async Task ANameTheCodeCannotDeclare_IsRefused_AsApplyingItIs(string name)
    {
        var answers = new[] { Answer(ExtractMethodService.NameChoice, name) };

        var notice = (await _service.CheckIdentifierAsync(Document("Main.bas"), Selection, answers, CancellationToken.None)).Notices.Single();
        var applied = await _service.ApplyAsync(Document("Main.bas"), Selection, answers, [], CancellationToken.None);

        Assert.AreEqual(RefactorNoticeKind.Refusal, notice.Kind);
        Assert.AreEqual(ExtractMethodService.NameChoice, notice.Choice);
        Assert.IsNull(notice.Item);
        Assert.AreEqual(applied.Refusal, notice.Message);
    }

    [TestMethod]
    public async Task ANameThatHidesAProcedureOfAnotherModule_IsANote_WithTheDeclarationItHides_AndIsTaken()
    {
        var answers = new[] { Answer(ExtractMethodService.NameChoice, "Report") };

        var notice = (await _service.CheckIdentifierAsync(Document("Main.bas"), Selection, answers, CancellationToken.None)).Notices.Single();
        var applied = await _service.ApplyAsync(Document("Main.bas"), Selection, answers, [], CancellationToken.None);

        Assert.AreEqual(RefactorNoticeKind.Note, notice.Kind);
        Assert.AreEqual("extractMethod.nameShadows", notice.Code);
        Assert.AreEqual(ExtractMethodService.NameChoice, notice.Choice);
        Assert.AreEqual(new SourceLocation(Document("Other.bas"), new SourceRange(0, 11, 0, 17)), notice.Related);
        Assert.IsNull(applied.Refusal, applied.Refusal);
        Assert.IsNotNull(applied.Edit);
    }

    [TestMethod]
    [DataRow("Increment", DisplayName = "a name nothing declares")]
    [DataRow("", DisplayName = "no name, which takes the default")]
    public async Task ANameTheExtractionTakesAsItIs_RaisesNothing(string name)
    {
        var checkedAnswers = await _service.CheckIdentifierAsync(Document("Main.bas"), Selection,
            [Answer(ExtractMethodService.NameChoice, name), Answer(ExtractMethodService.ReturnValueChoice, "b")], CancellationToken.None);

        Assert.IsFalse(checkedAnswers.Notices.Any(), string.Join("; ", checkedAnswers.Notices.Select(notice => notice.Message)));
    }

    // a value to return is chosen from the options rdcore/refactor/prepare offers, and rdcore/refactor/apply refuses one that is not among them.
    [TestMethod]
    public async Task AValueToReturnThatTheSelectionDoesNotGiveBack_IsNotChecked()
    {
        var answers = new[] { Answer(ExtractMethodService.NameChoice, "Increment"), Answer(ExtractMethodService.ReturnValueChoice, "a") };

        var checkedAnswers = await _service.CheckIdentifierAsync(Document("Main.bas"), Selection, answers, CancellationToken.None);

        Assert.IsFalse(checkedAnswers.Notices.Any(), string.Join("; ", checkedAnswers.Notices.Select(notice => notice.Message)));
    }

    // a range the extraction refuses is refused by rdcore/refactor/prepare, whatever the answers.
    [TestMethod]
    public async Task TheAnswersOfARangeThatIsRefused_RaiseNothing()
    {
        var checkedAnswers = await _service.CheckIdentifierAsync(Document("Main.bas"), new SourceRange(0, 0, 1, 0), [Answer(ExtractMethodService.NameChoice, "If")], CancellationToken.None);

        Assert.IsFalse(checkedAnswers.Notices.Any());
    }
}
