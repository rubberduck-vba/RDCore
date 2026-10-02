using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Server.Handlers.Document;
using RDCore.LanguageServer.Workspace.Services;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// The handlers of <strong>LSP 3.17</strong> §Text Document Synchronization say what the client said and nothing else: what the server does about it is the lifecycle service's.
/// </summary>
[TestClass]
public sealed class DocumentSynchronizationHandlerTests
{
    private static readonly DocumentUri Document = DocumentUri.From(new Uri("file:///c:/ws/Mod1.bas"));

    private readonly IDocumentLifecycleService _lifecycle = Substitute.For<IDocumentLifecycleService>();
    private readonly TextDocumentSelector _selector = new(new TextDocumentFilter { Pattern = "**/*.bas" });

    [TestMethod]
    public async Task DidOpen_OpensTheDocumentWithTheTextAndTheVersionTheClientSent()
    {
        await new DidOpenTextDocumentHandler(_lifecycle, _selector).Handle(
            new DidOpenTextDocumentParams { TextDocument = new TextDocumentItem { Uri = Document, Text = "Sub Foo()", Version = 3, LanguageId = "vba" } }, CancellationToken.None);

        _lifecycle.Received(1).Opened(Document.ToUri(), "Sub Foo()", 3);
    }

    [TestMethod]
    public async Task DidChange_PassesTheVersionAndEveryChange()
    {
        var changes = new[] { new TextDocumentContentChangeEvent { Text = "x" }, new TextDocumentContentChangeEvent { Text = "y" } };

        await new DidChangeTextDocumentHandler(_lifecycle, _selector).Handle(
            new DidChangeTextDocumentParams
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = Document, Version = 9 },
                ContentChanges = new Container<TextDocumentContentChangeEvent>(changes),
            }, CancellationToken.None);

        _lifecycle.Received(1).Changed(Document.ToUri(), 9, Arg.Is<IEnumerable<TextDocumentContentChangeEvent>>(received => received.SequenceEqual(changes)));
    }

    [TestMethod]
    [DataRow("the text", "the text")]
    [DataRow(null, null)]
    public async Task DidSave_ServesTheClientThatSendsTheText_AndTheOneThatDoesNot(string? sent, string? expected)
    {
        await new DidSaveTextDocumentHandler(_lifecycle, _selector).Handle(
            new DidSaveTextDocumentParams { TextDocument = new TextDocumentIdentifier(Document), Text = sent }, CancellationToken.None);

        _lifecycle.Received(1).Saved(Document.ToUri(), expected);
    }

    [TestMethod]
    [DataRow(TextDocumentSaveReason.Manual)]
    [DataRow(TextDocumentSaveReason.AfterDelay)]
    [DataRow(TextDocumentSaveReason.FocusOut)]
    public async Task WillSave_PassesTheReason(TextDocumentSaveReason reason)
    {
        await new WillSaveTextDocumentHandler(_lifecycle, _selector).Handle(
            new WillSaveTextDocumentParams { TextDocument = new TextDocumentIdentifier(Document), Reason = reason }, CancellationToken.None);

        _lifecycle.Received(1).WillSave(Document.ToUri(), reason);
    }

    [TestMethod]
    public async Task WillSaveWaitUntil_AnswersWithTheEditsTheServerHas_WhichMayBeNone()
    {
        _lifecycle.WillSaveWaitUntilAsync(Arg.Any<Uri>(), Arg.Any<TextDocumentSaveReason>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TextEdit>>([]));

        var edits = await new WillSaveWaitUntilTextDocumentHandler(_lifecycle, _selector).Handle(
            new WillSaveWaitUntilTextDocumentParams { TextDocument = new TextDocumentIdentifier(Document), Reason = TextDocumentSaveReason.Manual }, CancellationToken.None);

        Assert.IsNotNull(edits);
        Assert.IsEmpty(edits);
    }

    [TestMethod]
    public async Task DidClose_ClosesTheDocument()
    {
        await new DidCloseTextDocumentHandler(_lifecycle, _selector).Handle(
            new DidCloseTextDocumentParams { TextDocument = new TextDocumentIdentifier(Document) }, CancellationToken.None);

        await _lifecycle.Received(1).ClosedAsync(Document.ToUri());
    }

    // ---- publishDiagnostics ----

    [TestMethod]
    public void AClientThatPulls_IsNotPushedTo()
    {
        var server = Substitute.For<ILanguageServer>();
        var publisher = new DiagnosticsPublisher(NullLogger<DiagnosticsPublisher>.Instance);
        publisher.Attach(server, enabled: false);

        publisher.Publish(Document.ToUri(), 1, [new Diagnostic { Message = "wrong" }]);
        publisher.Clear(Document.ToUri());

        Assert.IsFalse(publisher.IsEnabled);
        Assert.IsEmpty(server.ReceivedCalls().Where(call => call.GetMethodInfo().Name != "get_TextDocument"));
    }

    [TestMethod]
    public void APublisherThatIsNotAttached_PushesNothing_AndDoesNotFail()
    {
        var publisher = new DiagnosticsPublisher(NullLogger<DiagnosticsPublisher>.Instance);

        publisher.Publish(Document.ToUri(), 1, [new Diagnostic { Message = "wrong" }]);

        Assert.IsFalse(publisher.IsEnabled);
    }
}
