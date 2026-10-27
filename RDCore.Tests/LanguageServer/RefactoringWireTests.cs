using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Progress;
using RDCore.SDK.Server.Serialization;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What a client of the platform asks to rename a name and fix a diagnostic, over the wire, as the shell asks it: the diagnostics of a document, its code actions,
/// and the rename - each answer read by the client as it is sent.
/// </summary>
[TestClass]
public sealed class RefactoringWireTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-refactoring-wire-ws");
    private const string Program = "10 Dim strName As String\r\n20 strName = \"a\"\r\n";

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AClient_ReadsTheDiagnostics_TheCodeActions_AndTheRename_ItIsAnswered()
    {
        var documents = Substitute.For<IWorkspaceDocumentService>();
        var parsing = Substitute.For<IParsingClientService>();
        documents.WorkspaceRoot.Returns(Root);
        var program = new WorkspaceDocument("Main.rdc", Root, Program, version: 4);
        var uri = program.Id.Uri.ToUri();
        documents.GetAllDocuments().Returns([program]);
        parsing.ParseDocumentAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new ModuleParser().Parse(uri, ParsingClientService.ModuleSourceOf(program))));

        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var rename = new RenameService(documents, parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance);

        var finding = new Diagnostic
        {
            Range = new Range(0, 7, 0, 14),
            Code = "RDC01002",
            Source = "RDCore",
            Message = "Hungarian",
            Data = new DiagnosticFixes { Rename = new RenameFix { Suggestions = ["Name"] } }.ToData(),
        };
        var diagnostics = Substitute.For<IDocumentDiagnosticsService>();
        diagnostics.GetAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<WorkProgressStep>?>())
            .Returns(DocumentDiagnosticsResult.Fresh(4, [finding]));

        // a request that is not answered fails the test, rather than keeping it waiting.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            // as the platform's servers write a pull diagnostic report (RDCoreServerApp).
            .WithSerializer(new PullDiagnosticsLspSerializer())
            .AddHandler(new DocumentDiagnosticHandler(diagnostics, new WorkProgressReporter()))
            .AddHandler(new CodeActionHandler(rename, documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem()))
            .AddHandler(new RenameHandler(rename))
            .AddHandler(new PrepareRenameHandler(rename)), timeout.Token);
        var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer), timeout.Token);

        await Task.WhenAll(serving, connecting);
        using var server = await serving;
        using var languageClient = await connecting;
        var document = new TextDocumentIdentifier(DocumentUri.From(uri));

        var pulled = (await languageClient.SendRequest<DocumentDiagnosticPullReport?>(new DocumentDiagnosticPullParams { TextDocument = document }, timeout.Token))!.Items;
        Assert.HasCount(1, pulled);
        CollectionAssert.AreEqual(new[] { "Name" }, DiagnosticFixes.Of(pulled[0])?.Rename?.Suggestions.ToArray(), "the fix travels in the data");

        var actions = await languageClient.SendRequest(new CodeActionParams
        {
            TextDocument = document,
            Range = pulled[0].Range,
            Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>(pulled) },
        }, timeout.Token);
        var titles = actions!.Select(action => action.CodeAction!.Title).ToArray();
        CollectionAssert.AreEqual(new[] { RefactoringTitles.RenameTo("strName", "Name"), RefactoringTitles.Rename("strName") }, titles);

        var prepared = await languageClient.SendRequest(new PrepareRenameParams { TextDocument = document, Position = new Position(1, 5) }, timeout.Token);
        Assert.AreEqual("strName", prepared?.PlaceholderRange?.Placeholder);

        var renamed = await languageClient.SendRequest(new RenameParams { TextDocument = document, Position = new Position(1, 5), NewName = "Title" }, timeout.Token);
        var edit = renamed!.DocumentChanges!.Single().TextDocumentEdit!;
        Assert.AreEqual(4, edit.TextDocument.Version);
        Assert.HasCount(2, edit.Edits);

        var refused = await Assert.ThrowsAsync<OmniSharp.Extensions.JsonRpc.Server.JsonRpcException>(
            () => languageClient.SendRequest(new RenameParams { TextDocument = document, Position = new Position(1, 5), NewName = "Next" }, timeout.Token));
        StringAssert.Contains(refused.Message, "Next", "the message of the refusal is the client's to show");
        Assert.AreEqual(-32803, refused.ErrorCode, "RequestFailed: an answer, which a client does not log as a fault");
    }
}
