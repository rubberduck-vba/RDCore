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
/// Convert GoSub as a client asks it of a label, or of a <c>GoSub</c> that names it: what it asks and refuses, and the edit it makes.
/// </summary>
[TestClass]
public sealed class ConvertGoSubServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-convertgosub-ws");

    // Twice, lines 8 to 10, doubles total for the GoSubs of lines 4 and 5; the statement of line 14 falls into Again.
    private const string Module =
        "Option Explicit\r\n" +
        "Public Sub Run(ByVal n As Long)\r\n" +
        "    Dim total As Long\r\n" +
        "    total = n\r\n" +
        "    GoSub Twice\r\n" +
        "    GoSub Twice\r\n" +
        "    Debug.Print total\r\n" +
        "    Exit Sub\r\n" +
        "Twice:\r\n" +
        "    total = total * 2\r\n" +
        "    Return\r\n" +
        "End Sub\r\n" +
        "Public Sub Falls(ByVal n As Long)\r\n" +
        "    GoSub Again\r\n" +
        "    Debug.Print n\r\n" +
        "Again:\r\n" +
        "    Debug.Print n\r\n" +
        "    Return\r\n" +
        "End Sub\r\n" +
        // Outer, lines 25 to 28, runs Inner, lines 29 to 31.
        "Public Sub Nested(ByVal n As Long)\r\n" +
        "    Dim total As Long\r\n" +
        "    total = n\r\n" +
        "    GoSub Outer\r\n" +
        "    Debug.Print total\r\n" +
        "    Exit Sub\r\n" +
        "Outer:\r\n" +
        "    total = total + 1\r\n" +
        "    GoSub Inner\r\n" +
        "    Return\r\n" +
        "Inner:\r\n" +
        "    total = total * 2\r\n" +
        "    Return\r\n" +
        "End Sub\r\n" +
        // Fail, line 35, is a label that no GoSub names.
        "Public Sub Handled()\r\n" +
        "    Exit Sub\r\n" +
        "Fail:\r\n" +
        "    Debug.Print 1\r\n" +
        "End Sub\r\n";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly RenameService _rename;
    private readonly IOptions<SdkAppOptions> _options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });

    public ConvertGoSubServiceTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        var module = new WorkspaceDocument("Main.bas", Root, Module, version: 1);
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

    private static SourceRange At(int line, int character) => new(line, character, line, character);

    [TestMethod]
    [DataRow(8, 0, DisplayName = "on the label")]
    [DataRow(4, 12, DisplayName = "on a GoSub that names it")]
    public async Task TheRoutine_IsAskedTheName_FromItsLabel_AndShowsItsParameters_ButNoValueToReturn(int line, int character)
    {
        var prepared = await Sut().PrepareAsync(RefactoringKinds.ConvertGoSub, Document, At(line, character), CancellationToken.None);

        Assert.IsFalse(prepared.Notices.Any(notice => notice.Kind is RefactorNoticeKind.Refusal), string.Join("; ", prepared.Notices.Select(notice => notice.Message)));
        CollectionAssert.AreEqual(new[] { ExtractMethodService.NameChoice, ExtractMethodService.ParametersChoice, ExtractMethodService.AccessibilityChoice },
            prepared.Choices.Select(choice => choice.Id).ToArray());
        Assert.AreEqual("TwiceProc", prepared.Choices[0].Default);
        Assert.AreEqual("ByRef total As Long", prepared.Choices[1].Items.Single().Label);
        Assert.AreEqual(new SourceRange(8, 0, 10, 10), prepared.Range);
    }

    [TestMethod]
    public async Task APositionOnNoRoutine_IsRefused()
    {
        var prepared = await Sut().PrepareAsync(RefactoringKinds.ConvertGoSub, Document, At(3, 4), CancellationToken.None);

        Assert.AreEqual(RefactorNoticeKind.Refusal, prepared.Notices.Single().Kind);
        Assert.IsFalse(prepared.Choices.Any());
    }

    [TestMethod]
    public async Task ARoutineTheCodeBeforeFallsInto_IsRefused_SayingWhere()
    {
        var prepared = await Sut().PrepareAsync(RefactoringKinds.ConvertGoSub, Document, At(15, 0), CancellationToken.None);

        var refusal = prepared.Notices.First(notice => notice.Kind is RefactorNoticeKind.Refusal);
        Assert.AreEqual(14, refusal.Location?.Range.Start.Line, refusal.Message);
        Assert.IsFalse(prepared.Choices.Any());
    }

    [TestMethod]
    public async Task TheDefaults_ConvertTheRoutine_EachGoSubACall()
    {
        var answer = await Sut().ApplyAsync(RefactoringKinds.ConvertGoSub, Document, At(8, 0), [], [], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var text = Applied(Module, answer.Edit!.Documents.Single().Edits);
        StringAssert.Contains(text, "    total = n\r\n    TwiceProc total\r\n    TwiceProc total\r\n    Debug.Print total\r\n    Exit Sub\r\nEnd Sub", text);
        StringAssert.Contains(text, "Private Sub TwiceProc(ByRef total As Long)\r\n    total = total * 2\r\nEnd Sub", text);
    }

    // a client with a dialog prepares and applies it by its kind.
    [TestMethod]
    public async Task TheRefactorRequests_ServeIt_ByItsKind()
    {
        var capabilities = Substitute.For<RDCore.SDK.Server.Services.IPlatformClientCapabilitiesService>();
        capabilities.Expects(Arg.Any<Func<RDCore.SDK.Client.LanguageServerCapabilities, RDCore.SDK.Client.CorePlatformClientCapability>>()).Returns(true);

        var prepared = await new RefactorPrepareHandler(Sut(), capabilities).Handle(
            new RefactorPrepareRequest { Kind = RefactoringKinds.ConvertGoSub, Uri = Document, Range = At(8, 0) }, CancellationToken.None);
        var applied = await new RefactorApplyHandler(Sut(), capabilities).Handle(
            new RefactorApplyRequest { Kind = RefactoringKinds.ConvertGoSub, Uri = Document, Range = At(8, 0) }, CancellationToken.None);

        Assert.AreEqual("TwiceProc", prepared.Choices[0].Default);
        StringAssert.Contains(Applied(Module, applied.Documents.Single().Edits), "Private Sub TwiceProc(");
    }

    [TestMethod]
    public async Task AValueToReturn_IsRefused()
    {
        var answer = await Sut().ApplyAsync(RefactoringKinds.ConvertGoSub, Document, At(8, 0),
            [new RefactorAnswer { Id = ExtractMethodService.ReturnValueChoice, Value = "total" }], [], CancellationToken.None);

        Assert.IsNull(answer.Edit);
        Assert.IsFalse(string.IsNullOrEmpty(answer.Refusal));
    }

    [TestMethod]
    [DataRow(8, 0, 8, 0, DisplayName = "at the label")]
    [DataRow(4, 12, 4, 12, DisplayName = "at a GoSub that names it")]
    [DataRow(10, 6, 10, 6, DisplayName = "at its Return")]
    [DataRow(3, 0, 6, 0, DisplayName = "a selection refused for a GoSub to it")]
    [DataRow(9, 0, 11, 0, DisplayName = "a selection refused for its Return")]
    public async Task AClientWithADialog_IsOfferedTheConversion_AsTheRefactorCommand(int startLine, int startCharacter, int endLine, int endCharacter)
    {
        var action = await ConversionActionAsync(new SourceRange(startLine, startCharacter, endLine, endCharacter), dialog: true);

        Assert.IsNotNull(action);
        Assert.AreEqual(RefactoringCommands.Refactor, action.Command!.Name);
        var prepare = action.Command.Arguments!.Single();
        Assert.AreEqual(RefactoringKinds.ConvertGoSub, prepare["kind"]!.ToString());
        var prepared = await Sut().PrepareAsync(RefactoringKinds.ConvertGoSub, Document,
            new SourceRange((int)prepare["range"]!["start"]!["line"]!, (int)prepare["range"]!["start"]!["character"]!,
                (int)prepare["range"]!["end"]!["line"]!, (int)prepare["range"]!["end"]!["character"]!), CancellationToken.None);
        Assert.AreEqual("TwiceProc", prepared.Choices[0].Default);
    }

    [TestMethod]
    public async Task AClientWithNoDialog_IsSentTheConversion_MadeWithTheDefaults()
    {
        var action = await ConversionActionAsync(At(8, 0), dialog: false);

        Assert.IsNotNull(action);
        Assert.IsNull(action.Command);
        var edits = action.Edit!.DocumentChanges?.Single().TextDocumentEdit!.Edits ?? action.Edit.Changes![DocumentUri.From(Document)];
        Assert.IsTrue(edits.Any(edit => edit.NewText.Contains("Private Sub TwiceProc(", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ARoutineThatRunsAnother_IsOfferedToConvertEveryRoutine_Ticked_ItsGoSubANote()
    {
        var outer = await Sut().PrepareAsync(RefactoringKinds.ConvertGoSub, Document, At(25, 0), CancellationToken.None);
        var inner = await Sut().PrepareAsync(RefactoringKinds.ConvertGoSub, Document, At(29, 0), CancellationToken.None);

        Assert.AreEqual(string.Empty, string.Join("; ", outer.Notices.Where(notice => notice.Kind is RefactorNoticeKind.Refusal).Select(notice => $"{notice.Code}: {notice.Message}")));
        Assert.AreEqual(27, outer.Notices.Single(notice => notice.Kind is RefactorNoticeKind.Note).Location?.Range.Start.Line);
        Assert.AreEqual("true", outer.Choices.Single(choice => choice.Id == ExtractMethodService.ConvertAllChoice).Default);
        Assert.AreEqual("false", inner.Choices.Single(choice => choice.Id == ExtractMethodService.ConvertAllChoice).Default);
        Assert.IsFalse(prepared(await Sut().PrepareAsync(RefactoringKinds.ConvertGoSub, Document, At(8, 0), CancellationToken.None)).Contains(ExtractMethodService.ConvertAllChoice),
            "Run has one routine");

        static string[] prepared(RefactorPrepareResult result) => [.. result.Choices.Select(choice => choice.Id)];
    }

    [TestMethod]
    public async Task EveryRoutine_IsConverted_InnermostFirst_ByDefaultFromOneThatRunsAnother()
    {
        var answer = await Sut().ApplyAsync(RefactoringKinds.ConvertGoSub, Document, At(25, 0), [], [], CancellationToken.None);

        Assert.AreEqual(null, answer.Refusal);
        var text = Applied(Module, answer.Edit!.Documents.Single().Edits);
        StringAssert.Contains(text, "    total = n\r\n    OuterProc total\r\n    Debug.Print total\r\n    Exit Sub\r\nEnd Sub", text);
        StringAssert.Contains(text, "Private Sub OuterProc(ByRef total As Long)\r\n    total = total + 1\r\n    InnerProc total\r\nEnd Sub", text);
        StringAssert.Contains(text, "Private Sub InnerProc(ByRef total As Long)\r\n    total = total * 2\r\nEnd Sub", text);
        Assert.IsFalse(text.Contains("GoSub Outer", StringComparison.Ordinal) || text.Contains("GoSub Inner", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public async Task TheRoutineAskedFor_TakesTheAnswers_AndTheOthersTheirDefaults()
    {
        var answer = await Sut().ApplyAsync(RefactoringKinds.ConvertGoSub, Document, At(29, 0),
            [new RefactorAnswer { Id = ExtractMethodService.ConvertAllChoice, Value = "true" }, new RefactorAnswer { Id = ExtractMethodService.NameChoice, Value = "Doubled" }],
            [], CancellationToken.None);

        Assert.IsNull(answer.Refusal, answer.Refusal);
        var text = Applied(Module, answer.Edit!.Documents.Single().Edits);
        StringAssert.Contains(text, "Private Sub OuterProc(ByRef total As Long)\r\n    total = total + 1\r\n    Doubled total\r\nEnd Sub", text);
        StringAssert.Contains(text, "Private Sub Doubled(ByRef total As Long)", text);
    }

    [TestMethod]
    public async Task ARoutineThatRunsAnother_IsRefused_Alone()
    {
        var answer = await Sut().ApplyAsync(RefactoringKinds.ConvertGoSub, Document, At(25, 0),
            [new RefactorAnswer { Id = ExtractMethodService.ConvertAllChoice, Value = "false" }], [], CancellationToken.None);

        Assert.IsNull(answer.Edit);
        Assert.IsFalse(string.IsNullOrEmpty(answer.Refusal));
    }

    [TestMethod]
    public async Task AConversionResolved_ForAnotherVersionOfTheDocument_IsRefused_AsAConversion()
    {
        var handler = new CodeActionHandler(_rename, _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem(), Sut(),
            Substitute.For<RDCore.SDK.Server.Services.IPlatformClientCapabilitiesService>(), new OmniSharp.Extensions.LanguageServer.Protocol.Serialization.LspSerializer());
        var offered = new OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeAction
        {
            Title = ExtractMethodService.Title(RefactoringKinds.ConvertGoSub),
            Kind = CodeActionHandler.RefactorConvertGoSub,
            Data = new Newtonsoft.Json.Linq.JObject
            {
                ["refactoring"] = RefactoringKinds.ConvertGoSub,
                ["uri"] = DocumentUri.From(Document).ToString(),
                ["range"] = Newtonsoft.Json.Linq.JObject.FromObject(new { start = new { line = 8, character = 0 }, end = new { line = 8, character = 0 } }),
                ["version"] = 0,
            },
        };

        var refused = await Assert.ThrowsAsync<OmniSharp.Extensions.JsonRpc.RpcErrorException>(() => handler.Handle(offered, CancellationToken.None));

        Assert.AreEqual(ExtractMethodService.DocumentChanged(RefactoringKinds.ConvertGoSub), refused.Message);
        Assert.AreNotEqual(ExtractMethodService.DocumentChanged(), refused.Message);
    }

    [TestMethod]
    [DataRow(3, 4, 3, 4, DisplayName = "a position on no routine")]
    [DataRow(15, 0, 15, 0, DisplayName = "a routine the code before falls into")]
    [DataRow(2, 0, 3, 0, DisplayName = "a selection refused for nothing of a routine")]
    public async Task NoConversion_IsOffered_ForWhatHasNoRoutineToConvert(int startLine, int startCharacter, int endLine, int endCharacter)
    {
        Assert.IsNull(await ConversionActionAsync(new SourceRange(startLine, startCharacter, endLine, endCharacter), dialog: true));
    }

    [TestMethod]
    public async Task ARefusedRoutine_IsOfferedDisabled_WithWhy_ToAClientThatShowsSo()
    {
        var refusal = (await Sut().PrepareAsync(RefactoringKinds.ConvertGoSub, Document, At(15, 0), CancellationToken.None)).Notices
            .First(notice => notice.Kind is RefactorNoticeKind.Refusal);

        var action = await ConversionActionAsync(At(15, 0), dialog: true, showsDisabled: true);

        Assert.IsNotNull(action);
        Assert.AreEqual(refusal.Message, action.Disabled?.Reason);
        Assert.IsNull(action.Command);
        Assert.IsNull(action.Edit);
    }

    [TestMethod]
    [DataRow(3, 4, DisplayName = "a position on no label")]
    [DataRow(35, 0, DisplayName = "a label that no GoSub names")]
    public async Task APositionOnNoRoutine_IsNotOfferedDisabled(int line, int character)
        => Assert.IsNull(await ConversionActionAsync(At(line, character), dialog: true, showsDisabled: true));

    // the conversion a client is offered for a range, among the code actions of the document; showsDisabled, when it shows an action it cannot take.
    private async Task<OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeAction?> ConversionActionAsync(SourceRange range, bool dialog, bool showsDisabled = false)
    {
        var capabilities = Substitute.For<RDCore.SDK.Server.Services.IPlatformClientCapabilitiesService>();
        capabilities.Expects(Arg.Any<Func<RDCore.SDK.Client.LanguageServerCapabilities, RDCore.SDK.Client.CorePlatformClientCapability>>()).Returns(dialog);
        var handler = new CodeActionHandler(_rename, _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem(), Sut(), capabilities,
            new OmniSharp.Extensions.LanguageServer.Protocol.Serialization.LspSerializer());
        ((OmniSharp.Extensions.LanguageServer.Protocol.IRegistration<OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeActionRegistrationOptions,
            OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.CodeActionCapability>)handler)
            .GetRegistrationOptions(new(), new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.ClientCapabilities
            {
                TextDocument = new() { CodeAction = new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.CodeActionCapability { DisabledSupport = showsDisabled } },
            });
        var actions = await handler.Handle(new OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeActionParams
        {
            TextDocument = new OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentIdentifier(DocumentUri.From(Document)),
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character),
            Context = new OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeActionContext
            {
                Diagnostics = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(),
            },
        }, CancellationToken.None);
        return actions!.Select(action => action.CodeAction!).SingleOrDefault(action => action.Kind == CodeActionHandler.RefactorConvertGoSub);
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
