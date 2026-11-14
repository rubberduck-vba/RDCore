using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
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
/// Extract Method as a client asks it of a range of a document: what it asks and refuses, the edit it makes with the user's answers, and the code action that
/// offers it.
/// </summary>
[TestClass]
public sealed class ExtractMethodServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-extractmethod-ws");

    // lines 3 and 4 compute b from a, which line 5 prints; lines 7 to 9 hold an object only they use.
    private const string Module =
        "Option Explicit\r\n" +
        "Public Sub Run(ByVal n As Long)\r\n" +
        "    Dim a As Long\r\n" +
        "    Dim b As Long\r\n" +
        "    a = n\r\n" +
        "    b = a + 1\r\n" +
        "    Debug.Print b\r\n" +
        "End Sub\r\n" +
        "Public Sub Hold(ByVal n As Long)\r\n" +
        "    Dim c As Collection\r\n" +
        "    Set c = New Collection\r\n" +
        "    c.Add n\r\n" +
        "    Debug.Print n\r\n" +
        "End Sub\r\n" +
        "Public Property Get Twice() As Long\r\n" +
        "    Twice = 2\r\n" +
        "End Property\r\n" +
        "Public Property Let Twice(ByVal v As Long)\r\n" +
        "    Dim t As Long\r\n" +
        "    t = v * 2\r\n" +
        "    Debug.Print t\r\n" +
        "End Property\r\n" +
        "Public Sub Jumps()\r\n" +
        "    GoTo Done\r\n" +
        "    Debug.Print 1\r\n" +
        "Done:\r\n" +
        "    Debug.Print 2\r\n" +
        "End Sub\r\n";

    private const int Version = 3;

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly IPlatformClientCapabilitiesService _capabilities = Substitute.For<IPlatformClientCapabilitiesService>();
    private readonly RenameService _rename;
    private readonly IOptions<SdkAppOptions> _options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });

    public ExtractMethodServiceTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        var module = new WorkspaceDocument("Main.bas", Root, Module, version: Version);
        _documents.GetAllDocuments().Returns([module]);
        var uri = module.Id.Uri.ToUri();
        _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = module; return true; });
        _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, Module)));
        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        _rename = new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, _options, NullLogger<RenameService>.Instance, OneProject.Registry());
    }

    private static Uri Document => DocumentUri.From(Path.Combine(Root, "Main.bas")).ToUri();

    private ExtractMethodService Sut() => new(_rename, _parsing, _options);

    // the whole lines first to last (zero-based), as an editor selects them.
    private static SourceRange Lines(int first, int last) => new(first, 0, last + 1, 0);

    private static RefactorAnswer Answer(string id, string value) => new() { Id = id, Value = value };

    [TestMethod]
    public async Task StatementsThatGiveAValueBack_AreAskedTheName_AndTheValueToReturn_AndShowTheirParameters()
    {
        var prepared = await Sut().PrepareAsync(Document, Lines(5, 5), CancellationToken.None);

        Assert.IsFalse(prepared.Notices.Any(), string.Join("; ", prepared.Notices.Select(notice => notice.Message)));
        CollectionAssert.AreEqual(
            new[] { ExtractMethodService.NameChoice, ExtractMethodService.ParametersChoice, ExtractMethodService.ReturnValueChoice, ExtractMethodService.AccessibilityChoice },
            prepared.Choices.Select(choice => choice.Id).ToArray());
        Assert.IsFalse(string.IsNullOrEmpty(prepared.Choices[0].Default));
        var parameters = prepared.Choices[1].Items;
        CollectionAssert.AreEquivalent(new[] { "a", "b" }, parameters.Select(item => item.Id).ToArray());
        Assert.IsTrue(parameters.All(item => item.Reason is { Length: > 0 }));
        Assert.AreEqual("ByVal a As Long", parameters.Single(item => item.Id == "a").Label);
        Assert.AreEqual("ByRef b As Long", parameters.Single(item => item.Id == "b").Label);
        var returned = prepared.Choices[2];
        Assert.AreEqual(string.Empty, returned.Default);
        CollectionAssert.AreEqual(new[] { string.Empty, "b" }, returned.Options.Select(option => option.Value).ToArray());
        var accessibility = prepared.Choices[3];
        Assert.AreEqual("Private", accessibility.Default);
        CollectionAssert.AreEqual(new[] { "Private", "Public" }, accessibility.Options.Select(option => option.Value).ToArray());
    }

    [TestMethod]
    public async Task ThePublicAccessibility_WritesAPublicProcedure()
    {
        var answer = await Sut().ApplyAsync(Document, Lines(5, 5),
            [Answer(ExtractMethodService.NameChoice, "Increment"), Answer(ExtractMethodService.AccessibilityChoice, "Public")], [], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        StringAssert.Contains(Applied(Module, answer.Edit!.Documents.Single().Edits), "\r\nPublic Sub Increment(");
    }

    [TestMethod]
    public async Task ARangeOfNoProcedure_IsRefused()
    {
        var prepared = await Sut().PrepareAsync(Document, Lines(0, 0), CancellationToken.None);

        Assert.AreEqual(RefactorNoticeKind.Refusal, prepared.Notices.Single().Kind);
        Assert.IsFalse(prepared.Choices.Any());
        Assert.IsNull(prepared.Range);
    }

    [TestMethod]
    public async Task ASelectionThatStartsAndEndsInsideStatements_IsPreparedAsTheWholeStatements_WhoseRangeItAnswers()
    {
        var prepared = await Sut().PrepareAsync(Document, new SourceRange(4, 6, 5, 7), CancellationToken.None);

        Assert.IsFalse(prepared.Notices.Any(notice => notice.Kind == RefactorNoticeKind.Refusal), string.Join("; ", prepared.Notices.Select(notice => notice.Message)));
        Assert.AreEqual(new SourceRange(4, 4, 5, 13), prepared.Range);
    }

    [TestMethod]
    public async Task TheDefaults_WriteASubAndItsCall_AsOneEditOfTheDocumentsVersion()
    {
        var answer = await Sut().ApplyAsync(Document, Lines(5, 5), [Answer(ExtractMethodService.NameChoice, "Increment")], [], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var document = answer.Edit!.Documents.Single();
        Assert.AreEqual(Version, document.Version);
        var text = Applied(Module, document.Edits);
        StringAssert.Contains(text, "Private Sub Increment(");
        StringAssert.Matches(text, new System.Text.RegularExpressions.Regex(@"\r\n    Increment (a, b|b, a)\r\n"));
    }

    // a, which line 5 only reads, is passed by value and can be passed by reference; b, which it gives a value, is passed by reference only.
    [TestMethod]
    public async Task TheParameters_CanBeReorderedAndRenamed_AndPassedByReferenceWhereTheyArePassedByValue()
    {
        var prepared = await Sut().PrepareAsync(Document, Lines(5, 5), CancellationToken.None);

        var parameters = prepared.Choices.Single(choice => choice.Id == ExtractMethodService.ParametersChoice);
        Assert.IsTrue(parameters.CanReorder);
        var a = parameters.Items.Single(item => item.Id == "a");
        CollectionAssert.AreEqual(new[] { ExtractMethodService.ParameterNameChoice, ExtractMethodService.ParameterPassingChoice }, a.Choices.Select(choice => choice.Id).ToArray());
        Assert.AreEqual("a", a.Choices[0].Default);
        Assert.AreEqual("ByVal", a.Choices[1].Default);
        CollectionAssert.AreEqual(new[] { "ByVal", "ByRef" }, a.Choices[1].Options.Select(option => option.Value).ToArray());
        var b = parameters.Items.Single(item => item.Id == "b");
        CollectionAssert.AreEqual(new[] { ExtractMethodService.ParameterNameChoice }, b.Choices.Select(choice => choice.Id).ToArray());
    }

    [TestMethod]
    public async Task TheParametersAnswer_IsWhatTheNewProcedureDeclares_AndWhatTheCallPasses()
    {
        var answer = await Sut().ApplyAsync(Document, Lines(5, 5),
        [
            Answer(ExtractMethodService.NameChoice, "Increment"),
            new RefactorAnswer
            {
                Id = ExtractMethodService.ParametersChoice,
                Items =
                [
                    new RefactorItemAnswer { Id = "b" },
                    new RefactorItemAnswer
                    {
                        Id = "a",
                        Answers = [Answer(ExtractMethodService.ParameterNameChoice, "x"), Answer(ExtractMethodService.ParameterPassingChoice, "ByRef")],
                    },
                ],
            },
        ], [], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var text = Applied(Module, answer.Edit!.Documents.Single().Edits);
        StringAssert.Contains(text, "Private Sub Increment(ByRef b As Long, ByRef x As Long)\r\n    b = x + 1\r\n");
        StringAssert.Contains(text, "\r\n    Increment b, a\r\n");
    }

    [TestMethod]
    public async Task AParameterThatCannotBePassedByValue_IsRefused_SayingWhy()
    {
        var answer = await Sut().ApplyAsync(Document, Lines(5, 5),
        [
            new RefactorAnswer
            {
                Id = ExtractMethodService.ParametersChoice,
                Items = [new RefactorItemAnswer { Id = "b", Answers = [Answer(ExtractMethodService.ParameterPassingChoice, "ByVal")] }],
            },
        ], [], CancellationToken.None);

        Assert.IsNull(answer.Edit);
        Assert.AreEqual(new ExtractionParameterProblem(ExtractionParameterProblemKind.PassingIsNotAChoice, "b", "ByVal").ToMessage(), answer.Refusal);
    }

    // line 19 is the Property Let's: its Property Get, of the same name, comes first.
    [TestMethod]
    public async Task ThePropertyLetOfAPair_IsTheProcedureTheSelectionIsExtractedFrom()
    {
        var answer = await Sut().ApplyAsync(Document, Lines(19, 19),
            [Answer(ExtractMethodService.NameChoice, "Doubled"), Answer(ExtractMethodService.ReturnValueChoice, "t")], [], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var text = Applied(Module, answer.Edit!.Documents.Single().Edits);
        StringAssert.Contains(text, "Private Function Doubled(ByVal v As Long) As Long");
        StringAssert.Contains(text, "t = Doubled(v)");
    }

    [TestMethod]
    public async Task AReturnValue_WritesAFunction_WhoseResultTheCallAssigns()
    {
        var answer = await Sut().ApplyAsync(Document, Lines(5, 5),
            [Answer(ExtractMethodService.NameChoice, "Increment"), Answer(ExtractMethodService.ReturnValueChoice, "b")], [], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var text = Applied(Module, answer.Edit!.Documents.Single().Edits);
        StringAssert.Contains(text, "Private Function Increment(");
        StringAssert.Contains(text, "b = Increment(a)");
    }

    [TestMethod]
    [DataRow(ExtractMethodService.NameChoice, "If", DisplayName = "a keyword for the name")]
    [DataRow(ExtractMethodService.NameChoice, "Run", DisplayName = "a name the module declares")]
    [DataRow(ExtractMethodService.ReturnValueChoice, "a", DisplayName = "a value the selection does not give back")]
    [DataRow(ExtractMethodService.AccessibilityChoice, "Friend", DisplayName = "an accessibility the new procedure cannot have")]
    public async Task AnAnswerThatCannotBeWritten_IsRefused_AndSaysWhy(string choice, string value)
    {
        var answer = await Sut().ApplyAsync(Document, Lines(5, 5), [Answer(choice, value)], [], CancellationToken.None);

        Assert.IsNull(answer.Edit);
        Assert.IsFalse(string.IsNullOrEmpty(answer.Refusal));
    }

    [TestMethod]
    public async Task AChangeOfWhatTheCodeDoes_IsAppliedOnlyWhenTheUserAcceptsIt()
    {
        var service = Sut();
        var prepared = await service.PrepareAsync(Document, Lines(9, 11), CancellationToken.None);
        var change = prepared.Notices.Single(notice => notice.Kind is RefactorNoticeKind.Change);

        var declined = await service.ApplyAsync(Document, Lines(9, 11), [], [], CancellationToken.None);
        var accepted = await service.ApplyAsync(Document, Lines(9, 11), [], [change.Code], CancellationToken.None);

        Assert.IsNull(declined.Edit);
        Assert.IsFalse(string.IsNullOrEmpty(declined.Refusal));
        Assert.IsNull(accepted.Refusal, accepted.Refusal);
        Assert.IsNotNull(accepted.Edit);
    }

    [TestMethod]
    public async Task AClientWithADialog_IsOfferedTheRefactorCommand_WithTheRangeToPrepare()
    {
        _capabilities.Expects(Arg.Any<Func<LanguageServerCapabilities, CorePlatformClientCapability>>()).Returns(true);

        var action = await ExtractMethodActionAsync(new Range(5, 0, 6, 0));

        Assert.IsNotNull(action);
        Assert.IsNull(action.Edit);
        Assert.AreEqual(RefactoringCommands.Refactor, action.Command!.Name);
        var prepare = action.Command.Arguments!.Single();
        Assert.AreEqual(RefactoringKinds.ExtractMethod, prepare["kind"]!.ToString());
    }

    [TestMethod]
    public async Task AClientWithNoDialog_IsSentTheEditMadeWithTheDefaults()
    {
        var action = await ExtractMethodActionAsync(new Range(5, 0, 6, 0));

        Assert.IsNotNull(action);
        Assert.IsNull(action.Command);
        var edits = action.Edit!.DocumentChanges?.Single().TextDocumentEdit!.Edits ?? action.Edit.Changes![DocumentUri.From(Document)];
        Assert.IsTrue(edits.Any(edit => edit.NewText.Contains("Private Sub ", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow(9, 0, 12, 0, DisplayName = "a change the user would have to accept")]
    [DataRow(5, 4, 5, 4, DisplayName = "an empty selection")]
    [DataRow(0, 0, 1, 0, DisplayName = "a selection of no procedure")]
    public async Task AClientWithNoDialog_IsNotOfferedAnExtractionItCannotBeSent(int startLine, int startCharacter, int endLine, int endCharacter)
    {
        Assert.IsNull(await ExtractMethodActionAsync(new Range(startLine, startCharacter, endLine, endCharacter)));
    }

    // the GoTo of line 23 lands on the label of line 25, inside the lines 24 to 26.
    [TestMethod]
    public async Task ARefusedExtraction_IsOfferedDisabled_WithWhy_ToAClientThatShowsSo()
    {
        var refusal = (await Sut().PrepareAsync(Document, Lines(24, 26), CancellationToken.None)).Notices.First(notice => notice.Kind is RefactorNoticeKind.Refusal);

        var action = await ExtractMethodActionAsync(new Range(24, 0, 27, 0), showsDisabled: true);

        Assert.IsNotNull(action);
        Assert.AreEqual(refusal.Message, action.Disabled?.Reason);
        Assert.IsNull(action.Edit);
        Assert.IsNull(action.Command);
        Assert.IsNull(action.Data);
    }

    [TestMethod]
    public async Task ARefusedExtraction_IsNotOffered_ToAClientThatDoesNotShowDisabledActions()
        => Assert.IsNull(await ExtractMethodActionAsync(new Range(24, 0, 27, 0)));

    [TestMethod]
    public async Task ASelectionOfNoProcedure_IsNotOfferedDisabled()
        => Assert.IsNull(await ExtractMethodActionAsync(new Range(0, 0, 1, 0), showsDisabled: true));

    // the extraction a client is offered for a range, among the code actions of the document; showsDisabled, when it shows an action it cannot take.
    private async Task<CodeAction?> ExtractMethodActionAsync(Range range, bool showsDisabled = false)
    {
        var handler = new CodeActionHandler(_rename, _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem(), Sut(), _capabilities,
            new OmniSharp.Extensions.LanguageServer.Protocol.Serialization.LspSerializer());
        ((OmniSharp.Extensions.LanguageServer.Protocol.IRegistration<CodeActionRegistrationOptions, OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.CodeActionCapability>)handler)
            .GetRegistrationOptions(new(), new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.ClientCapabilities
            {
                TextDocument = new() { CodeAction = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.CodeActionCapability { DisabledSupport = showsDisabled } },
            });
        var actions = await handler.Handle(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(Document)),
            Range = range,
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
