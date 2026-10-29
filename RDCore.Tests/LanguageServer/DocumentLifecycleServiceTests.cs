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
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Diagnostics;
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
    private readonly IWorkspaceRefresh _refresh = Substitute.For<IWorkspaceRefresh>();
    private readonly WorkspaceDocumentService _documents;
    private readonly DocumentLifecycleService _sut;

    public DocumentLifecycleServiceTests()
    {
        _documents = new WorkspaceDocumentService(
            new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance), NullLogger<WorkspaceDocumentService>.Instance, _files.Path, _files.File);
        _documents.Initialize(Root);

        // the text of the document, as it is when it is parsed.
        _parsing.ParseDocumentAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ModuleParser().Parse((Uri)call[0], _documents.TryGetDocument((Uri)call[0], out var document) ? document.Text : string.Empty)));
        // the static phase finds one thing, and the runtime phase nothing: one publication.
        _diagnostics.GetPhaseAsync(Arg.Any<Uri>(), AnalysisPhase.Static, Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(DocumentDiagnosticsResult.Fresh(1, [Diagnostic()])));
        _diagnostics.GetPhaseAsync(Arg.Any<Uri>(), AnalysisPhase.Runtime, Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(DocumentDiagnosticsResult.Fresh(1, [])));

        // a client that is pushed to; one that pulls is told nothing (ForAClientThatPulls_NothingIsFoundOutOrPublished).
        _publisher.IsEnabled.Returns(true);
        _sut = Lifecycle(TimeSpan.Zero);
    }

    private DocumentLifecycleService Lifecycle(TimeSpan? editDelay, Func<TimeSpan, CancellationToken, Task>? wait = null)
        => new(_documents, _parsing, _symbols, _diagnostics, _publisher, _refresh, NullLogger<DocumentLifecycleService>.Instance, editDelay, wait);

    // a lifecycle whose refresh of an edit waits until it is hurried or superseded: no burst of edits is ever over on its own.
    private DocumentLifecycleService WaitingForMoreEdits()
        => Lifecycle(DocumentLifecycleService.EditDelay, (_, token) => Task.Delay(Timeout.Infinite, token));

    // each sync of the module with the host: when it starts, and what lets it finish, in the order they are asked for.
    private (TaskCompletionSource[] Started, TaskCompletionSource[] Released) GatedSyncs(int count)
    {
        var started = Enumerable.Range(0, count).Select(_ => new TaskCompletionSource()).ToArray();
        var released = Enumerable.Range(0, count).Select(_ => new TaskCompletionSource()).ToArray();
        var calls = -1;
        _symbols.SyncDocumentAsync(Mod1, Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            var call = Interlocked.Increment(ref calls);
            started[call].TrySetResult();
            await released[call].Task;
        });

        return (started, released);
    }

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

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
            _diagnostics.GetPhaseAsync(Mod1, AnalysisPhase.Static, Arg.Any<CancellationToken>());
            _publisher.Publish(Mod1, 4, Arg.Any<IReadOnlyList<Diagnostic>>());
            _diagnostics.GetPhaseAsync(Mod1, AnalysisPhase.Runtime, Arg.Any<CancellationToken>());
        });
    }

    [TestMethod]
    public async Task WhatIsReady_IsPublished_BeforeTheSlowPhaseIsDone_AndTheSlowPhaseAddsToIt()
    {
        var runtimeStarted = new TaskCompletionSource();
        var releaseRuntime = new TaskCompletionSource();
        _diagnostics.GetPhaseAsync(Mod1, AnalysisPhase.Runtime, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                runtimeStarted.SetResult();
                await releaseRuntime.Task;
                return DocumentDiagnosticsResult.Fresh(4, [Diagnostic(), Diagnostic()]);
            });
        var published = new List<int>();
        _publisher.When(publisher => publisher.Publish(Mod1, 4, Arg.Any<IReadOnlyList<Diagnostic>>()))
            .Do(call => published.Add(call.Arg<IReadOnlyList<Diagnostic>>().Count));

        _sut.Opened(Mod1, "Public Sub Foo()\r\nEnd Sub", version: 4);
        await runtimeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        CollectionAssert.AreEqual(new[] { 1 }, published, "the static findings are with the client while the runtime phase is still being found");

        releaseRuntime.SetResult();
        await _sut.WhenRefreshedAsync(Mod1);

        CollectionAssert.AreEqual(new[] { 1, 3 }, published, "and the next publication is all of it, since a publication replaces the one before");
    }

    [TestMethod]
    public async Task ARefreshSupersededWhileTheSlowPhaseRuns_PublishesNothingMoreForTheTextThatIsGone()
    {
        var runtimeStarted = new TaskCompletionSource();
        var releaseRuntime = new TaskCompletionSource();
        _diagnostics.GetPhaseAsync(Mod1, AnalysisPhase.Runtime, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                runtimeStarted.TrySetResult();
                await releaseRuntime.Task;
                return DocumentDiagnosticsResult.Fresh(1, [Diagnostic()]);
            });

        _sut.Opened(Mod1, "a", version: 1);
        await runtimeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _publisher.ClearReceivedCalls();

        _ = _sut.Changed(Mod1, 2, [Full("b")]);
        releaseRuntime.SetResult();
        await _sut.WhenRefreshedAsync(Mod1);

        _publisher.DidNotReceive().Publish(Mod1, 1, Arg.Any<IReadOnlyList<Diagnostic>>());
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

    // a client that pulls asks for what it shows itself: the refresh finds out nothing for it, and pushes nothing. The host is told of the text all the same.
    [TestMethod]
    public async Task ForAClientThatPulls_NothingIsFoundOutOrPublished()
    {
        _publisher.IsEnabled.Returns(false);

        _sut.Opened(Mod1, "a", 1);
        await _sut.WhenRefreshedAsync(Mod1);
        _ = _sut.Changed(Mod1, 2, [Full("b")]);
        await _sut.WhenRefreshedAsync(Mod1);

        await _symbols.Received(2).SyncDocumentAsync(Mod1, Arg.Any<CancellationToken>());
        await _diagnostics.DidNotReceiveWithAnyArgs().GetPhaseAsync(default!, default, default);
        _publisher.DidNotReceiveWithAnyArgs().Publish(default!, default, default!);
    }

    // as the user types: each edit supersedes the one before while the refresh waits for more, and only the text that is there at the end is refreshed.
    [TestMethod]
    public async Task ABurstOfEdits_IsRefreshedOnce_AtTheLastVersion()
    {
        var pause = new TaskCompletionSource();
        var sut = Lifecycle(DocumentLifecycleService.EditDelay, (_, token) => pause.Task.WaitAsync(token));
        sut.Opened(Mod1, "a", 1);
        await sut.WhenRefreshedAsync(Mod1);
        _parsing.ClearReceivedCalls();
        _symbols.ClearReceivedCalls();
        _publisher.ClearReceivedCalls();

        for (var version = 2; version <= 6; version++)
        {
            _ = sut.Changed(Mod1, version, [Full("x" + version)]);
        }

        pause.SetResult();
        await sut.WhenRefreshedAsync(Mod1);

        await _parsing.Received(1).ParseDocumentAsync(Mod1, Arg.Any<CancellationToken>());
        await _symbols.Received(1).SyncDocumentAsync(Mod1, Arg.Any<CancellationToken>());
        _publisher.Received(1).Publish(Mod1, 6, Arg.Any<IReadOnlyList<Diagnostic>>());
        _publisher.DidNotReceive().Publish(Mod1, Arg.Is<int?>(version => version < 6), Arg.Any<IReadOnlyList<Diagnostic>>());
    }

    [TestMethod]
    public async Task TheRefreshOfAnEdit_WaitsAQuarterOfASecond_WhenNothingElseIsSaid()
    {
        var asked = new TaskCompletionSource<TimeSpan>();
        var sut = Lifecycle(editDelay: null, (span, _) =>
        {
            asked.TrySetResult(span);
            return Task.CompletedTask;
        });
        sut.Opened(Mod1, "a", 1);
        await sut.WhenRefreshedAsync(Mod1);

        _ = sut.Changed(Mod1, 2, [Full("b")]);

        Assert.AreEqual(TimeSpan.FromMilliseconds(250), await asked.Task.WaitAsync(Patience));
        Assert.AreEqual(TimeSpan.FromMilliseconds(250), DocumentLifecycleService.EditDelay);
    }

    // what is found of a document is of what the host has of it: a request for it does not wait for more edits, and waits for the host to have this one.
    [TestMethod]
    public async Task ARequestWhileTheRefreshWaits_StartsItAtOnce_AndWaitsForTheHost()
    {
        var sut = WaitingForMoreEdits();
        sut.Opened(Mod1, "a", 1);
        await sut.WhenRefreshedAsync(Mod1);
        var (started, released) = GatedSyncs(1);

        _ = sut.Changed(Mod1, 2, [Full("b")]);
        var synced = sut.WhenSyncedAsync(Mod1, CancellationToken.None);
        await started[0].Task.WaitAsync(Patience);

        Assert.IsFalse(synced.IsCompleted, "the host does not have the edit yet");
        released[0].SetResult();
        await synced.WaitAsync(Patience);
    }

    [TestMethod]
    public async Task ARequestForADocumentWithNoRefresh_DoesNotWait()
        => await _sut.WhenSyncedAsync(Mod1, CancellationToken.None).WaitAsync(Patience);

    [TestMethod]
    [DataRow(true, DisplayName = "the parse")]
    [DataRow(false, DisplayName = "the sync with the host")]
    public async Task ARefreshThatFails_DoesNotKeepARequestWaiting(bool parse)
    {
        _sut.Opened(Mod1, "a", 1);
        await _sut.WhenRefreshedAsync(Mod1);
        if (parse)
        {
            _parsing.ParseDocumentAsync(Mod1, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("no parse"));
        }
        else
        {
            _symbols.SyncDocumentAsync(Mod1, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("no host"));
        }

        _ = _sut.Changed(Mod1, 2, [Full("b")]);

        await _sut.WhenSyncedAsync(Mod1, CancellationToken.None).WaitAsync(Patience);
    }

    // a refresh another edit supersedes hands the wait on: what is waited for is the host having the text there is.
    [TestMethod]
    public async Task ARequestWaitingForARefreshThatIsSuperseded_WaitsForTheOneThatSupersedesIt()
    {
        var sut = WaitingForMoreEdits();
        sut.Opened(Mod1, "a", 1);
        await sut.WhenRefreshedAsync(Mod1);
        var (started, released) = GatedSyncs(2);

        _ = sut.Changed(Mod1, 2, [Full("b")]);
        var synced = sut.WhenSyncedAsync(Mod1, CancellationToken.None);
        await started[0].Task.WaitAsync(Patience);
        var superseded = sut.WhenRefreshedAsync(Mod1);
        _ = sut.Changed(Mod1, 3, [Full("c")]);
        _ = sut.WhenSyncedAsync(Mod1, CancellationToken.None);
        await started[1].Task.WaitAsync(Patience);
        released[0].SetResult();
        await superseded.WaitAsync(Patience);
        await Task.WhenAny(synced, Task.Delay(100));

        Assert.IsFalse(synced.IsCompleted, "the host does not have the text there is yet");
        released[1].SetResult();
        await synced.WaitAsync(Patience);
    }

    // a save as the edit is brought to the host refreshes the same version again: what waits for the host waits for that refresh, whose parse is the one the
    // host is given.
    [TestMethod]
    public async Task ARequestWaitingForAnEdit_ThatASaveSupersedes_WaitsForTheSavesRefresh()
    {
        var sut = WaitingForMoreEdits();
        sut.Opened(Mod1, "a", 1);
        await sut.WhenRefreshedAsync(Mod1);
        var (started, released) = GatedSyncs(2);

        _ = sut.Changed(Mod1, 2, [Full("b")]);
        var synced = sut.WhenSyncedAsync(Mod1, CancellationToken.None);
        await started[0].Task.WaitAsync(Patience);
        var edited = sut.WhenRefreshedAsync(Mod1);
        sut.Saved(Mod1, text: null);
        await started[1].Task.WaitAsync(Patience);
        released[0].SetResult();
        await edited.WaitAsync(Patience);
        await Task.WhenAny(synced, Task.Delay(100));

        Assert.IsFalse(synced.IsCompleted, "the save's refresh has not brought the host up to date yet");
        released[1].SetResult();
        await synced.WaitAsync(Patience);
    }

    [TestMethod]
    public async Task ARequestWaitingForTheHost_IsDoneWaiting_WhenTheDocumentIsClosed()
    {
        var sut = WaitingForMoreEdits();
        sut.Opened(Mod1, "a", 1);
        await sut.WhenRefreshedAsync(Mod1);
        var (started, released) = GatedSyncs(1);

        _ = sut.Changed(Mod1, 2, [Full("b")]);
        var synced = sut.WhenSyncedAsync(Mod1, CancellationToken.None);
        await started[0].Task.WaitAsync(Patience);
        await sut.ClosedAsync(Mod1);
        released[0].SetResult();

        await synced.WaitAsync(Patience);
    }

    [TestMethod]
    public async Task ARequestForEveryDocument_StartsEachRefreshThatWaits_AndWaitsForEach()
    {
        var sut = WaitingForMoreEdits();
        sut.Opened(Mod1, "a", 1);
        await sut.WhenRefreshedAsync(Mod1);
        var (started, released) = GatedSyncs(1);

        _ = sut.Changed(Mod1, 2, [Full("b")]);
        var synced = sut.WhenAllSyncedAsync(CancellationToken.None);
        await started[0].Task.WaitAsync(Patience);

        Assert.IsFalse(synced.IsCompleted);
        released[0].SetResult();
        await synced.WaitAsync(Patience);
    }

    // a program that waits is checked against the whole workspace as the documents are: the module edited is loaded first, and the others are defined again
    // against what it declares after it - which is waited for too, the refresh that waits for more edits started at once.
    [TestMethod]
    public async Task TheWorkspaceLoaded_IsWaitedFor_UntilTheOthersAreDefinedAgainstTheEdit()
    {
        var sut = WaitingForMoreEdits();
        sut.Opened(Mod1, Declares, 1);
        await sut.WhenRefreshedAsync(Mod1);
        var (syncing, release) = (new TaskCompletionSource(), new TaskCompletionSource());
        SyncingTheWorkspace(async _ =>
        {
            syncing.TrySetResult();
            await release.Task;
        });

        _ = sut.Changed(Mod1, 2, [Full(Declares.Replace("Foo", "Bar"))]);
        var loaded = sut.WhenAllLoadedAsync(CancellationToken.None);
        await syncing.Task.WaitAsync(Patience);

        Assert.IsFalse(loaded.IsCompleted, "the others are being defined again against what the module declares now");
        release.SetResult();
        await loaded.WaitAsync(Patience);
    }

    // a newer edit's load supersedes the one waited for, and the wait goes on to it.
    [TestMethod]
    public async Task TheWorkspaceLoaded_IsWaitedFor_AsANewerEditHasItLoaded()
    {
        var sut = WaitingForMoreEdits();
        sut.Opened(Mod1, Declares, 1);
        await sut.WhenRefreshedAsync(Mod1);
        var (syncing, release) = (new[] { new TaskCompletionSource(), new TaskCompletionSource() }, new[] { new TaskCompletionSource(), new TaskCompletionSource() });
        SyncingTheWorkspace(async call =>
        {
            syncing[call - 1].TrySetResult();
            await release[call - 1].Task;
        });

        _ = sut.Changed(Mod1, 2, [Full(Declares.Replace("Foo", "Bar"))]);
        var loaded = sut.WhenAllLoadedAsync(CancellationToken.None);
        await syncing[0].Task.WaitAsync(Patience);
        var first = sut.WhenRefreshedAsync(Mod1);
        _ = sut.Changed(Mod1, 3, [Full(Declares.Replace("Foo", "Baz"))]);
        _ = sut.WhenAllSyncedAsync(CancellationToken.None);
        release[0].SetResult();
        await first.WaitAsync(Patience);
        await syncing[1].Task.WaitAsync(Patience);

        Assert.IsFalse(loaded.IsCompleted, "the newer edit's others are being defined again");
        release[1].SetResult();
        await loaded.WaitAsync(Patience);
    }

    [TestMethod]
    public async Task TheWorkspaceLoaded_IsNotWaitedFor_WhenNothingIsBeingLoaded()
    {
        _sut.Opened(Mod1, Declares, 1);
        await _sut.WhenRefreshedAsync(Mod1);

        await _sut.WhenAllLoadedAsync(CancellationToken.None).WaitAsync(Patience);
    }

    // an edit loaded while a program waits is checked at once: what is found of the document after it is of what the program does not have.
    [TestMethod]
    public async Task AnEditLoaded_HasTheProgramThatWaitsCheckedAgainstIt_BeforeItsFindingsAre()
    {
        var editStatus = Substitute.For<RDCore.LanguageServer.Debugging.IEditStatusMonitor>();
        var sut = new DocumentLifecycleService(_documents, _parsing, _symbols, _diagnostics, _publisher, _refresh, NullLogger<DocumentLifecycleService>.Instance,
            TimeSpan.Zero, editStatus: editStatus);

        sut.Opened(Mod1, Declares, 1);
        await sut.WhenRefreshedAsync(Mod1);

        Received.InOrder(() =>
        {
            _symbols.SyncDocumentAsync(Mod1, Arg.Any<CancellationToken>());
            editStatus.CheckAsync(Arg.Any<CancellationToken>(), true);
            _diagnostics.GetPhaseAsync(Mod1, AnalysisPhase.Static, Arg.Any<CancellationToken>());
        });
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

    // ---- the other documents ----

    private const string Declares = "Public Sub Foo()\r\n    Debug.Print 1\r\nEnd Sub\r\n";

    private async Task EditedAsync(string text)
    {
        _sut.Opened(Mod1, Declares, 1);
        await _sut.WhenRefreshedAsync(Mod1);
        _refresh.ClearReceivedCalls();
        _symbols.ClearReceivedCalls();

        _sut.Changed(Mod1, 2, [Full(text)]);
        await _sut.WhenRefreshedAsync(Mod1);
    }

    [TestMethod]
    public async Task AChangeOfWhatTheModuleDeclares_DefinesTheWorkspaceAgain_AndThenAsksTheClientToRefresh()
    {
        await EditedAsync(Declares.Replace("Foo", "Bar"));

        Received.InOrder(() =>
        {
            _symbols.SyncDocumentAsync(Mod1, Arg.Any<CancellationToken>());
            _publisher.Publish(Mod1, 2, Arg.Any<IReadOnlyList<Diagnostic>>());
            _symbols.SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
            _refresh.Changed();
        });
    }

    private void SyncingTheWorkspace(Func<int, Task> each)
    {
        var calls = 0;
        _symbols.SyncWorkspaceAsync(Arg.Any<CancellationToken>(), Arg.Any<IProgress<RDCore.SDK.Server.Progress.WorkProgressStep>?>(), Arg.Any<bool>())
            .Returns(_ => each(Interlocked.Increment(ref calls)));
    }

    // a character typed, then taken back while the others are defined again against it: the text tells what it did before, and the others are defined
    // against what it told in between unless they are defined again.
    [TestMethod]
    public async Task AChangeTakenBackWhileTheOthersAreDefinedAgainstIt_DefinesThemAgain()
    {
        var syncing = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _sut.Opened(Mod1, Declares, 1);
        await _sut.WhenRefreshedAsync(Mod1);
        SyncingTheWorkspace(async call =>
        {
            if (call == 1)
            {
                syncing.TrySetResult();
                await release.Task;
            }
        });
        _refresh.ClearReceivedCalls();

        _sut.Changed(Mod1, 2, [Full(Declares.Replace("Foo", "Fooo"))]);
        var taken = _sut.WhenRefreshedAsync(Mod1);
        await syncing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _sut.Changed(Mod1, 3, [Full(Declares)]);
        await _sut.WhenRefreshedAsync(Mod1);
        release.SetResult();
        await taken;

        await _symbols.Received(2).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
        _refresh.Received(2).Changed();

        // what they are defined against now is what the text tells: nothing more to do for it.
        _sut.Changed(Mod1, 4, [Full(Declares.Replace("Debug.Print 1", "Debug.Print 2"))]);
        await _sut.WhenRefreshedAsync(Mod1);
        await _symbols.Received(2).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
    }

    [TestMethod]
    public async Task ASyncOfTheOthersThatDidNotFinish_IsDoneAgain_ByTheNextChange_WhateverItTells()
    {
        _sut.Opened(Mod1, Declares, 1);
        await _sut.WhenRefreshedAsync(Mod1);
        SyncingTheWorkspace(call => call == 1 ? Task.FromCanceled(new CancellationToken(canceled: true)) : Task.CompletedTask);
        _refresh.ClearReceivedCalls();

        _sut.Changed(Mod1, 2, [Full(Declares.Replace("Foo", "Bar"))]);
        await _sut.WhenRefreshedAsync(Mod1);
        _sut.Changed(Mod1, 3, [Full(Declares.Replace("Foo", "Bar").Replace("Debug.Print 1", "Debug.Print 2"))]);
        await _sut.WhenRefreshedAsync(Mod1);

        await _symbols.Received(2).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
        _refresh.Received(2).Changed();
    }

    [TestMethod]
    [DataRow("Debug.Print 2", DisplayName = "a literal")]
    [DataRow("Debug.Print 1 ' one", DisplayName = "a comment")]
    public async Task AChangeInsideAProcedure_ThatDeclaresAndNamesNothingNew_AsksNothing(string edited)
    {
        await EditedAsync(Declares.Replace("Debug.Print 1", edited));

        _refresh.DidNotReceive().Changed();
        await _symbols.Received(1).SyncDocumentAsync(Mod1, Arg.Any<CancellationToken>());
        await _symbols.DidNotReceiveWithAnyArgs().SyncWorkspaceAsync(default, default, default);
    }

    // opened before the workspace was parsed: the workspace is defined with it when it is.
    [TestMethod]
    public async Task AnOpenedDocument_TheServerHadNoTextOf_AsksNothing()
    {
        _sut.Opened(Mod1, Declares, 1);
        await _sut.WhenRefreshedAsync(Mod1);

        _refresh.DidNotReceive().Changed();
        await _symbols.DidNotReceiveWithAnyArgs().SyncWorkspaceAsync(default, default, default);
    }

    [TestMethod]
    public async Task AnOpenedDocument_WhoseTextIsWhatTheWorkspaceHad_AsksNothing()
    {
        TheParseOfTheDocumentIs(Mod1, Declares);

        _sut.Opened(Mod1, Declares, 1);
        await _sut.WhenRefreshedAsync(Mod1);

        _refresh.DidNotReceive().Changed();
    }

    // LSP 3.17 §URI: a document need not be a file; what is found of it is published at the address the client gave it.
    [TestMethod]
    public async Task ADocumentThatIsNoFile_HasItsDiagnosticsPublished_AtTheAddressOfTheClient()
    {
        var untitled = new Uri("untitled:Untitled-1");

        _sut.Opened(untitled, "Public Sub Foo()\r\nEnd Sub", 1);
        await _sut.WhenRefreshedAsync(untitled);

        await _parsing.Received(1).ParseDocumentAsync(untitled, Arg.Any<CancellationToken>());
        _publisher.Received(1).Publish(untitled, 1, Arg.Any<IReadOnlyList<Diagnostic>>());
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
        await _symbols.DidNotReceiveWithAnyArgs().DiscardModuleAsync(default!, default);
    }

    // a program a shell loads is a file of the user's, outside the workspace the shell composed: the client's document, and the workspace's no more once it is closed.
    private static readonly Uri Elsewhere = new(Path.Combine(Path.GetTempPath(), "rdcore-elsewhere", "Program1.rdc"));

    private void TheParseOfTheDocumentIsCached(Uri document) => TheParseOfTheDocumentIs(document, "Public Sub Foo()\r\nEnd Sub");

    private void TheParseOfTheDocumentIs(Uri document, string text)
    {
        var parse = new ModuleParser().Parse(document, text);
        _parsing.TryGetCached(document, out Arg.Any<ModuleParseResult>()).Returns(call => { call[1] = parse; return true; });
    }

    [TestMethod]
    public async Task ADocumentFromOutsideTheWorkspace_IsNotTrackedOnceClosed_ThoughItIsAFile_AndTheHostIsToldItsModuleIsGone()
    {
        _files.AddFile(Elsewhere.LocalPath, new MockFileData("10 X = 1"));
        _sut.Opened(Elsewhere, "10 X = 1", 1);
        await _sut.WhenRefreshedAsync(Elsewhere);
        TheParseOfTheDocumentIsCached(Elsewhere);

        await _sut.ClosedAsync(Elsewhere);
        await _sut.WhenRefreshedAsync(Elsewhere);

        Assert.IsFalse(_documents.TryGetDocument(Elsewhere, out _));
        await _symbols.Received(1).DiscardModuleAsync("Program1", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ADocumentThatIsNotAFile_TakesItsModuleOutOfTheHost_OnceClosed()
    {
        _sut.Opened(Mod1, "a", 1);
        await _sut.WhenRefreshedAsync(Mod1);
        TheParseOfTheDocumentIsCached(Mod1);

        _refresh.ClearReceivedCalls();

        await _sut.ClosedAsync(Mod1);

        await _symbols.Received(1).DiscardModuleAsync("Mod1", Arg.Any<CancellationToken>());
        await _symbols.Received(1).SyncWorkspaceAsync(Arg.Any<CancellationToken>(), null, true);
        _refresh.Received(1).Changed();
    }

    [TestMethod]
    public async Task ADocumentThatWasNeverParsed_HasNoModuleInTheHostToTakeOut()
    {
        _sut.Opened(Elsewhere, "10 X = 1", 1);

        await _sut.ClosedAsync(Elsewhere);

        await _symbols.DidNotReceiveWithAnyArgs().DiscardModuleAsync(default!, default);
    }

    [TestMethod]
    public async Task AHostThatCannotBeToldTheModuleIsGone_DoesNotStopTheDocumentFromClosing()
    {
        _sut.Opened(Mod1, "a", 1);
        await _sut.WhenRefreshedAsync(Mod1);
        TheParseOfTheDocumentIsCached(Mod1);
        _symbols.DiscardModuleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("the host is gone"));

        await _sut.ClosedAsync(Mod1);

        Assert.IsFalse(_documents.TryGetDocument(Mod1, out _));
        _publisher.Received(1).Clear(Mod1);
    }
}
