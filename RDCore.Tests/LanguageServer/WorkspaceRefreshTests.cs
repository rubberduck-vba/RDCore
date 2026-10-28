using System.IO.Abstractions.TestingHelpers;
using System.IO.Pipelines;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.Parsing;
using RDCore.SDK.Model.Diagnostics;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What a client shows of the documents it did not change is refreshed once another module changed what it declares (#257): each refresh the client says it is
/// asked for (<c>refreshSupport</c>), once for a burst of changes, as a request; and none for a change that no other module can tell.
/// </summary>
[TestClass]
public sealed class WorkspaceRefreshTests
{
    private static readonly string[] Methods = [.. WorkspaceRefresh.Requests.Select(request => request.Method)];

    // a client that says it is asked for every refresh.
    private static WorkspaceClientCapabilities Refreshing() => new()
    {
        Diagnostics = new DiagnosticWorkspaceClientCapabilities { RefreshSupport = true },
        SemanticTokens = new SemanticTokensWorkspaceCapability { RefreshSupport = true },
        InlayHint = new InlayHintWorkspaceClientCapabilities { RefreshSupport = true },
        CodeLens = new CodeLensWorkspaceClientCapabilities { RefreshSupport = true },
    };

    private static ILanguageServerFacade ServerFor(WorkspaceClientCapabilities? workspace)
    {
        var server = Substitute.For<ILanguageServerFacade>();
        server.ClientSettings.Returns(new InitializeParams { Capabilities = new ClientCapabilities { Workspace = workspace } });
        return server;
    }

    [TestMethod]
    public void AClientThatSaysItRefreshes_IsAskedEachRefresh()
        => CollectionAssert.AreEquivalent(new[]
            {
                "workspace/diagnostic/refresh", "workspace/semanticTokens/refresh", "workspace/inlayHint/refresh", "workspace/codeLens/refresh",
            },
            WorkspaceRefresh.SupportedBy(ServerFor(Refreshing())).ToArray());

    [TestMethod]
    public void AClientThatSaysItRefreshesSome_IsAskedThose()
        => CollectionAssert.AreEqual(new[] { "workspace/codeLens/refresh" }, WorkspaceRefresh.SupportedBy(ServerFor(new WorkspaceClientCapabilities
        {
            CodeLens = new CodeLensWorkspaceClientCapabilities { RefreshSupport = true },
            InlayHint = new InlayHintWorkspaceClientCapabilities { RefreshSupport = false },
        })).ToArray());

    [TestMethod]
    public void AClientThatDoesNot_IsNot()
    {
        Assert.IsEmpty(WorkspaceRefresh.SupportedBy(ServerFor(null)));
        Assert.IsEmpty(WorkspaceRefresh.SupportedBy(ServerFor(new WorkspaceClientCapabilities())));
    }

    // what the server registers for a client that sent none of a capability is a capability with no value.
    [TestMethod]
    public void ACapabilityWithNoValue_IsNoSupport_AndThrowsNothing()
        => Assert.IsEmpty(WorkspaceRefresh.SupportedBy(ServerFor(new WorkspaceClientCapabilities
        {
            Diagnostics = new(true), SemanticTokens = new(true), InlayHint = new(true), CodeLens = new(true),
        })));

    [TestMethod]
    public void AChange_ChangesTheGeneration_AtOnce()
    {
        using var refresh = new WorkspaceRefresh(TimeSpan.FromHours(1));
        var before = refresh.Generation;

        refresh.Changed();

        Assert.AreNotEqual(before, refresh.Generation, "a pull that comes before the request is not answered unchanged");
    }

    [TestMethod]
    public async Task ABurstOfChanges_AsksForEachRefreshOnce()
    {
        var server = ServerFor(Refreshing());
        using var refresh = new WorkspaceRefresh(TimeSpan.FromMilliseconds(100));
        refresh.Attach(server);

        for (var change = 0; change < 5; change++)
        {
            refresh.Changed();
        }

        for (var attempt = 0; attempt < 50 && server.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "SendRequest") < Methods.Length; attempt++)
        {
            await Task.Delay(50);
        }

        await Task.Delay(300);
        foreach (var method in Methods)
        {
            server.Received(1).SendRequest(method);
        }
    }

    // ---- over an actual connection: OmniSharp 0.19.9's own Send*Refresh write a notification, with no id ----

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-refresh-ws");
    private static readonly Uri ModuleA = new(Path.Combine(Root, "A.bas"));

    private const string A = "Public Function Twice(ByVal Value As Long) As Long\r\n    Twice = Value * 2\r\nEnd Function\r\n";

    // the server, its client, what the server wrote to the client, and a lifecycle of documents that asks the client to refresh.
    private sealed class Connection : IAsyncDisposable
    {
        private readonly StringBuilder _wire = new();
        private readonly MockFileSystem _files = new();
        private OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServer? _server;
        private LanguageClient? _client;
        private WorkspaceRefresh? _refresh;

        public DocumentLifecycleService Documents { get; private set; } = null!;

        public static async Task<Connection> OpenAsync(CancellationToken token)
        {
            var connection = new Connection();
            await connection.StartAsync(token);
            return connection;
        }

        private async Task StartAsync(CancellationToken token)
        {
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            var toClient = new Pipe();
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    var read = await serverToClient.Reader.ReadAsync();
                    foreach (var segment in read.Buffer)
                    {
                        lock (_wire)
                        {
                            _wire.Append(Encoding.UTF8.GetString(segment.Span));
                        }

                        await toClient.Writer.WriteAsync(segment);
                    }

                    serverToClient.Reader.AdvanceTo(read.Buffer.End);
                    if (read.IsCompleted)
                    {
                        await toClient.Writer.CompleteAsync();
                        return;
                    }
                }
            });

            var serverTask = OmniSharpLanguageServer.From(options => options.WithInput(clientToServer.Reader).WithOutput(serverToClient.Writer), token);
            var clientTask = LanguageClient.From(options => options
                .WithInput(toClient.Reader)
                .WithOutput(clientToServer.Writer)
                .WithClientCapabilities(new ClientCapabilities { Workspace = Refreshing() }), token);
            _server = await serverTask;
            _client = await clientTask;

            _refresh = new WorkspaceRefresh(TimeSpan.FromMilliseconds(100));
            _refresh.Attach(_server);

            var documents = new WorkspaceDocumentService(
                new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance), NullLogger<WorkspaceDocumentService>.Instance, _files.Path, _files.File);
            documents.Initialize(Root);
            var parsing = Substitute.For<IParsingClientService>();
            parsing.ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(new ModuleParser().Parse((Uri)call[0], documents.TryGetDocument((Uri)call[0], out var document) ? document.Text : string.Empty)));
            var diagnostics = Substitute.For<IDocumentDiagnosticsService>();
            diagnostics.GetPhaseAsync(Arg.Any<Uri>(), Arg.Any<AnalysisPhase>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(DocumentDiagnosticsResult.Fresh(1, [])));
            Documents = new DocumentLifecycleService(documents, parsing, Substitute.For<ISymbolSyncService>(), diagnostics, Substitute.For<IDiagnosticsPublisher>(),
                _refresh, NullLogger<DocumentLifecycleService>.Instance);
        }

        public string Wire
        {
            get
            {
                lock (_wire)
                {
                    return _wire.ToString();
                }
            }
        }

        // each message the server wrote for a method.
        public IReadOnlyList<string> MessagesOf(string method)
            => [.. Regex.Split(Wire, "Content-Length: \\d+\\r\\n\\r\\n").Where(message => message.Contains($"\"method\":\"{method}\"", StringComparison.Ordinal))];

        public async Task WaitForAsync(Func<bool> condition)
        {
            for (var attempt = 0; attempt < 50 && !condition(); attempt++)
            {
                await Task.Delay(100);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _refresh?.Dispose();
            _client?.Dispose();
            _server?.Dispose();
            await Task.CompletedTask;
        }
    }

    [TestMethod]
    public async Task AChangeOfWhatAModuleDeclares_AsksForEachRefresh_AsARequest_OnTheWire()
    {
        using var connecting = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var connection = await Connection.OpenAsync(connecting.Token);
        connection.Documents.Opened(ModuleA, A, 1);
        await connection.Documents.WhenRefreshedAsync(ModuleA);

        // the procedure another module calls is gone.
        connection.Documents.Changed(ModuleA, 2, [new TextDocumentContentChangeEvent { Text = A.Replace("Twice", "Thrice") }]);
        await connection.Documents.WhenRefreshedAsync(ModuleA);
        await connection.WaitForAsync(() => Methods.All(method => connection.MessagesOf(method).Count == 1));

        foreach (var method in Methods)
        {
            var message = connection.MessagesOf(method).Single();
            StringAssert.Contains(message, "\"id\":", $"{method} is a request, which the client answers");
        }
    }

    [TestMethod]
    public async Task AChangeNoOtherModuleCanTell_AsksForNoRefresh()
    {
        using var connecting = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var connection = await Connection.OpenAsync(connecting.Token);
        var thrice = A.Replace("Twice", "Thrice");
        connection.Documents.Opened(ModuleA, A, 1);
        await connection.Documents.WhenRefreshedAsync(ModuleA);
        connection.Documents.Changed(ModuleA, 2, [new TextDocumentContentChangeEvent { Text = thrice }]);
        await connection.Documents.WhenRefreshedAsync(ModuleA);
        await connection.WaitForAsync(() => Methods.All(method => connection.MessagesOf(method).Count == 1));
        Assert.AreEqual(Methods.Length, Methods.Sum(method => connection.MessagesOf(method).Count), "a change the other modules can tell asks once for each");

        connection.Documents.Changed(ModuleA, 3, [new TextDocumentContentChangeEvent { Text = thrice.Replace("Value * 2", "Value * 3 ' thrice") }]);
        await connection.Documents.WhenRefreshedAsync(ModuleA);
        await Task.Delay(500);

        Assert.AreEqual(Methods.Length, Methods.Sum(method => connection.MessagesOf(method).Count), "a literal and a comment in a function's body are its own");
    }
}
