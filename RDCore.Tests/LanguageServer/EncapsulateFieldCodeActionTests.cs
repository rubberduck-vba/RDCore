using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NSubstitute;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Encapsulate Field offered as a code action: with its edit, or resolved; disabled with why, when it is refused or changes what the code does; nothing where
/// no field is named; a command for a client with a dialog of its own, which prepares and applies it by its kind.
/// </summary>
[TestClass]
public sealed class EncapsulateFieldCodeActionTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-encapsulatefield-actions-ws");

    private const int Version = 3;

    private const string Fields =
        "Option Explicit\r\n" +
        "Public total As Long\r\n" +
        "Public count As Long\r\n" +
        "Private hidden As Long\r\n" +
        "\r\n" +
        "Public Sub Reset()\r\n" +
        "    total = 0\r\n" +
        "End Sub\r\n";

    private const string Main =
        "Option Explicit\r\n" +
        "Private Sub Bump(ByRef n As Long)\r\n" +
        "    n = n + 1\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub Run()\r\n" +
        "    Bump count\r\n" +
        "    Debug.Print total\r\n" +
        "End Sub\r\n";

    private const string Texts =
        "Option Explicit\r\n" +
        "Public label As String\r\n" +
        "Public note As String\r\n" +
        "\r\n" +
        "Public Sub Retitle()\r\n" +
        "    Mid(note, 1, 1) = \"x\"\r\n" +
        "End Sub\r\n";

    private const string Stamps =
        "Option Explicit\r\n" +
        "Public Sub Stamp()\r\n" +
        "    Mid(Texts.label, 1, 1) = \"x\"\r\n" +
        "End Sub\r\n";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly IPlatformClientCapabilitiesService _capabilities = Substitute.For<IPlatformClientCapabilitiesService>();
    private readonly RenameService _rename;
    private readonly IOptions<SdkAppOptions> _options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });

    public EncapsulateFieldCodeActionTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        var documents = new[]
        {
            new WorkspaceDocument("Fields.bas", Root, Fields, version: Version), new WorkspaceDocument("Main.bas", Root, Main, version: 1),
            new WorkspaceDocument("Texts.bas", Root, Texts, version: 1), new WorkspaceDocument("Stamps.bas", Root, Stamps, version: 1),
        };
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
        _rename = new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, _options, NullLogger<RenameService>.Instance, OneProject.Registry());
    }

    private static DocumentUri Document => DocumentUri.From(Path.Combine(Root, "Fields.bas"));

    private EncapsulateFieldService Sut() => new(_rename, _parsing, _options);

    [TestMethod]
    public async Task AField_IsOfferedItsEncapsulation_WithItsEdit_OfTheVersionOfItsDocument()
    {
        var action = (await ActionsAsync(Handler(), At(1, 8))).Single();

        Assert.AreEqual(CodeActionHandler.RefactorEncapsulateField, action.Kind);
        var edit = action.Edit!.DocumentChanges!.Single().TextDocumentEdit!;
        Assert.AreEqual(Version, edit.TextDocument.Version);
        Assert.IsTrue(edit.Edits.Any(each => each.NewText.Contains("Private Type TFields")), "the private type is declared");
    }

    [TestMethod]
    public async Task AFieldPassedByRefOutsideItsModule_IsOfferedDisabled_WithTheChange_ToAClientThatShowsIt()
    {
        Assert.IsEmpty(await ActionsAsync(Handler(), At(2, 8)));

        var action = (await ActionsAsync(Handler(showsDisabled: true), At(2, 8))).Single();

        Assert.IsNull(action.Edit);
        StringAssert.Contains(action.Disabled!.Reason, "'count'");
    }

    [TestMethod]
    public async Task ARefusedField_IsOfferedDisabled_WithWhy_ToAClientThatShowsIt()
    {
        Assert.IsEmpty(await ActionsAsync(Handler(), At(3, 9)));

        var action = (await ActionsAsync(Handler(showsDisabled: true), At(3, 9))).Single();

        Assert.IsNull(action.Edit);
        StringAssert.Contains(action.Disabled!.Reason, "'hidden'");
    }

    [TestMethod]
    public async Task AFieldAMidStatementWritesOutsideItsModule_IsOfferedDisabled_WithWhy_ToAClientThatShowsIt()
    {
        var texts = DocumentUri.From(Path.Combine(Root, "Texts.bas"));
        Assert.IsEmpty(await ActionsAsync(Handler(), At(1, 8), texts));

        var action = (await ActionsAsync(Handler(showsDisabled: true), At(1, 8), texts)).Single();

        Assert.IsNull(action.Edit);
        StringAssert.Contains(action.Disabled!.Reason, "'label'");
        StringAssert.Contains(action.Disabled!.Reason, "Mid");
    }

    [TestMethod]
    public async Task AFieldAMidStatementWritesInsideItsModule_IsOfferedItsEncapsulation_TheMidWritingTheState()
    {
        var actions = await ActionsAsync(Handler(showsDisabled: true), At(2, 8), DocumentUri.From(Path.Combine(Root, "Texts.bas")));
        var action = actions.Single(each => each.Title == EncapsulateFieldService.Title());

        Assert.IsNull(action.Disabled, action.Disabled?.Reason);
        var edits = action.Edit!.DocumentChanges!.Single().TextDocumentEdit!.Edits;
        Assert.IsTrue(edits.Any(each => each.NewText.Contains("this.note")), "the Mid writes the state");
    }

    [TestMethod]
    public async Task APositionThatNamesNoField_IsOfferedNothing_EvenToAClientThatShowsDisabledActions()
        => Assert.IsEmpty(await ActionsAsync(Handler(showsDisabled: true), At(5, 4)));

    [TestMethod]
    public async Task AnEncapsulationResolved_GetsItsEdit_AndIsRefused_OnceItsDocumentChanged()
    {
        var handler = Handler(resolvesEdits: true);
        var action = (await ActionsAsync(handler, At(1, 8))).Single();
        Assert.IsNull(action.Edit);

        var resolved = await handler.Handle(action, CancellationToken.None);
        Assert.IsTrue(resolved.Edit!.DocumentChanges!.Single().TextDocumentEdit!.Edits.Any(each => each.NewText.Contains("Private Type TFields")));

        ((JObject)action.Data!)["version"] = Version - 1;
        var refused = await Assert.ThrowsExactlyAsync<RpcErrorException>(() => handler.Handle(action, CancellationToken.None));
        Assert.AreEqual(EncapsulateFieldService.DocumentChanged(), refused.Message);
    }

    [TestMethod]
    public async Task TheDialogsAnswers_AreTakenByApply_AndTheBackingFieldNameIsChecked()
    {
        _capabilities.Expects(Arg.Any<Func<RDCore.SDK.Client.LanguageServerCapabilities, RDCore.SDK.Client.CorePlatformClientCapability>>()).Returns(true);
        var range = new RDCore.SDK.Model.Source.SourceRange(1, 8, 1, 8);
        RefactorAnswer[] answers =
        [
            new() { Id = EncapsulateFieldService.ModeChoice, Value = EncapsulateFieldService.BackingFieldMode },
            new() { Id = EncapsulateFieldService.BackingFieldChoice, Value = "hidden" },
        ];

        var checkedName = await new RefactorIdentifierCheckHandler(Substitute.For<IExtractMethodService>(), _capabilities, Sut()).Handle(
            new RefactorCheckIdentifierRequest { Kind = RefactoringKinds.EncapsulateField, Uri = Document.ToUri(), Range = range, Answers = answers }, CancellationToken.None);
        Assert.AreEqual("encapsulateField.nameTaken", checkedName.Notices.Single().Code);

        answers[1] = new() { Id = EncapsulateFieldService.BackingFieldChoice, Value = "mTotal" };
        var applied = await new RefactorApplyHandler(Substitute.For<IExtractMethodService>(), _capabilities, Sut()).Handle(
            new RefactorApplyRequest { Kind = RefactoringKinds.EncapsulateField, Uri = Document.ToUri(), Range = range, Answers = answers }, CancellationToken.None);
        Assert.IsTrue(applied.Documents.Single().Edits.Any(each => each.NewText.Contains("Private mTotal As Long")));
    }

    [TestMethod]
    public async Task AClientWithADialog_IsOfferedTheRefactorCommand_AndTheRefactorRequestsServeItByItsKind()
    {
        _capabilities.Expects(Arg.Any<Func<RDCore.SDK.Client.LanguageServerCapabilities, RDCore.SDK.Client.CorePlatformClientCapability>>()).Returns(true);

        var action = (await ActionsAsync(Handler(), At(2, 8))).Single();
        Assert.AreEqual(RefactoringCommands.Refactor, action.Command!.Name);

        var range = new RDCore.SDK.Model.Source.SourceRange(2, 8, 2, 8);
        var prepared = await new RefactorPrepareHandler(Substitute.For<IExtractMethodService>(), _capabilities, Sut()).Handle(
            new RefactorPrepareRequest { Kind = RefactoringKinds.EncapsulateField, Uri = Document.ToUri(), Range = range }, CancellationToken.None);
        Assert.AreEqual(EncapsulateFieldService.PassedByRefCode, prepared.Notices.Single().Code);

        var applied = await new RefactorApplyHandler(Substitute.For<IExtractMethodService>(), _capabilities, Sut()).Handle(
            new RefactorApplyRequest { Kind = RefactoringKinds.EncapsulateField, Uri = Document.ToUri(), Range = range, Accepted = [EncapsulateFieldService.PassedByRefCode] },
            CancellationToken.None);
        Assert.IsTrue(applied.Documents.Single().Edits.Any(each => each.NewText.Contains("Public Property Get count() As Long")));

        await Assert.ThrowsExactlyAsync<RpcErrorException>(() => new RefactorApplyHandler(Substitute.For<IExtractMethodService>(), _capabilities, Sut()).Handle(
            new RefactorApplyRequest { Kind = RefactoringKinds.EncapsulateField, Uri = Document.ToUri(), Range = range }, CancellationToken.None));
    }

    private static Range At(int line, int character) => new(line, character, line, character);

    private CodeActionHandler Handler(bool showsDisabled = false, bool resolvesEdits = false)
    {
        var handler = new CodeActionHandler(_rename, _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem(), new ExtractMethodService(_rename, _parsing, _options),
            new MoveMemberService(_rename, _parsing), _capabilities, new OmniSharp.Extensions.LanguageServer.Protocol.Serialization.LspSerializer(), encapsulateField: Sut());
        ((IRegistration<CodeActionRegistrationOptions, CodeActionCapability>)handler).GetRegistrationOptions(new(), new ClientCapabilities
        {
            Workspace = new() { WorkspaceEdit = new WorkspaceEditCapability { DocumentChanges = true } },
            TextDocument = new()
            {
                CodeAction = new CodeActionCapability
                {
                    DisabledSupport = showsDisabled,
                    DataSupport = resolvesEdits,
                    ResolveSupport = resolvesEdits ? new CodeActionCapabilityResolveSupportOptions { Properties = new Container<string>("edit") } : null,
                },
            },
        });
        return handler;
    }

    // the encapsulations a client is offered for a range, among the code actions of the document: Fields.bas, unless another is named.
    private static async Task<IReadOnlyList<CodeAction>> ActionsAsync(CodeActionHandler handler, Range range, DocumentUri? document = null)
    {
        var actions = await handler.Handle(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(document ?? Document),
            Range = range,
            Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>(), Only = new Container<CodeActionKind>(CodeActionHandler.RefactorEncapsulateField) },
        }, CancellationToken.None);
        return [.. actions!.Select(action => action.CodeAction!)];
    }
}
