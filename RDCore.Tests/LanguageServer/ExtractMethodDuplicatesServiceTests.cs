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
using RDCore.SDK.Client;
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
/// Extract Method's duplicates as a client asks for them: those it is offered to replace, the calls that replace those the user ticks, in the selection's
/// module and in another, and the confirmation a client with no dialog asks its user for each of them.
/// </summary>
[TestClass]
public sealed class ExtractMethodDuplicatesServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-extractmethod-duplicates-ws");

    // lines 3 and 4 are extracted; Twin, lines 8 and 9, does what they do.
    private const string Main =
        "Option Explicit\r\n" +
        "Public Sub Run(ByVal n As Long)\r\n" +
        "    Dim x As Long\r\n" +
        "    x = n * 2\r\n" +
        "    Debug.Print x\r\n" +
        "End Sub\r\n" +
        "Public Sub Twin(ByVal m As Long)\r\n" +
        "    Dim y As Long\r\n" +
        "    y = m * 2\r\n" +
        "    Debug.Print y\r\n" +
        "End Sub\r\n";

    // lines 3 and 4 do what Main's lines 3 and 4 do.
    private const string Other =
        "Option Explicit\r\n" +
        "Public Sub Elsewhere(ByVal k As Long)\r\n" +
        "    Dim z As Long\r\n" +
        "    z = k * 2\r\n" +
        "    Debug.Print z\r\n" +
        "End Sub\r\n";

    // a class module: lines 8 and 9 are extracted; Twin, lines 13 and 14, does what they do, which nothing in the standard modules does.
    private const string Worker =
        "VERSION 1.0 CLASS\r\n" +
        "BEGIN\r\n" +
        "  MultiUse = -1  'True\r\n" +
        "END\r\n" +
        "Attribute VB_Name = \"Worker\"\r\n" +
        "Option Explicit\r\n" +
        "Public Sub Run(ByVal n As Long)\r\n" +
        "    Dim x As Long\r\n" +
        "    x = n + 7\r\n" +
        "    Debug.Print x\r\n" +
        "End Sub\r\n" +
        "Public Sub Twin(ByVal m As Long)\r\n" +
        "    Dim y As Long\r\n" +
        "    y = m + 7\r\n" +
        "    Debug.Print y\r\n" +
        "End Sub\r\n";

    private const string InMain = "y = m * 2";
    private const string InOther = "z = k * 2";
    private const string InWorker = "y = m + 7";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly IPlatformClientCapabilitiesService _capabilities = Substitute.For<IPlatformClientCapabilitiesService>();
    private readonly RenameService _rename;
    private readonly IOptions<SdkAppOptions> _options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });

    public ExtractMethodDuplicatesServiceTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        WorkspaceDocument[] documents = [new("Main.bas", Root, Main, version: 3), new("Other.bas", Root, Other, version: 5), new("Worker.cls", Root, Worker, version: 7)];
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

    private static Uri DocumentOf(string name) => DocumentUri.From(Path.Combine(Root, $"{name}.bas")).ToUri();

    private static Uri ClassDocument => DocumentUri.From(Path.Combine(Root, "Worker.cls")).ToUri();

    private static readonly SourceRange Selection = new(3, 0, 5, 0);

    private static readonly SourceRange ClassSelection = new(8, 0, 10, 0);

    private ExtractMethodService Sut() => new(_rename, _parsing, _options);

    private static RefactorAnswer Answer(string id, string value) => new() { Id = id, Value = value };

    // the duplicates' answer: each duplicate whose first line is given, ticked or not.
    private async Task<RefactorAnswer> TickedAsync(params (string FirstLine, bool Replace)[] ticks)
    {
        var items = (await Sut().PrepareAsync(DocumentOf("Main"), Selection, CancellationToken.None)).Choices.Single(choice => choice.Id == ExtractMethodService.DuplicatesChoice).Items;
        return new RefactorAnswer
        {
            Id = ExtractMethodService.DuplicatesChoice,
            Items =
            [
                .. ticks.Select(tick => new RefactorItemAnswer
                {
                    Id = items.Single(item => item.Label.EndsWith(tick.FirstLine, StringComparison.Ordinal)).Id,
                    Answers = [Answer(ExtractMethodService.DuplicateReplaceChoice, tick.Replace ? "true" : "false")],
                }),
            ],
        };
    }

    [TestMethod]
    public async Task TheDuplicates_AreOfferedToReplace_ThoseOfTheModuleTicked_AndThoseOfAnotherSayingTheyNeedAPublicProcedure()
    {
        var prepared = await Sut().PrepareAsync(DocumentOf("Main"), Selection, CancellationToken.None);

        var replace = prepared.Choices.Single(choice => choice.Id == ExtractMethodService.ReplaceDuplicatesChoice);
        Assert.AreEqual("true", replace.Default);
        var items = prepared.Choices.Single(choice => choice.Id == ExtractMethodService.DuplicatesChoice).Items;
        Assert.HasCount(2, items);
        var inMain = items.Single(item => item.Label.EndsWith(InMain, StringComparison.Ordinal));
        Assert.IsNull(inMain.Reason);
        Assert.AreEqual("true", inMain.Choices.Single(choice => choice.Id == ExtractMethodService.DuplicateReplaceChoice).Default);
        var inOther = items.Single(item => item.Label.EndsWith(InOther, StringComparison.Ordinal));
        Assert.IsFalse(string.IsNullOrEmpty(inOther.Reason));
        Assert.AreEqual("false", inOther.Choices.Single(choice => choice.Id == ExtractMethodService.DuplicateReplaceChoice).Default);
    }

    [TestMethod]
    public async Task TheDuplicatesOfAClassMember_AreThoseOfTheClass_OfferedUnticked_SayingTheyNeedAPublicMember()
    {
        var prepared = await Sut().PrepareAsync(ClassDocument, ClassSelection, CancellationToken.None);

        var item = prepared.Choices.Single(choice => choice.Id == ExtractMethodService.DuplicatesChoice).Items.Single();
        StringAssert.EndsWith(item.Label, InWorker);
        Assert.IsFalse(string.IsNullOrEmpty(item.Reason));
        Assert.AreEqual("false", item.Choices.Single(choice => choice.Id == ExtractMethodService.DuplicateReplaceChoice).Default);
    }

    [TestMethod]
    public async Task APublicClassMember_ReplacesTheDuplicateOfTheClass_ByDefault()
    {
        var answer = await Sut().ApplyAsync(ClassDocument, ClassSelection,
            [Answer(ExtractMethodService.NameChoice, "Added"), Answer(ExtractMethodService.AccessibilityChoice, "Public")], [], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var text = Applied(Worker, answer.Edit!.Documents.Single().Edits);
        StringAssert.Contains(text, "Public Sub Run(ByVal n As Long)\r\n    Added n\r\nEnd Sub", text);
        StringAssert.Contains(text, "Public Sub Twin(ByVal m As Long)\r\n    Added m\r\nEnd Sub", text);
        StringAssert.Contains(answer.Replacements.Single().Label, InWorker);
    }

    [TestMethod]
    public async Task APrivateClassMember_ReplacesNoDuplicate_AndIsRefusedOneTicked()
    {
        var untouched = await Sut().ApplyAsync(ClassDocument, ClassSelection, [Answer(ExtractMethodService.NameChoice, "Added")], [], CancellationToken.None);
        var items = (await Sut().PrepareAsync(ClassDocument, ClassSelection, CancellationToken.None)).Choices
            .Single(choice => choice.Id == ExtractMethodService.DuplicatesChoice).Items;
        var ticked = await Sut().ApplyAsync(ClassDocument, ClassSelection,
        [
            Answer(ExtractMethodService.NameChoice, "Added"),
            new RefactorAnswer
            {
                Id = ExtractMethodService.DuplicatesChoice,
                Items = [new RefactorItemAnswer { Id = items.Single().Id, Answers = [Answer(ExtractMethodService.DuplicateReplaceChoice, "true")] }],
            },
        ], [], CancellationToken.None);

        Assert.IsNull(untouched.Refusal, untouched.Refusal);
        Assert.IsEmpty(untouched.Replacements);
        StringAssert.Contains(Applied(Worker, untouched.Edit!.Documents.Single().Edits), "    y = m + 7\r\n");
        Assert.IsNull(ticked.Edit);
        StringAssert.Contains(ticked.Refusal, InWorker);
    }

    [TestMethod]
    public async Task TheDefaults_ReplaceTheDuplicateOfTheModule_WithACall()
    {
        var answer = await Sut().ApplyAsync(DocumentOf("Main"), Selection, [Answer(ExtractMethodService.NameChoice, "Doubled")], [], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var document = answer.Edit!.Documents.Single();
        Assert.AreEqual(DocumentOf("Main").AbsoluteUri, document.Uri!.AbsoluteUri);
        var text = Applied(Main, document.Edits);
        StringAssert.Contains(text, "Public Sub Run(ByVal n As Long)\r\n    Doubled n\r\nEnd Sub");
        StringAssert.Contains(text, "Public Sub Twin(ByVal m As Long)\r\n    Doubled m\r\nEnd Sub");
        var replacement = answer.Replacements.Single();
        Assert.AreEqual(DocumentOf("Main").AbsoluteUri, replacement.Document.AbsoluteUri);
        StringAssert.Contains(replacement.Label, InMain);
        Assert.IsNotEmpty(replacement.Ranges);
    }

    [TestMethod]
    public async Task NoDuplicate_IsReplaced_WhereTheUserReplacesNone()
    {
        var answer = await Sut().ApplyAsync(DocumentOf("Main"), Selection,
            [Answer(ExtractMethodService.NameChoice, "Doubled"), Answer(ExtractMethodService.ReplaceDuplicatesChoice, "false")], [], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        Assert.IsEmpty(answer.Replacements);
        StringAssert.Contains(Applied(Main, answer.Edit!.Documents.Single().Edits), "    Dim y As Long\r\n    y = m * 2\r\n    Debug.Print y\r\n");
    }

    [TestMethod]
    public async Task APublicProcedure_ReplacesTheDuplicateOfAnotherModule_InThatModulesDocument()
    {
        var answer = await Sut().ApplyAsync(DocumentOf("Main"), Selection,
        [
            Answer(ExtractMethodService.NameChoice, "Doubled"),
            Answer(ExtractMethodService.AccessibilityChoice, "Public"),
            await TickedAsync((InMain, false), (InOther, true)),
        ], [], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var main = answer.Edit!.Documents.Single(document => document.Uri!.AbsoluteUri == DocumentOf("Main").AbsoluteUri);
        var other = answer.Edit.Documents.Single(document => document.Uri!.AbsoluteUri == DocumentOf("Other").AbsoluteUri);
        Assert.AreEqual(5, other.Version);
        StringAssert.Contains(Applied(Main, main.Edits), "    y = m * 2\r\n");
        StringAssert.Contains(Applied(Other, other.Edits), "Public Sub Elsewhere(ByVal k As Long)\r\n    Doubled k\r\nEnd Sub");
        Assert.AreEqual(DocumentOf("Other").AbsoluteUri, answer.Replacements.Single().Document.AbsoluteUri);
    }

    [TestMethod]
    public async Task APrivateProcedure_IsRefused_TheDuplicateOfAnotherModule()
    {
        var answer = await Sut().ApplyAsync(DocumentOf("Main"), Selection,
            [Answer(ExtractMethodService.NameChoice, "Doubled"), await TickedAsync((InOther, true))], [], CancellationToken.None);

        Assert.IsNull(answer.Edit);
        StringAssert.Contains(answer.Refusal, InOther);
    }

    [TestMethod]
    public async Task AnItemThatIsNoDuplicate_IsRefused()
    {
        var answer = await Sut().ApplyAsync(DocumentOf("Main"), Selection,
        [
            Answer(ExtractMethodService.NameChoice, "Doubled"),
            new RefactorAnswer
            {
                Id = ExtractMethodService.DuplicatesChoice,
                Items = [new RefactorItemAnswer { Id = "Main:2:5", Answers = [Answer(ExtractMethodService.DuplicateReplaceChoice, "true")] }],
            },
        ], [], CancellationToken.None);

        Assert.IsNull(answer.Edit);
        StringAssert.Contains(answer.Refusal, "Main:2:5");
    }

    [TestMethod]
    public async Task AClientWithNoDialog_ThatTakesChangeAnnotations_AsksItsUserToConfirmEachReplacement()
    {
        var edit = (await ExtractMethodActionAsync(takesAnnotations: true))!.Edit!;

        var annotated = edit.DocumentChanges!.Single().TextDocumentEdit!.Edits.OfType<AnnotatedTextEdit>().ToArray();
        Assert.IsNotEmpty(annotated);
        Assert.IsTrue(annotated.All(each => each.Range.Start.Line is >= 7 and <= 9), "only Twin's edits are annotated");
        var annotation = edit.ChangeAnnotations!.Single();
        Assert.IsTrue(annotated.All(each => each.AnnotationId.Identifier == annotation.Key.Identifier));
        Assert.IsTrue(annotation.Value.NeedsConfirmation);
        StringAssert.Contains(annotation.Value.Label, InMain);
    }

    [TestMethod]
    public async Task AClientWithNoDialog_ThatTakesNoChangeAnnotations_IsSentNoReplacement()
    {
        var edit = (await ExtractMethodActionAsync(takesAnnotations: false))!.Edit!;

        var edits = edit.DocumentChanges!.Single().TextDocumentEdit!.Edits;
        Assert.IsFalse(edits.Any(each => each.Range.Start.Line is >= 7 and <= 9), "Twin is left as it is");
        Assert.IsNull(edit.ChangeAnnotations);
    }

    // the extraction a client with no dialog is offered for the selection, which takes change annotations or not.
    private async Task<CodeAction?> ExtractMethodActionAsync(bool takesAnnotations)
    {
        var handler = new CodeActionHandler(_rename, _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem(), Sut(), _capabilities,
            new OmniSharp.Extensions.LanguageServer.Protocol.Serialization.LspSerializer());
        ((OmniSharp.Extensions.LanguageServer.Protocol.IRegistration<CodeActionRegistrationOptions, CodeActionCapability>)handler).GetRegistrationOptions(
            new CodeActionCapability(),
            new ClientCapabilities
            {
                Workspace = new WorkspaceClientCapabilities
                {
                    WorkspaceEdit = new WorkspaceEditCapability
                    {
                        DocumentChanges = true,
                        ChangeAnnotationSupport = takesAnnotations ? new WorkspaceEditSupportCapabilitiesChangeAnnotationSupport() : null,
                    },
                },
            });
        var actions = await handler.Handle(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(DocumentOf("Main"))),
            Range = new Range(Selection.Start.Line, Selection.Start.Character, Selection.End.Line, Selection.End.Character),
            Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>() },
        }, CancellationToken.None);
        return actions!.Select(action => action.CodeAction!).SingleOrDefault(action => action.Kind == CodeActionHandler.RefactorExtractMethod);
    }

    // the text with the edits made, each of the text as it was before any of them.
    private static string Applied(string text, IReadOnlyList<RefactorTextEdit> edits)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        int Offset(SourcePosition position) => starts[position.Line] + position.Character;
        foreach (var edit in edits.OrderByDescending(edit => Offset(edit.Range.Start)))
        {
            text = string.Concat(text.AsSpan(0, Offset(edit.Range.Start)), edit.NewText, text.AsSpan(Offset(edit.Range.End)));
        }

        return text;
    }
}
