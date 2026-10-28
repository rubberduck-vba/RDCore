using System.IO.Pipelines;
using System.Text;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.JsonRpc.Server;
using OmniSharp.Extensions.LanguageServer.Client;
using RDCore.SDK.Server;
using RDCore.SDK.Workspace;
using OmniSharpLanguageServer = OmniSharp.Extensions.LanguageServer.Server.LanguageServer;

namespace RDCore.Tests.Server;

/// <summary>
/// A request about a document stops when a change to the document arrives, and is answered <c>ContentModified</c>, so that the change, and the client's next
/// request, don't wait for it (<see cref="IncomingDocumentChanges"/>): HexIDE's completion waited five seconds behind a diagnostics pull (findings desk #31).
/// </summary>
[TestClass]
public sealed class IncomingDocumentChangesTests
{
    private const string DocumentA = "file:///c:/ws/A.bas";
    private const string DocumentB = "file:///c:/ws/B.bas";

    public sealed record class Document(string Uri);

    [Method("test/aboutDocument", Direction.ClientToServer)]
    public sealed record class AboutDocumentParams(Document TextDocument) : IRequest<int>;

    [Method("textDocument/didChange", Direction.ClientToServer)]
    public sealed record class ChangeParams(Document TextDocument) : IRequest;

    // a server whose request about a document takes five seconds (heeding its token or not), and that applies a change to a document in order, as didChange is.
    private sealed class Wire : IAsyncDisposable
    {
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
        private OmniSharpLanguageServer? _server;

