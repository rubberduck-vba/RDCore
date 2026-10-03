using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Symbols;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using RDCore.Parsing;
using System.IO.Abstractions.TestingHelpers;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What the server does when a client opens, edits, saves and closes a document (<strong>LSP 3.17</strong> §Text Document Synchronization): the document is brought up
/// to date at once, and what follows from the text - the parse, the host, the diagnostics - is of the version that is current, and of no other.
/// </summary>
[TestClass]
public sealed class DocumentLifecycleServiceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-lifecycle-ws");
    private static readonly Uri Mod1 = new(Path.Combine(Root, "Mod1.bas"));

    private readonly MockFileSystem _files = new();
    private readonly IParsingClientService _parsing = Substitute.For<IParsingClientService>();
    private readonly ISymbolSyncService _symbols = Substitute.For<ISymbolSyncService>();
    private readonly IDocumentDiagnosticsService _diagnostics = Substitute.For<IDocumentDiagnosticsService>();
    private readonly IDiagnosticsPublisher _publisher = Substitute.For<IDiagnosticsPublisher>();
    private readonly WorkspaceDocumentService _documents;
    private readonly DocumentLifecycleService _sut;

    public DocumentLifecycleServiceTests()
    {
        _documents = new WorkspaceDocumentService(
            new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance), NullLogger<WorkspaceDocumentService>.Instance, _files.Path, _files.File);
        _documents.Initialize(Root);

        _parsing.ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse((Uri)call[0], "Public Sub Foo()\r\nEnd Sub")));
        _diagnostics.GetAsync(Arg.Any<Uri>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(DocumentDiagnosticsResult.Fresh(1, [Diagnostic()])));

        _sut = new DocumentLifecycleService(_documents, _parsing, _symbols, _diagnostics, _publisher, NullLogger<DocumentLifecycleService>.Instance);
    }

    private static Diagnostic Diagnostic() => new() { Message = "wrong", Range = new Range(new Position(0, 0), new Position(0, 1)) };

    private static TextDocumentContentChangeEvent Full(string text) => new() { Text = text };

    // ---- didOpen ----

    [TestMethod]
    public async Task AnOpenedDocument_IsParsed_TheHostIsToldOfIt_AndItsDiagnosticsArePublished()
    {
        _sut.Opened(Mod1, "Public Sub Foo()\r\nEnd Sub", version: 4);
        await _sut.WhenRefreshedAsync(Mod1);

        Received.InOrder(() =>
        {
            _parsing.Invalidate(Mod1);
            _parsing.ParseDocumentAsync(Mod1, Arg.Any<CancellationToken>());
            _symbols.SyncDocumentAsync(Mod1, Arg.Any<CancellationToken>());
            _diagnostics.GetAsync(Mod1, null, Arg.Any<CancellationToken>());
            _publisher.Publish(Mod1, 4, Arg.Any<IReadOnlyList<Diagnostic>>());
        });
    }

    // ---- didChange ----

    [TestMethod]
    public async Task AChange_IsPublishedAtTheVersionOfTheClient()
    {
        _sut.Opened(Mod1, "a", 1);
        await _sut.WhenRefreshedAsync(Mod1);

        var outcome = _sut.Changed(Mod1, 2, [Full("b")]);
        await _sut.WhenRefreshedAsync(Mod1);

        Assert.AreEqual(DocumentChangeOutcome.Applied, outcome);
        _publisher.Received(1).Publish(Mod1, 2, Arg.Any<IReadOnlyList<Diagnostic>>());
    }

    [TestMethod]
    public async Task AChangeThatIsOutOfDate_IsIgnored_AndNothingFollowsFromIt()
    {
        _sut.Opened(Mod1, "a", 5);
        await _sut.WhenRefreshedAsync(Mod1);
        _publisher.ClearReceivedCalls();

        var outcome = _sut.Changed(Mod1, 3, [Full("stale")]);
        await _sut.WhenRefreshedAsync(Mod1);

        Assert.AreEqual(DocumentChangeOutcome.OutOfDate, outcome);
        _publisher.DidNotReceiveWithAnyArgs().Publish(default!, default, default!);
    }

    [TestMethod]
    public async Task ARefreshThatTheNextEditSupersedes_PublishesNothingForTheTextThatIsNotThereAnyMore()
    {
        var firstParseStarted = new TaskCompletionSource();
        var releaseFirstParse = new TaskCompletionSource();
        var calls = 0;
        _parsing.ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    firstParseStarted.SetResult();
                    await releaseFirstParse.Task;
                }

                return new ModuleParser().Parse((Uri)call[0], "Public Sub Foo()\r\nEnd Sub");
            });

        _sut.Opened(Mod1, "a", 1);
        await firstParseStarted.Task;
        var superseded = _sut.WhenRefreshedAsync(Mod1);

        _sut.Changed(Mod1, 2, [Full("b")]);
        releaseFirstParse.SetResult();
        await superseded;
        await _sut.WhenRefreshedAsync(Mod1);

        _publisher.DidNotReceive().Publish(Mod1, 1, Arg.Any<IReadOnlyList<Diagnostic>>());
        _publisher.Received(1).Publish(Mod1, 2, Arg.Any<IReadOnlyList<Diagnostic>>());
    }

    [TestMethod]
    public async Task AHostThatCannotBeBroughtUpToDate_DoesNotCostTheClientItsDiagnostics()
    {
        _symbols.SyncDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("the host is gone"));

        _sut.Opened(Mod1, "a", 1);
        await _sut.WhenRefreshedAsync(Mod1);

        _publisher.Received(1).Publish(Mod1, 1, Arg.Any<IReadOnlyList<Diagnostic>>());
    }

    // ---- didSave ----

    [TestMethod]
    public async Task ASave_RefreshesTheDocument_AndIsNoLongerUnsaved()
    {
        _sut.Opened(Mod1, "a", 1);
        _sut.Changed(Mod1, 2, [Full("b")]);
        await _sut.WhenRefreshedAsync(Mod1);
        _publisher.ClearReceivedCalls();

        _sut.Saved(Mod1, text: null);
        await _sut.WhenRefreshedAsync(Mod1);

        _documents.TryGetDocument(Mod1, out var document);
        Assert.IsFalse(document.IsDirty);
        _publisher.Received(1).Publish(Mod1, 2, Arg.Any<IReadOnlyList<Diagnostic>>());
    }

    [TestMethod]
    public async Task ASaveThatSaysTheTextItSaved_WhenItIsNotTheTextTheServerHas_IsALaterVersionOfTheText()
    {
        _sut.Opened(Mod1, "a", 1);
        await _sut.WhenRefreshedAsync(Mod1);

        _sut.Saved(Mod1, "what the client really has");
        await _sut.WhenRefreshedAsync(Mod1);

        _documents.TryGetDocument(Mod1, out var document);
        Assert.AreEqual("what the client really has", document.Text);
        Assert.AreEqual(2, document.Version, "what was derived from the text the server had is of the version before");
    }

    [TestMethod]
    public void ASaveOfADocumentThatIsNotOpen_IsIgnored()
    {
        _sut.Saved(new Uri(Path.Combine(Root, "Nope.bas")), null);

        _publisher.DidNotReceiveWithAnyArgs().Publish(default!, default, default!);
    }

    // ---- willSave, willSaveWaitUntil ----

    [TestMethod]
    public async Task BeforeASave_ThereAreNoEditsToMake()
        => Assert.IsEmpty(await _sut.WillSaveWaitUntilAsync(Mod1, TextDocumentSaveReason.Manual, CancellationToken.None));

    [TestMethod]
    public async Task ARequestForEditsBeforeASave_IsCancelled_WhenTheClientCancelsIt()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _sut.WillSaveWaitUntilAsync(Mod1, TextDocumentSaveReason.Manual, cancellation.Token));
    }

    [TestMethod]
    [DataRow(TextDocumentSaveReason.Manual)]
    [DataRow(TextDocumentSaveReason.AfterDelay)]
    [DataRow(TextDocumentSaveReason.FocusOut)]
    public void EveryReasonForASave_IsHandled(TextDocumentSaveReason reason)
        => _sut.WillSave(Mod1, reason);

    // ---- didClose ----

    [TestMethod]
    public async Task AClosedDocument_HasItsDiagnosticsCleared_AndItsParseForgotten()
    {
        _sut.Opened(Mod1, "a", 1);
        await _sut.WhenRefreshedAsync(Mod1);

        await _sut.ClosedAsync(Mod1);
        await _sut.WhenRefreshedAsync(Mod1);

        _publisher.Received(1).Clear(Mod1);
        _parsing.Received().Invalidate(Mod1);
        Assert.IsFalse(_documents.TryGetDocument(Mod1, out _), "a document that is not a file is not tracked once it is closed");
    }

    [TestMethod]
    public async Task ADocumentThatIsAFile_IsWhatIsOnDiskOnceItIsClosed_AndNothingIsPublishedForIt()
    {
        _files.AddFile(Path.Combine(Root, "Mod1.bas"), new MockFileData("on disk"));
        _sut.Opened(Mod1, "unsaved", 1);
        await _sut.WhenRefreshedAsync(Mod1);
        _publisher.ClearReceivedCalls();

        await _sut.ClosedAsync(Mod1);
        await _sut.WhenRefreshedAsync(Mod1);

        _documents.TryGetDocument(Mod1, out var document);
        Assert.AreEqual("on disk", document.Text);
        _publisher.DidNotReceive().Publish(Mod1, Arg.Any<int?>(), Arg.Is<IReadOnlyList<Diagnostic>>(found => found.Count > 0));
        await _symbols.Received().SyncDocumentAsync(Mod1, Arg.Any<CancellationToken>());
    }
}
