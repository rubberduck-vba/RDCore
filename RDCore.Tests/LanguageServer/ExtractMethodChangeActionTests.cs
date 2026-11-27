using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
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
using RDCore.SDK.Server.Services;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// Extract Method that changes what the code does, offered as a code action to a client with no dialog: to one that takes change annotations, with its edit,
/// which the client asks its user to confirm, the changes in the annotation's description; to one that takes none, disabled, with the change.
/// </summary>
[TestClass]
public sealed class ExtractMethodChangeActionTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-extractmethod-change-ws");

    // Run's last line reads Err under the On Error Resume Next before it, which the extracted procedure's own clears: a change of what the code does.
    private const string Main =
        "Option Explicit\r\n" +
        "Public Sub Run(ByVal n As Long)\r\n" +
        "    On Error Resume Next\r\n" +
        "    Debug.Print 1 / n\r\n" +
        "    Debug.Print Err.Number\r\n" +
        "End Sub\r\n";

    // Run's handler resumes after an error of the selection, which raised at the call skips the rest of the extracted code: a change of what the code does.
    // Twin has no handler, so the same statements there are a duplicate whose replacement changes nothing.
    private const string Pair =
        "Option Explicit\r\n" +
        "Public Sub Run(ByVal n As Long)\r\n" +
        "    On Error GoTo Handler\r\n" +
        "    Debug.Print n\r\n" +
        "    Debug.Print 1 / n: Debug.Print n\r\n" +
        "    Exit Sub\r\n" +
        "Handler:\r\n" +
        "    Resume Next\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub Twin(ByVal m As Long)\r\n" +
        "    Debug.Print 1 / m: Debug.Print m\r\n" +
        "End Sub\r\n";

    private static readonly SourceRange Selection = new(4, 0, 5, 0);

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly IPlatformClientCapabilitiesService _capabilities = Substitute.For<IPlatformClientCapabilitiesService>();
    private readonly RenameService _rename;
    private readonly IOptions<SdkAppOptions> _options =
        Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });

    public ExtractMethodChangeActionTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        WorkspaceDocument[] documents = [new("Main.bas", Root, Main, version: 3), new("Pair.bas", Root, Pair, version: 5)];
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
            => [.. new LibrarySymbolProvider(
                call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        _rename = new RenameService(
            _documents, _parsing, new IntrinsicSymbolResolver(), libraries, _options, NullLogger<RenameService>.Instance, OneProject.Registry());
    }

    private static Uri DocumentOf(string name) => DocumentUri.From(Path.Combine(Root, $"{name}.bas")).ToUri();

    private ExtractMethodService Sut() => new(_rename, _parsing, _options);

    // the changes of what the code does the extraction of the selection makes: the test's premise.
    private async Task<IReadOnlyList<RefactorNotice>> ChangesAsync(string module)
    {
        var changes = (await Sut().PrepareAsync(DocumentOf(module), Selection, CancellationToken.None)).Notices
            .Where(notice => notice.Kind is RefactorNoticeKind.Change).ToArray();
        Assert.IsNotEmpty(changes, $"the extraction in {module} changes what the code does: the test needs another selection if it does not");
        return changes;
    }

    [TestMethod]
    public async Task AClientThatTakesChangeAnnotations_IsSentTheEdit_ForItsUserToConfirm_WithTheChanges()
    {
        var changes = await ChangesAsync("Main");

        var action = (await ActionAsync("Main", annotations: true))!;

        Assert.IsNull(action.Disabled, action.Disabled?.Reason);
        AssertConfirmed(action.Edit!, changes, duplicates: 0);
        var accepted = await Sut().ApplyAsync(DocumentOf("Main"), Selection, [], [.. changes.Select(change => change.Code)], CancellationToken.None);
        CollectionAssert.AreEqual(
            accepted.Edit!.Documents.Single().Edits.Select(each => (each.Range.Start.Line, each.Range.Start.Character, each.NewText)).ToArray(),
            EditsOf(action.Edit!).Select(each => (each.Range.Start.Line, each.Range.Start.Character, each.NewText)).ToArray(),
            "the edit is the extraction with the defaults, its changes accepted");
    }

    [TestMethod]
    public async Task AClientThatResolvesTheEdit_IsSentItConfirmed_WhenItResolvesIt()
    {
        var changes = await ChangesAsync("Main");
        var handler = Handler(annotations: true, resolvesEdits: true);

        var offered = (await ActionAsync(handler, "Main"))!;
        var resolved = await handler.Handle(offered, CancellationToken.None);

        Assert.IsNull(offered.Edit);
        AssertConfirmed(resolved.Edit!, changes, duplicates: 0);
    }

    [TestMethod]
    public async Task AClientThatTakesNoChangeAnnotations_IsOfferedTheExtractionDisabled_WithTheChange()
    {
        var changes = await ChangesAsync("Main");

        var hidden = await ActionAsync("Main", annotations: false);
        var shown = (await ActionAsync(Handler(annotations: false, showsDisabled: true), "Main"))!;

        Assert.IsNull(hidden);
        Assert.IsNull(shown.Edit);
        Assert.AreEqual(ExtractMethodService.NeedsAccepting(changes[0]), shown.Disabled!.Reason);
    }

    [TestMethod]
    public async Task ADuplicate_KeepsItsOwnConfirmation_AndTheRestOfTheEditIsConfirmedWithTheChanges()
    {
        var changes = await ChangesAsync("Pair");
        var applied = await Sut().ApplyAsync(DocumentOf("Pair"), Selection, [], [.. changes.Select(change => change.Code)], CancellationToken.None);
        Assert.HasCount(1, applied.Replacements, "the selection in Pair has a duplicate in Twin: the test needs another one if it does not");

        var action = (await ActionAsync("Pair", annotations: true))!;

        AssertConfirmed(action.Edit!, changes, duplicates: 1);
    }

    // every text edit is annotated, and each one that is not a duplicate's replacement by the one annotation of the changes, which the user confirms: labeled
    // with the refactoring, described with each change.
    private static void AssertConfirmed(WorkspaceEdit edit, IReadOnlyList<RefactorNotice> changes, int duplicates)
    {
        var annotations = edit.ChangeAnnotations!;
        var change = annotations.Single(each => each.Value.Label == ExtractMethodService.Title(RefactoringKinds.ExtractMethod));
        Assert.IsTrue(change.Value.NeedsConfirmation);
        foreach (var notice in changes)
        {
            StringAssert.Contains(change.Value.Description, notice.Message);
        }

        Assert.HasCount(duplicates + 1, annotations);
        Assert.IsTrue(annotations.All(each => each.Value.NeedsConfirmation));
        var edits = EditsOf(edit).ToArray();
        var annotated = edits.OfType<AnnotatedTextEdit>().ToArray();
        Assert.HasCount(edits.Length, annotated, "every edit is annotated");
        var ownAnnotations = annotated.Select(each => each.AnnotationId.Identifier).Distinct().ToArray();
        CollectionAssert.AreEquivalent(annotations.Keys.Select(key => key.Identifier).ToArray(), ownAnnotations, "each annotation is of an edit");
    }

    private static IEnumerable<TextEdit> EditsOf(WorkspaceEdit edit) => edit.DocumentChanges!.SelectMany(document => document.TextDocumentEdit!.Edits);

    private Task<CodeAction?> ActionAsync(string module, bool annotations) => ActionAsync(Handler(annotations), module);

    // the extraction of the selection a client with no dialog is offered.
    private static async Task<CodeAction?> ActionAsync(CodeActionHandler handler, string module)
    {
        var actions = await handler.Handle(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(DocumentOf(module))),
            Range = new Range(Selection.Start.Line, Selection.Start.Character, Selection.End.Line, Selection.End.Character),
            Context = new CodeActionContext
            {
                Diagnostics = new Container<Diagnostic>(), Only = new Container<CodeActionKind>(CodeActionHandler.RefactorExtractMethod),
            },
        }, CancellationToken.None);
        return actions!.Select(action => action.CodeAction!).SingleOrDefault(action => action.Kind == CodeActionHandler.RefactorExtractMethod);
    }

    private CodeActionHandler Handler(bool annotations, bool showsDisabled = false, bool resolvesEdits = false)
    {
        var handler = new CodeActionHandler(_rename, _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem(), Sut(),
            new MoveMemberService(_rename, _parsing), _capabilities, new OmniSharp.Extensions.LanguageServer.Protocol.Serialization.LspSerializer());
        ((OmniSharp.Extensions.LanguageServer.Protocol.IRegistration<CodeActionRegistrationOptions, CodeActionCapability>)handler).GetRegistrationOptions(
            new CodeActionCapability(),
            new ClientCapabilities
            {
                Workspace = new WorkspaceClientCapabilities
                {
                    WorkspaceEdit = new WorkspaceEditCapability
                    {
                        DocumentChanges = true,
                        ChangeAnnotationSupport = annotations ? new WorkspaceEditSupportCapabilitiesChangeAnnotationSupport() : null,
                    },
                },
                TextDocument = new TextDocumentClientCapabilities
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
}