        public IncomingDocumentChanges Incoming { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Applied { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LanguageClient Client { get; private set; } = default!;

        public static async Task<Wire> StartAsync(bool answerHeedsItsToken)
        {
            var wire = new Wire();
            var toServer = new Pipe();
            var toClient = new Pipe();
            var serving = OmniSharpLanguageServer.From(server => server
                .WithInput(PipeReader.Create(wire.Incoming.Tap(toServer.Reader.AsStream())))
                .WithOutput(toClient.Writer)
                .WithEveryRequestAnswered()
                .OnRequest<AboutDocumentParams, int>("test/aboutDocument", (request, token) => wire.Incoming.WhileCurrentAsync(new Uri(request.TextDocument.Uri), token, async answering =>
                {
                    wire.Started.TrySetResult();
                    await Task.Delay(TimeSpan.FromSeconds(5), answerHeedsItsToken ? answering : CancellationToken.None);
                    return 42;
                }), new JsonRpcHandlerOptions { RequestProcessType = RequestProcessType.Parallel })
                .OnNotification<ChangeParams>("textDocument/didChange", change =>
                {
                    wire.Incoming.Applied(new Uri(change.TextDocument.Uri));
                    wire.Applied.TrySetResult();
                }, new JsonRpcHandlerOptions { RequestProcessType = RequestProcessType.Serial }),
                wire._timeout.Token);
            var connecting = LanguageClient.From(client => client.WithInput(toClient.Reader).WithOutput(toServer.Writer), wire._timeout.Token);
            await Task.WhenAll(serving, connecting).WaitAsync(TimeSpan.FromSeconds(20));
            wire._server = await serving;
            wire.Client = await connecting;
            return wire;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            _server?.Dispose();
            _timeout.Dispose();
            await Task.CompletedTask;
        }
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ARequestAboutADocument_WhenAChangeToItArrives_IsAnsweredContentModifiedAtOnce()
    {
        await using var wire = await Wire.StartAsync(answerHeedsItsToken: true);

        var answer = wire.Client.SendRequest(new AboutDocumentParams(new Document(DocumentA)), CancellationToken.None);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var changed = DateTime.UtcNow;
        wire.Client.SendNotification(new ChangeParams(new Document(DocumentA)));

        await Assert.ThrowsExactlyAsync<ContentModifiedException>(() => answer.WaitAsync(TimeSpan.FromSeconds(10)));
        await wire.Applied.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsLessThan(TimeSpan.FromSeconds(3), DateTime.UtcNow - changed, "the change didn't wait for the request's five seconds");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ARequestAboutADocument_WhenAChangeToAnotherArrives_IsAnsweredItsResult()
    {
        await using var wire = await Wire.StartAsync(answerHeedsItsToken: true);

        var answer = wire.Client.SendRequest(new AboutDocumentParams(new Document(DocumentA)), CancellationToken.None);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        wire.Client.SendNotification(new ChangeParams(new Document(DocumentB)));

        Assert.AreEqual(42, await answer.WaitAsync(TimeSpan.FromSeconds(15)));
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ARequestWhoseAnswerDoesNotHeedItsToken_StillEndsWhenAChangeArrives()
    {
        await using var wire = await Wire.StartAsync(answerHeedsItsToken: false);

        var answer = wire.Client.SendRequest(new AboutDocumentParams(new Document(DocumentA)), CancellationToken.None);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var changed = DateTime.UtcNow;
        wire.Client.SendNotification(new ChangeParams(new Document(DocumentA)));

        await Assert.ThrowsExactlyAsync<ContentModifiedException>(() => answer.WaitAsync(TimeSpan.FromSeconds(10)));
        await wire.Applied.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsLessThan(TimeSpan.FromSeconds(3), DateTime.UtcNow - changed, "the change didn't wait for the request's five seconds");
    }

    [TestMethod]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task ARequestTheClientCancels_EndsAtOnce_ThoughItsAnswerDoesNotHeedItsToken()
    {
        await using var wire = await Wire.StartAsync(answerHeedsItsToken: false);
        using var cancel = new CancellationTokenSource();

        var answer = wire.Client.SendRequest(new AboutDocumentParams(new Document(DocumentA)), cancel.Token);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancel.Cancel();
        var changed = DateTime.UtcNow;
        wire.Client.SendNotification(new ChangeParams(new Document(DocumentA)));

        await Assert.ThrowsAsync<OperationCanceledException>(() => answer.WaitAsync(TimeSpan.FromSeconds(10)));
        await wire.Applied.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsLessThan(TimeSpan.FromSeconds(3), DateTime.UtcNow - changed, "the change didn't wait for the cancelled request's five seconds");
    }

    [TestMethod]
    public async Task ARequestThatStartsWhileAChangeToItsDocumentIsUnapplied_IsContentModified()
    {
        var incoming = new IncomingDocumentChanges();
        incoming.Arrived(new Uri(DocumentA));

        Assert.IsTrue(await IsUnappliedAsync(incoming, DocumentA));
        Assert.AreEqual(42, await incoming.WhileCurrentAsync(new Uri(DocumentB), CancellationToken.None, _ => Task.FromResult(42)), "another document is current");

        incoming.Applied(new Uri(DocumentA));
        Assert.AreEqual(42, await incoming.WhileCurrentAsync(new Uri(DocumentA), CancellationToken.None, _ => Task.FromResult(42)), "the change is applied");
    }

    // a request made of every document, such as workspace/diagnostic, is given up for a change to any of them.
    [TestMethod]
    public async Task ARequestAboutTheWorkspace_IsContentModified_ByAChangeToAnyDocument()
    {
        var incoming = new IncomingDocumentChanges();
        Assert.AreEqual(42, await incoming.WhileWorkspaceCurrentAsync(CancellationToken.None, _ => Task.FromResult(42)), "nothing is unapplied");

        incoming.Arrived(new Uri(DocumentB));
        await Assert.ThrowsAsync<RpcErrorException>(() => incoming.WhileWorkspaceCurrentAsync(CancellationToken.None, _ => Task.FromResult(42)),
            "a change unapplied as it starts");

        incoming.Applied(new Uri(DocumentB));
        var started = new TaskCompletionSource();
        var running = incoming.WhileWorkspaceCurrentAsync(CancellationToken.None, async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        });
        await started.Task;
        incoming.Arrived(new Uri(DocumentA));

        var modified = await Assert.ThrowsAsync<RpcErrorException>(() => running.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(-32801, modified.Code, "ContentModified: a change that arrives while it runs");
    }

    // a client opens a README.md beside the modules: OmniSharp hands its didOpen to no handler, so it is never applied. Waited for, every workspace/diagnostic
    // after it was answered ContentModified, and the client pulled again, for good.
    [TestMethod]
    public async Task AChangeToADocumentNoHandlerServes_IsNotWaitedFor()
    {
        var incoming = new IncomingDocumentChanges();
        incoming.Serve(SupportedLanguages.DocumentSelector);

        incoming.Arrived(new Uri("file:///c:/ws/README.md"));

        Assert.AreEqual(42, await incoming.WhileWorkspaceCurrentAsync(CancellationToken.None, _ => Task.FromResult(42)));
        Assert.IsFalse(await IsUnappliedAsync(incoming, "file:///c:/ws/README.md"));
    }

    [TestMethod]
    [DataRow("file:///c:/ws/Module1.bas")]
    [DataRow("file:///c:/ws/Classes/Class1.cls")]
    [DataRow("file:///c:/ws/Form1.frm")]
    [DataRow("file:///c:/ws/Program1.rdc")]
    public async Task AChangeToADocumentAHandlerServes_IsWaitedFor(string document)
    {
        var incoming = new IncomingDocumentChanges();
        incoming.Serve(SupportedLanguages.DocumentSelector);

        incoming.Arrived(new Uri(document));

        await Assert.ThrowsAsync<RpcErrorException>(() => incoming.WhileWorkspaceCurrentAsync(CancellationToken.None, _ => Task.FromResult(42)));
    }

    [TestMethod]
    public async Task AChangeAppliedThatNeverArrived_OwesNothing()
    {
        var incoming = new IncomingDocumentChanges();
        incoming.Applied(new Uri(DocumentA));
        incoming.Arrived(new Uri(DocumentA));
        incoming.Applied(new Uri(DocumentA));

        Assert.AreEqual(42, await incoming.WhileCurrentAsync(new Uri(DocumentA), CancellationToken.None, _ => Task.FromResult(42)));
    }

    private static string Framed(string json) => $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";

    // what the tap reads, in reads of the given sizes, is passed on byte for byte; the changes it saw are said.
    private static (byte[] PassedOn, IncomingDocumentChanges Incoming) ReadThrough(string wire, int readSize)
    {
        var incoming = new IncomingDocumentChanges();
        using var tap = incoming.Tap(new MemoryStream(Encoding.UTF8.GetBytes(wire)));
        using var passedOn = new MemoryStream();
        var buffer = new byte[readSize];
        int read;
        while ((read = tap.Read(buffer, 0, buffer.Length)) > 0)
        {
            passedOn.Write(buffer, 0, read);
        }
        return (passedOn.ToArray(), incoming);
    }

    // a request about the document, made now, is answered ContentModified: the error OmniSharp writes with that code.
    private static async Task<bool> IsUnappliedAsync(IncomingDocumentChanges incoming, string document)
    {
        try
        {
            await incoming.WhileCurrentAsync(new Uri(document), CancellationToken.None, _ => Task.FromResult(0));
            return false;
        }
        catch (RpcErrorException error) when (error.Code == ErrorCodes.ContentModified)
        {
            return true;
        }
    }

    [TestMethod]
    [DataRow(1, DisplayName = "a byte at a time")]
    [DataRow(7, DisplayName = "7 bytes at a time")]
    [DataRow(4096, DisplayName = "all at once")]
    public async Task TheTap_PassesEveryByteOn_AndSeesEachChangeToADocument(int readSize)
    {
        var wire = Framed("""{"jsonrpc":"2.0","id":1,"method":"textDocument/diagnostic","params":{"textDocument":{"uri":"file:///c:/ws/B.bas"}}}""")
            + "Content-Type: application/vscode-jsonrpc; charset=utf-8\r\n" + Framed("""{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///c:/ws/A.bas","version":2},"contentChanges":[{"text":"é"}]}}""")
            + Framed("""{"jsonrpc":"2.0","method":"textDocument/didSave","params":{"textDocument":{"uri":"file:///c:/ws/C.bas"}}}""");

        var (passedOn, incoming) = ReadThrough(wire, readSize);

        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(wire), passedOn);
        Assert.IsTrue(await IsUnappliedAsync(incoming, DocumentA), "didChange changes A");
        Assert.IsFalse(await IsUnappliedAsync(incoming, DocumentB), "a pull changes nothing");
        Assert.IsFalse(await IsUnappliedAsync(incoming, "file:///c:/ws/C.bas"), "didSave changes nothing");
    }

    [TestMethod]
    public async Task TheTap_PassesOnWhatItCannotRead_AndSeesTheMessagesAfterIt()
    {
        var wire = Framed("""{"method":"textDocument/didOpen", not json""") + Framed("""{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///c:/ws/A.bas","languageId":"vba","version":1,"text":""}}}""");

        var (passedOn, incoming) = ReadThrough(wire, 5);

        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(wire), passedOn);
        Assert.IsTrue(await IsUnappliedAsync(incoming, DocumentA));
    }
}
