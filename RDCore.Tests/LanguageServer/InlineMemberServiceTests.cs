using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer;
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
/// Inline Member asked of a document: the call at a position inlined, or every call of the member asked from its declaration, indented, with fresh names
/// counted across the calls of one procedure; the sites the user unticks, the sites that are refused, and the removal of the member.
/// </summary>
[TestClass]
public sealed class InlineMemberServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-inlinemember-ws");

    private const string Main =
        "Option Explicit\r\n" +
        "Private Sub Shout(ByVal x As Long)\r\n" +
        "    Dim total As Long\r\n" +
        "    total = x * 2\r\n" +
        "    Debug.Print total\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub Run()\r\n" +
        "    Dim i As Long\r\n" +
        "    For i = 1 To 2\r\n" +
        "        Shout i\r\n" +
        "    Next\r\n" +
        "    Shout 5\r\n" +
        "    Greet\r\n" +
        "End Sub\r\n";

    private const string Helpers =
        "Option Explicit\r\n" +
        "Public Sub Greet()\r\n" +
        "    Debug.Print \"hi\"\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Private Sub Twice()\r\n" +
        "    Echo 1: Echo 2\r\n" +
        "    Echo 3\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Private Sub Echo(ByVal x As Long)\r\n" +
        "    Debug.Print x\r\n" +
        "End Sub\r\n";

    private const string Counters =
        "Option Explicit\r\n" +
        "Public Counter As Long\r\n" +
        "Private Tally As Long\r\n" +
        "\r\n" +
        "Public Sub Tick()\r\n" +
        "    Counter = Counter + 1\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Private Sub AddOne()\r\n" +
        "    Tally = Tally + 1\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub ShowTally()\r\n" +
        "    Dim Tally As Long\r\n" +
        "    AddOne\r\n" +
        "    Debug.Print Tally\r\n" +
        "End Sub\r\n";

    // a module whose own Counter shadows the Public one of Counters.
    private const string Shadow =
        "Option Explicit\r\n" +
        "Private Counter As Long\r\n" +
        "\r\n" +
        "Public Sub Start()\r\n" +
        "    Tick\r\n" +
        "    Debug.Print Counter\r\n" +
        "End Sub\r\n";

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly IReferencedLibraryService _libraries = Substitute.For<IReferencedLibraryService>();
    private readonly IOptions<SdkAppOptions> _options =
        Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
    private readonly InlineMemberService _service;

    public InlineMemberServiceTests()
        => _service = ServiceOf(
            new WorkspaceDocument("Main.bas", Root, Main, version: 4), new WorkspaceDocument("Helpers.bas", Root, Helpers, version: 2),
            new WorkspaceDocument("Counters.bas", Root, Counters, version: 1), new WorkspaceDocument("Shadow.bas", Root, Shadow, version: 1));

    // the service, on a workspace of these documents.
    private InlineMemberService ServiceOf(params WorkspaceDocument[] documents)
    {
        _documents.WorkspaceRoot.Returns(Root);
        _documents.GetAllDocuments().Returns(documents);
        foreach (var document in documents)
        {
            var uri = document.Id.Uri.ToUri();
            _documents.TryGetDocument(uri, out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = document; return true; });
            _parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, document.Text)));
        }

        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));

        _libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(
                call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var rename = new RenameService(
            _documents, _parsing, new IntrinsicSymbolResolver(), _libraries, _options, NullLogger<RenameService>.Instance, OneProject.Registry());
        return new InlineMemberService(rename, _parsing, _options);
    }

    // the service as the language server's container makes it: OmniSharp's, which looks at every constructor, the internal ones too, and fills primitive
    // parameters itself. The workspace, the parser and the libraries are the ones above; everything else is as the language server registers it.
    [TestMethod]
    public async Task TheServiceOfTheLanguageServersContainer_PreparesACall()
    {
        var toServer = new System.IO.Pipelines.Pipe();
        var toClient = new System.IO.Pipelines.Pipe();
        using var server = OmniSharp.Extensions.LanguageServer.Server.LanguageServer.Create(options => options
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .WithServices(services => services
                .AddSingleton<System.IO.Abstractions.IFileSystem>(new System.IO.Abstractions.FileSystem())
                .AddPlatformCoordination(new Version(1, 0))
                .AddSingleton(_options)
                .AddSingleton(_documents)
                .AddSingleton(_parsing)
                .AddSingleton(_libraries)
                .AddSingleton(OneProject.Registry())
                .AddSingleton<RDCore.SDK.Runtime.Abstract.Execution.ISymbolResolver>(new IntrinsicSymbolResolver())));

        var service = server.GetRequiredService<IInlineMemberService>();
        var prepared = await service.PrepareAsync(Document("Main.bas"), new SourcePosition(12, 5), CancellationToken.None);

        Assert.IsInstanceOfType<InlineMemberService>(service);
        Assert.IsTrue(prepared.Choices.Any(choice => choice.Id == InlineMemberService.SitesChoice),
            string.Join(" | ", prepared.Notices.Select(notice => $"{notice.Code}: {notice.Message}")));
    }

    private static Uri Document(string fileName) => DocumentUri.From(Path.Combine(Root, fileName)).ToUri();

    // the text of a document with the answer's edit of it made.
    private static string Edited(InlineMemberAnswer answer, string fileName, string text)
    {
        Assert.IsNull(answer.Refusal, answer.Refusal);
        var edit = answer.Edit!.Documents.Single(document => document.Uri == Document(fileName));
        return DeclarationEdits.ApplyTo(text, edit.Edits);
    }

    [TestMethod]
    public async Task ACall_IsInlined_IndentedWhereItIs_AtTheVersionOfItsDocument()
    {
        var answer = await _service.ApplyAsync(Document("Main.bas"), new SourcePosition(12, 5), [], [], CancellationToken.None);

        var text = Edited(answer, "Main.bas", Main);
        Assert.AreEqual(4, answer.Edit!.Documents.Single().Version);
        StringAssert.Contains(
            text, "    Next\r\n    Dim x As Long\r\n    x = 5\r\n    Dim total As Long\r\n    total = x * 2\r\n    Debug.Print total\r\n    Greet\r\n");
        StringAssert.Contains(text, "Private Sub Shout(ByVal x As Long)", "a call inlined alone leaves the member");
    }

    [TestMethod]
    public async Task FromItsDeclaration_EveryCallIsInlined_WithFreshNamesAcrossThem_AndAPrivateMemberIsRemoved()
    {
        var answer = await _service.ApplyAsync(Document("Main.bas"), new SourcePosition(1, 13), [], [], CancellationToken.None);

        var text = Edited(answer, "Main.bas", Main);
        Assert.DoesNotContain("Sub Shout", text);
        // the call after the loop is inlined first: the one in the loop then finds its names taken.
        StringAssert.Contains(
            text,
            "        Dim x2 As Long\r\n        x2 = i\r\n        Dim total2 As Long\r\n        total2 = x2 * 2\r\n        Debug.Print total2\r\n    Next\r\n");
        StringAssert.Contains(text, "    Dim x As Long\r\n    x = 5\r\n    Dim total As Long\r\n    total = x * 2\r\n");
    }

    [TestMethod]
    public async Task ASiteTheUserUnticks_IsNotInlined_AndKeepsTheMember()
    {
        var prepared = await _service.PrepareAsync(Document("Main.bas"), new SourcePosition(1, 13), CancellationToken.None);
        var loop = prepared.Choices.Single(choice => choice.Id == InlineMemberService.SitesChoice).Items
            .Single(item => item.Location!.Value.Range.Start.Line == 10);

        var untick = new RefactorItemAnswer { Id = loop.Id, Answers = [new RefactorAnswer { Id = InlineMemberService.InlineChoice, Value = "false" }] };
        var answer = await _service.ApplyAsync(Document("Main.bas"), new SourcePosition(1, 13),
            [new RefactorAnswer { Id = InlineMemberService.SitesChoice, Items = [untick] }], [], CancellationToken.None);

        var text = Edited(answer, "Main.bas", Main);
        StringAssert.Contains(text, "        Shout i\r\n");
        StringAssert.Contains(text, "Private Sub Shout(ByVal x As Long)");
        StringAssert.Contains(text, "    x = 5\r\n");
    }

    [TestMethod]
    public async Task APublicMember_IsOfferedItsRemoval_Unticked()
    {
        var prepared = await _service.PrepareAsync(Document("Helpers.bas"), new SourcePosition(1, 12), CancellationToken.None);

        var site = prepared.Choices.Single(choice => choice.Id == InlineMemberService.SitesChoice).Items.Single();
        Assert.AreEqual(Document("Main.bas"), site.Location!.Value.Uri);
        var removal = prepared.Choices.SingleOrDefault(choice => choice.Id == InlineMemberService.RemoveChoice);
        Assert.IsNotNull(removal, string.Join(" | ", prepared.Notices.Select(notice => $"{notice.Code}: {notice.Message}")));
        Assert.AreEqual("false", removal.Default);
    }

    [TestMethod]
    public async Task ARefusedSite_IsARefusalOfItsItem_AndKeepsTheMember()
    {
        var prepared = await _service.PrepareAsync(Document("Helpers.bas"), new SourcePosition(10, 13), CancellationToken.None);

        var items = prepared.Choices.Single(choice => choice.Id == InlineMemberService.SitesChoice).Items;
        var refusals = prepared.Notices.Where(notice => notice.Kind is RefactorNoticeKind.Refusal).ToList();
        Assert.HasCount(2, refusals);
        Assert.IsTrue(refusals.All(notice
            => notice.Choice == InlineMemberService.SitesChoice && items.Single(item => item.Id == notice.Item).Choices.Count == 0));
        Assert.AreEqual(1, items.Count(item => item.Choices.Any(choice => choice.Id == InlineMemberService.InlineChoice)));
        Assert.IsFalse(prepared.Choices.Any(choice => choice.Id == InlineMemberService.RemoveChoice));
        Assert.AreEqual("inlineMember.removalReferenceLeft", prepared.Notices.Single(notice => notice.Kind is RefactorNoticeKind.Note).Code);
    }

    [TestMethod]
    public async Task ANameOfTheBody_ThatTheCallersModuleShadows_IsQualified()
    {
        var answer = await _service.ApplyAsync(Document("Shadow.bas"), new SourcePosition(4, 5), [], [], CancellationToken.None);

        var text = Edited(answer, "Shadow.bas", Shadow);
        StringAssert.Contains(text, "    Counters.Counter = Counters.Counter + 1\r\n    Debug.Print Counter\r\n");
    }

    [TestMethod]
    public async Task ANameOfTheBody_ThatALocalAtTheCallShadows_IsRefused()
    {
        var prepared = await _service.PrepareAsync(Document("Counters.bas"), new SourcePosition(14, 5), CancellationToken.None);
        var answer = await _service.ApplyAsync(Document("Counters.bas"), new SourcePosition(14, 5), [], [], CancellationToken.None);

        var refusal = prepared.Notices.Single(notice => notice.Kind is RefactorNoticeKind.Refusal);
        StringAssert.Contains(refusal.Message, "Tally");
        Assert.IsNull(answer.Edit);
        Assert.IsNotNull(answer.Refusal);
    }

    // a module of parts (RD-VBAL §5.1): Program's plain part declares Add, and its Partial part, a block of Extra.bas, calls it.
    private const string PlainPart = "Option Explicit\r\nPrivate count As Long\r\n\r\nPrivate Sub Add(ByVal n As Long)\r\n    count = count + n\r\nEnd Sub\r\n";

    private const string PartialPart = "Partial Module Program\r\nPublic Sub Main()\r\n    Add 20\r\n    Debug.Print count\r\nEnd Sub\r\nEnd Module\r\n";

    [TestMethod]
    public async Task ACallInThePartialPartOfAModule_IsInlinedWhereItIsWritten()
    {
        var service = ServiceOf(
            new WorkspaceDocument("Program.bas", Root, PlainPart, version: 1), new WorkspaceDocument("Extra.bas", Root, PartialPart, version: 3));

        var answer = await service.ApplyAsync(Document("Extra.bas"), new SourcePosition(2, 5), [], [], CancellationToken.None);

        var text = Edited(answer, "Extra.bas", PartialPart);
        Assert.AreEqual(3, answer.Edit!.Documents.Single().Version);
        Assert.DoesNotContain("Add 20", text);
        StringAssert.Contains(text, "    count = count + n\r\n    Debug.Print count\r\n");
    }
}
