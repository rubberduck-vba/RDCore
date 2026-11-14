using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Serialization;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.Parsing;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.Services;
using RDCore.SDK.Workspace;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using System.IO.Pipelines;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The extraction a client with no dialog is offered, over the wire: one that resolves an action's edit is offered it without the edit, which is made when it
/// resolves the action, and refused when the document has changed since; any other is sent the edit with the action. The code actions are registered as
/// resolved.
/// </summary>
[TestClass]
public sealed class ExtractMethodResolveWireTests
{
    // a cap on a hang, not a budget: each wait is on the answer itself, which a loaded machine gives as late as it does.
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(5);

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-extractmethod-resolve-ws");

    // line 5 computes b from a, which line 6 prints.
    private const string Module =
        "Option Explicit\r\n" +
        "Public Sub Run(ByVal n As Long)\r\n" +
        "    Dim a As Long\r\n" +
        "    Dim b As Long\r\n" +
        "    a = n\r\n" +
        "    b = a + 1\r\n" +
        "    Debug.Print b\r\n" +
        "End Sub\r\n";

    private static readonly Range Selection = new(5, 0, 6, 0);

    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private WorkspaceDocument _document = new("Main.bas", Root, Module, version: 3);

    public ExtractMethodResolveWireTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        _documents.GetAllDocuments().Returns(_ => [_document]);
        _documents.TryGetDocument(Arg.Any<Uri>(), out Arg.Any<WorkspaceDocument>()).Returns(call => { call[1] = _document; return true; });
        _parsing.ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), Module)));
        _parsing.ParseFragmentAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse(call.Arg<Uri>(), call.Arg<string>())));
    }

    private static TextDocumentIdentifier Document => new(DocumentUri.From(Path.Combine(Root, "Main.bas")));

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AClientThatResolvesEdits_IsOfferedTheExtractionWithoutItsEdit_AndResolvingItMakesTheEdit()
    {
        using var timeout = new CancellationTokenSource(SafetyCap);
        var (server, client) = await ConnectAsync(resolvesEdits: true, timeout.Token);
        using var _ = server;
        using var __ = client;

        var offered = await ExtractionAsync(client, timeout.Token);
        Assert.AreEqual(CodeActionHandler.RefactorExtractMethod, offered.Kind);
        Assert.IsNull(offered.Edit, "the edit is made when the action is resolved");
        Assert.IsNull(offered.Command);

        var resolved = await client.SendRequest(offered, timeout.Token);

        Assert.AreEqual(offered.Title, resolved.Title);
        Assert.AreEqual(CodeActionHandler.RefactorExtractMethod, resolved.Kind);
        AssertExtracts(resolved.Edit);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AClientThatDoesNotResolveEdits_IsSentTheEditWithTheExtraction()
    {
        using var timeout = new CancellationTokenSource(SafetyCap);
        var (server, client) = await ConnectAsync(resolvesEdits: false, timeout.Token);
        using var _ = server;
        using var __ = client;

        AssertExtracts((await ExtractionAsync(client, timeout.Token)).Edit);
    }

    // the range of an offered extraction is of the text the document had then: a change after it makes the range another selection.
    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AnExtractionResolvedAfterItsDocumentChanged_IsRefused_WithWhy()
    {
        using var timeout = new CancellationTokenSource(SafetyCap);
        var (server, client) = await ConnectAsync(resolvesEdits: true, timeout.Token);
        using var _ = server;
        using var __ = client;
        var offered = await ExtractionAsync(client, timeout.Token);

        _document = new WorkspaceDocument("Main.bas", Root, Module, version: 4);
        var refused = await Assert.ThrowsAsync<OmniSharp.Extensions.JsonRpc.Server.JsonRpcException>(() => client.SendRequest(offered, timeout.Token));

        Assert.AreEqual(-32803, refused.ErrorCode, "RequestFailed: an answer, which a client does not log as a fault");
        Assert.AreEqual(ExtractMethodService.DocumentChanged(), refused.Message);
    }

    private static void AssertExtracts(WorkspaceEdit? edit)
    {
        Assert.IsNotNull(edit);
        var edits = edit.DocumentChanges?.Single().TextDocumentEdit!.Edits ?? edit.Changes![Document.Uri];
        Assert.IsTrue(edits.Any(each => each.NewText.Contains("Private Sub ", StringComparison.Ordinal)), string.Join(" | ", edits.Select(each => each.NewText)));
    }

    private static async Task<CodeAction> ExtractionAsync(LanguageClient client, CancellationToken token)
    {
        var actions = await client.SendRequest(new CodeActionParams
        {
            TextDocument = Document,
            Range = Selection,
            Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>(), Only = new Container<CodeActionKind>(CodeActionHandler.RefactorExtractMethod) },
        }, token);
        return actions!.Select(action => action.CodeAction!).Single();
    }

    // the options the server registers code actions with, which it is asked for both when it answers the capability as it initializes and when it registers
    // it afterwards. They are read from the handler: the test client registers afterwards, and the connection does not wait for that.
    [TestMethod]
    public void TheCodeActions_AreRegisteredAsResolved()
    {
        var registration = (IRegistration<CodeActionRegistrationOptions, CodeActionCapability>)Handler();

        Assert.IsTrue(registration.GetRegistrationOptions(new CodeActionCapability(), new ClientCapabilities()).ResolveProvider);
    }

    private CodeActionHandler Handler()
    {
        var libraries = Substitute.For<IReferencedLibraryService>();
        libraries.SymbolsUnder(Arg.Any<Uri>()).Returns(call
            => [.. new LibrarySymbolProvider(call.Arg<Uri>(), ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()]);
        var options = Options.Create(new SdkAppOptions { Workspace = new SdkWorkspaceOptions { WorkspaceUri = Root, Language = "vba" } });
        var rename = new RenameService(_documents, _parsing, new IntrinsicSymbolResolver(), libraries, options, NullLogger<RenameService>.Instance, OneProject.Registry());
        return new CodeActionHandler(rename, _documents, new System.IO.Abstractions.TestingHelpers.MockFileSystem(), new ExtractMethodService(rename, _parsing, options),
            Substitute.For<IPlatformClientCapabilitiesService>(), new LspSerializer());
    }

    private async Task<(OmniSharpLanguageServer Server, LanguageClient Client)> ConnectAsync(bool resolvesEdits, CancellationToken token)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(toClient.Writer)
            .AddHandler(Handler()), token);
        var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer)
            .WithClientCapabilities(new ClientCapabilities
            {
                TextDocument = new TextDocumentClientCapabilities
                {
                    CodeAction = new CodeActionCapability
                    {
                        CodeActionLiteralSupport = new CodeActionLiteralSupportOptions
                        {
                            CodeActionKind = new CodeActionKindCapabilityOptions { ValueSet = new Container<CodeActionKind>(CodeActionHandler.RefactorExtractMethod) },
                        },
                        DataSupport = resolvesEdits,
                        ResolveSupport = resolvesEdits ? new CodeActionCapabilityResolveSupportOptions { Properties = new Container<string>("edit") } : null,
                    },
                },
            }), token);

        await Task.WhenAll(serving, connecting);
        return (await serving, await connecting);
    }
}
