using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Server;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.SDK.Server;
using RDCore.SDK.Server.Progress;
using RDCore.SDK.Server.Serialization;
using System.IO.Pipelines;
using System.Text;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <c>workspace/diagnostic</c> over the wire: a report for every module, open or not; unchanged for a module the client has the result of; each streamed
/// to a client that gives a partial result token; and the pull given up for a change to any document.
/// </summary>
[TestClass]
public sealed class WorkspaceDiagnosticWireTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-workspace-diagnostic-ws");

    // the module the client has open, at its version 3, and one it has not, as the server loaded it.
    private readonly WorkspaceDocument _open = new("Open.bas", Root, "Attribute VB_Name = \"Open\"\r\n", version: 3);
    private readonly WorkspaceDocument _closed = new("Closed.bas", Root, "Attribute VB_Name = \"Closed\"\r\n", version: 1);
    private readonly IWorkspaceDocumentService _documents = Substitute.For<IWorkspaceDocumentService>();
    private readonly DocumentStateProvider _states = new(NullLogger<DocumentStateProvider>.Instance);
    private readonly IDocumentDiagnosticsService _diagnostics = Substitute.For<IDocumentDiagnosticsService>();
    private readonly IncomingDocumentChanges _incoming = new();

    public WorkspaceDiagnosticWireTests()
    {
        _documents.WorkspaceRoot.Returns(Root);
        _documents.GetAllDocuments().Returns([_open, _closed]);
        _states.Initialize([_open.Id, _closed.Id]);
        _states.OnDocumentLoaded(_closed.Id);
        _states.OnDocumentOpenedByClient(_open.Id);

        // a module's own pull: unchanged for the result the client has, a finding of its own otherwise.
        _diagnostics.GetAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<WorkProgressStep>?>()).Returns(call =>
        {
            var document = call.Arg<Uri>().AbsolutePath.EndsWith("Open.bas", StringComparison.OrdinalIgnoreCase) ? _open : _closed;
            return Task.FromResult(call.Arg<string?>() == DocumentDiagnosticsResult.ResultIdOf(document.Version, generation: 0)
                ? DocumentDiagnosticsResult.NotChanged(document.Version)
                : DocumentDiagnosticsResult.Fresh(document.Version, [new Diagnostic { Range = new Range(0, 0, 0, 9), Message = $"in {document.Name}" }]));
        });
    }

    // a server of the pull, and its client: each message the server writes is kept as it is written, to read what the client's own types would hide.
    private async Task<(OmniSharpLanguageServer Server, LanguageClient Client, List<JObject> Wire)> ConnectAsync(CancellationToken token)
    {
        var toServer = new Pipe();
        var fromServer = new Pipe();
        var toClient = new Pipe();
        var wire = new List<JObject>();
        var pending = new StringBuilder();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var read = await fromServer.Reader.ReadAsync();
                foreach (var segment in read.Buffer)
                {
                    lock (wire)
                    {
                        pending.Append(Encoding.UTF8.GetString(segment.Span));
                        Frames(pending, wire);
                    }

                    await toClient.Writer.WriteAsync(segment);
                }

                fromServer.Reader.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted)
                {
                    break;
                }
            }
        });

        var serving = OmniSharpLanguageServer.From(server => server
            .WithInput(toServer.Reader)
            .WithOutput(fromServer.Writer)
            // as the platform's servers write a pull diagnostic report (RDCoreServerApp).
            .WithSerializer(new PullDiagnosticsLspSerializer())
            .WithServices(services => services
                .AddSingleton(_documents)
                .AddSingleton<IDocumentStateProvider>(_states)
                .AddSingleton(_diagnostics)
                .AddSingleton<IWorkProgressReporter>(new WorkProgressReporter())
                .AddSingleton(_incoming))
            .WithHandler<WorkspaceDiagnosticHandler>(), token);
        var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer), token);
        await Task.WhenAll(serving, connecting);
        return (await serving, await connecting, wire);
    }

    // the messages a buffer holds whole: a Content-Length header, then that many bytes of JSON.
    private static void Frames(StringBuilder pending, List<JObject> wire)
    {
        while (pending.ToString() is var text && text.IndexOf("\r\n\r\n", StringComparison.Ordinal) is var end and >= 0)
        {
            var length = int.Parse(text[..end].Split(':')[1].Trim());
            var body = Encoding.UTF8.GetBytes(text[(end + 4)..]);
            if (body.Length < length)
            {
                return;
            }

            var json = Encoding.UTF8.GetString(body, 0, length);
            wire.Add(JObject.Parse(json));
            pending.Remove(0, end + 4 + json.Length);
        }
    }

    private static Task<JToken> PullAsync(LanguageClient client, WorkspaceDiagnosticParams request, CancellationToken token)
        => client.SendRequest("workspace/diagnostic", request).Returning<JToken>(token);

    private static string[] Reported(JToken items)
        => [.. items.Select(item => $"{Path.GetFileName(DocumentUri.From(item.Value<string>("uri")!).GetFileSystemPath())} {item.Value<string>("kind")} {item.Value<string>("resultId")} v={(item["version"] is JValue { Type: JTokenType.Null } ? "null" : item.Value<int>("version").ToString())} {item["items"]?.Count() ?? 0}")];

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task EveryModule_IsReported_OpenOrNot_AndUnchanged_WhenTheClientHasItsResult()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (server, client, _) = await ConnectAsync(timeout.Token);
        using var _server = server;
        using var _client = client;
        Assert.IsTrue(client.ServerSettings.Capabilities.DiagnosticProvider?.WorkspaceDiagnostics, "the server says it serves workspace/diagnostic");

        var first = await PullAsync(client, new WorkspaceDiagnosticParams { PreviousResultIds = new Container<PreviousResultId>() }, timeout.Token);
        CollectionAssert.AreEqual(new[] { "Closed.bas full v1.0 v=null 1", "Open.bas full v3.0 v=3 1" }, Reported(first["items"]!),
            "the open module at the client's version, the closed one at none");

        var second = await PullAsync(client, new WorkspaceDiagnosticParams
        {
            PreviousResultIds = new Container<PreviousResultId>(
                new PreviousResultId { Uri = _open.Id.Uri, Value = "v3.0" },
                new PreviousResultId { Uri = _closed.Id.Uri, Value = "v1.0" }),
        }, timeout.Token);
        CollectionAssert.AreEqual(new[] { "Closed.bas unchanged v1.0 v=null 0", "Open.bas unchanged v3.0 v=3 0" }, Reported(second["items"]!));
    }

    // a module the project no longer lists: what the client shows of it is cleared, and there is no result id for it to send again.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AModuleTheWorkspaceNoLongerHas_IsReportedWithNoFindings()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (server, client, _) = await ConnectAsync(timeout.Token);
        using var _server = server;
        using var _client = client;
        var gone = DocumentUri.FromFileSystemPath(Path.Combine(Root, "Gone.bas"));

        var answer = await PullAsync(client, new WorkspaceDiagnosticParams
        {
            PreviousResultIds = new Container<PreviousResultId>(new PreviousResultId { Uri = gone, Value = "v2.0" }),
        }, timeout.Token);

        CollectionAssert.Contains(Reported(answer["items"]!), "Gone.bas full  v=null 0");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AClientThatGivesAPartialResultToken_IsSentEachModulesReportAsItIsMade_AndAnEmptyAnswer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (server, client, wire) = await ConnectAsync(timeout.Token);
        using var _server = server;
        using var _client = client;

        var answer = await PullAsync(client, new WorkspaceDiagnosticParams { PreviousResultIds = new Container<PreviousResultId>(), PartialResultToken = "partial" }, timeout.Token);

        Assert.IsEmpty(answer["items"]!, "the partial results are the answer");
        List<JObject> progress;
        lock (wire)
        {
            progress = [.. wire.Where(message => message.Value<string>("method") == "$/progress" && message["params"]!.Value<string>("token") == "partial")];
        }

        CollectionAssert.AreEqual(new[] { "Closed.bas full v1.0 v=null 1", "Open.bas full v3.0 v=3 1" },
            progress.SelectMany(message => Reported(message["params"]!["value"]!["items"]!)).ToArray(), "one module at a time");
    }

    // a change to any document gives the pull up at once: the change, and the client's next request, do not wait for the whole workspace.
    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task AChangeToAnyDocument_GivesThePullUp_AnsweredContentModified()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var started = new TaskCompletionSource();
        _diagnostics.GetAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<WorkProgressStep>?>()).Returns(async call =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
            return DocumentDiagnosticsResult.Fresh(0, []);
        });
        var (server, client, _) = await ConnectAsync(timeout.Token);
        using var _server = server;
        using var _client = client;

        var pulling = PullAsync(client, new WorkspaceDiagnosticParams { PreviousResultIds = new Container<PreviousResultId>() }, timeout.Token);
        await started.Task.WaitAsync(timeout.Token);
        _incoming.Arrived(new Uri(Path.Combine(Root, "Elsewhere.bas")));

        var modified = await Assert.ThrowsAsync<OmniSharp.Extensions.JsonRpc.Server.ContentModifiedException>(() => pulling);
        Assert.IsNotNull(modified);
    }
}
