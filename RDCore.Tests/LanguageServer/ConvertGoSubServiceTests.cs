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
        CollectionAssert.AreEqual(new[] { ExtractMethodService.NameChoice, ExtractMethodService.ParametersChoice }, prepared.Choices.Select(choice => choice.Id).ToArray());
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
