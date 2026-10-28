using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using System.IO.Abstractions.TestingHelpers;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// A client's notifications are read in one lane, but the document service is also written to by the project and by requests: a move to another path, and the
/// client's open and change of a document, are each made as one step, whichever is first. The steps are held where one is half made with a gate - never with a sleep.
/// </summary>
[TestClass]
public sealed class WorkspaceDocumentRenameRaceTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-race-ws");
    private static readonly TextDocumentIdentifier Old = new(new Uri(Path.Combine(Root, "Old.bas")));
    private static readonly TextDocumentIdentifier New = new(new Uri(Path.Combine(Root, "New.bas")));

    /// <summary>The state provider, holding the first call that <see cref="Gate"/> chooses until the test releases it.</summary>
    private sealed class GatedStates(DocumentStateProvider inner) : IDocumentStateProvider
    {
        public Func<string, TextDocumentIdentifier, bool>? Gate { get; set; }

        public ManualResetEventSlim Reached { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        private void Pass(string call, TextDocumentIdentifier id)
        {
            var gate = Gate;
            if (gate is not null && gate(call, id))
            {
                Gate = null;
                Reached.Set();
                Assert.IsTrue(Release.Wait(TimeSpan.FromSeconds(30)), "The gate was never released.");
            }
        }

        public void Initialize(IEnumerable<TextDocumentIdentifier> workspaceDocumentIds) => inner.Initialize(workspaceDocumentIds);

        public DocumentState GetCurrentState(TextDocumentIdentifier id)
        {
            Pass(nameof(GetCurrentState), id);
            return inner.GetCurrentState(id);
        }

        public void OnDocumentUnloaded(TextDocumentIdentifier id) => inner.OnDocumentUnloaded(id);

        public void OnDocumentLoaded(TextDocumentIdentifier id) => inner.OnDocumentLoaded(id);

        public void OnDocumentMissing(TextDocumentIdentifier id) => inner.OnDocumentMissing(id);

        public void OnDocumentLoadError(TextDocumentIdentifier id) => inner.OnDocumentLoadError(id);

        public void OnDocumentOpened(TextDocumentIdentifier id) => inner.OnDocumentOpened(id);

        public void OnDocumentClosed(TextDocumentIdentifier id) => inner.OnDocumentClosed(id);

        public bool IsTracked(TextDocumentIdentifier id) => inner.IsTracked(id);

        public void OnDocumentOpenedByClient(TextDocumentIdentifier id) => inner.OnDocumentOpenedByClient(id);

        public void Forget(TextDocumentIdentifier id)
        {
            Pass(nameof(Forget), id);
            inner.Forget(id);
        }

        public void Track(TextDocumentIdentifier id) => inner.Track(id);
    }

    private static async Task<(WorkspaceDocumentService Documents, GatedStates States)> LoadedOldAsync()
    {
        var files = new MockFileSystem();
        files.AddFile(Old.Uri.GetFileSystemPath(), new MockFileData("Attribute VB_Name = \"Old\"\r\n"));

        var states = new GatedStates(new DocumentStateProvider(NullLogger<DocumentStateProvider>.Instance));
        states.Initialize([Old]);
        var documents = new WorkspaceDocumentService(states, NullLogger<WorkspaceDocumentService>.Instance, files.Path, files.File);
        documents.Initialize(Root);
        Assert.IsTrue(await documents.TryLoadAsync(Old));
        return (documents, states);
    }

    // a step that fails fails the test that waits for it, not the process.
    private sealed class Step
    {
        private readonly Thread _thread;
        private Exception? _failure;

        public Step(Action work)
        {
            _thread = new Thread(() =>
            {
                try
                {
                    work();
                }
                catch (Exception exception)
                {
                    _failure = exception;
                }
            })
            { IsBackground = true };
            _thread.Start();
        }

        public ThreadState State => _thread.ThreadState;

        public void Join()
        {
            _thread.Join();
            if (_failure is not null)
            {
                Assert.Fail("A step failed: " + _failure);
            }
        }
    }

    private static Step Start(Action work) => new(work);

    // a thread that waits for the step another has half made is blocked on it - the positive signal that it has come as far as it can; one that is done has not been held.
    private static void UntilBlockedOrDone(Step step)
    {
        var spin = new SpinWait();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((step.State & (ThreadState.WaitSleepJoin | ThreadState.Stopped)) == 0)
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "The thread neither waited nor finished.");
            spin.SpinOnce();
        }
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ADocumentTheClientOpensAtTheNewPath_WhileItIsBeingMovedThere_IsTheClientsAndOpen()
    {
        var (documents, states) = await LoadedOldAsync();

        // the move is held after it put the document at the new path, before it is done with the state of it.
        states.Gate = (call, id) => call == nameof(IDocumentStateProvider.Forget) && id.Equals(New);
        var moving = Start(() => documents.Move(Old, New));
        Assert.IsTrue(states.Reached.Wait(TimeSpan.FromSeconds(30)), "The move never came to its state.");

        var opening = Start(() => documents.Open(New.Uri.ToUri(), "client text", 5));
        UntilBlockedOrDone(opening);
        states.Release.Set();
        moving.Join();
        opening.Join();

        Assert.IsTrue(documents.TryGetDocument(New.Uri.ToUri(), out var document));
        Assert.AreEqual("client text", document.Text);
        Assert.AreEqual(5, document.Version);
        Assert.AreEqual(1, document.Projects.Count);
        Assert.IsInstanceOfType<OpenedDocumentState>(states.GetCurrentState(New), "The client has the document open.");
        Assert.IsFalse(documents.TryGetDocument(Old.Uri.ToUri(), out _), "The document is not at the old path any more.");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AChangeToTheOldPath_ThatIsHalfMadeWhenTheDocumentMoves_DoesNotBringTheOldPathBack()
    {
        var (documents, states) = await LoadedOldAsync();
        documents.Open(Old.Uri.ToUri(), "first", 1);

        // the change has read the document and is held before it writes it back.
        states.Gate = (call, id) => call == nameof(IDocumentStateProvider.GetCurrentState) && id.Equals(Old);
        var changing = Start(() => documents.Change(Old.Uri.ToUri(), 2, [new TextDocumentContentChangeEvent { Text = "second" }]));
        Assert.IsTrue(states.Reached.Wait(TimeSpan.FromSeconds(30)), "The change never came to its state.");

        var moving = Start(() => documents.Move(Old, New));
        UntilBlockedOrDone(moving);
        states.Release.Set();
        changing.Join();
        moving.Join();

        Assert.IsFalse(documents.TryGetDocument(Old.Uri.ToUri(), out _), "The change did not bring the old path back.");
        Assert.IsTrue(documents.TryGetDocument(New.Uri.ToUri(), out var document));
        Assert.AreEqual("second", document.Text);
        Assert.AreEqual(2, document.Version);
        Assert.IsInstanceOfType<OpenedDocumentState>(states.GetCurrentState(New), "The document that was open is open where it moved.");
    }
}
