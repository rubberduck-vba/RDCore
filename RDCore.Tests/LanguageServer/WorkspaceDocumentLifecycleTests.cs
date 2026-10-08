using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.Services;
using RDCore.LanguageServer.Workspace.States;
using System.IO.Abstractions.TestingHelpers;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// <strong>LSP 3.17</strong> §Text Document Synchronization: while a client has a document open its text is the client's, and the server follows its versions.
/// </summary>
[TestClass]
public sealed class WorkspaceDocumentLifecycleTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rdcore-lifecycle-ws");

    private readonly MockFileSystem _files = new();
    private readonly DocumentStateProvider _state = new(NullLogger<DocumentStateProvider>.Instance);

    private WorkspaceDocumentService Sut()
    {
        var service = new WorkspaceDocumentService(_state, NullLogger<WorkspaceDocumentService>.Instance, _files.Path, _files.File);
        service.Initialize(Root);
        return service;
    }

    private static Uri UriOf(string name) => new(Path.Combine(Root, name));

    private static TextDocumentContentChangeEvent Full(string text) => new() { Text = text };

    private static TextDocumentContentChangeEvent Edit(int line, int start, int end, string text)
        => new() { Range = new Range(new Position(line, start), new Position(line, end)), Text = text };

    private static DocumentState StateOf(DocumentStateProvider state, Uri uri) => state.GetCurrentState(new TextDocumentIdentifier(uri));

    // ---- didOpen ----

    [TestMethod]
    public void AnOpenedDocument_IsTrackedWithTheTextAndTheVersionTheClientGave()
    {
        var sut = Sut();

        var document = sut.Open(UriOf("Mod1.bas"), "Sub Foo()\r\nEnd Sub", version: 7);

        Assert.AreEqual(7, document.Version);
        Assert.IsFalse(document.IsDirty, "what the editor opens is what is on disk");
        Assert.IsTrue(sut.TryGetDocument(UriOf("Mod1.bas"), out var found));
        Assert.AreEqual("Sub Foo()\r\nEnd Sub", found.Text);
        Assert.IsInstanceOfType<OpenedDocumentState>(StateOf(_state, UriOf("Mod1.bas")));
    }

    [TestMethod]
    public void ADocumentThatIsNotInTheWorkspaceFolder_IsOpenedAllTheSame()
    {
        var elsewhere = new Uri(Path.Combine(Path.GetTempPath(), "somewhere-else", "Loose.rdc"));
        var sut = Sut();
        sut.Initialize(Root);

        sut.Open(elsewhere, "PRINT 1", version: 1);

        Assert.IsTrue(sut.TryGetDocument(elsewhere, out var found));
        Assert.AreEqual("PRINT 1", found.Text);
    }

    [TestMethod]
    public async Task ADocumentThatWasMissing_IsOpenedWithTheTextOfTheClient()
    {
        var sut = Sut();
        sut.Initialize(Root);
        var id = new TextDocumentIdentifier(UriOf("Gone.bas"));
        _state.Initialize([id]);
        await sut.TryLoadAsync(id);
        Assert.IsInstanceOfType<MissingDocumentState>(StateOf(_state, UriOf("Gone.bas")));

        sut.Open(UriOf("Gone.bas"), "Sub Back()\r\nEnd Sub", version: 1);

        Assert.IsInstanceOfType<OpenedDocumentState>(StateOf(_state, UriOf("Gone.bas")));
        Assert.IsTrue(sut.TryGetDocument(UriOf("Gone.bas"), out var found));
        Assert.AreEqual("Sub Back()\r\nEnd Sub", found.Text);
    }

    // ---- didChange ----

    [TestMethod]
    public void TheWholeText_ReplacesTheText()
    {
        var sut = Sut();
        sut.Open(UriOf("Mod1.bas"), "old", 1);

        var outcome = sut.Change(UriOf("Mod1.bas"), 2, [Full("new")]);

        Assert.AreEqual(DocumentChangeOutcome.Applied, outcome);
        sut.TryGetDocument(UriOf("Mod1.bas"), out var found);
        Assert.AreEqual("new", found.Text);
        Assert.AreEqual(2, found.Version);
        Assert.IsTrue(found.IsDirty);
    }

    [TestMethod]
    public void IncrementalEdits_AreAppliedInOrder_EachToWhatTheOneBeforeLeft()
    {
        var sut = Sut();
        sut.Open(UriOf("Mod1.bas"), "Sub Foo()\r\nEnd Sub", 1);

        // Foo -> Bar, and then the Bar that is there now -> Baz.
        sut.Change(UriOf("Mod1.bas"), 2, [Edit(0, 4, 7, "Bar"), Edit(0, 4, 7, "Baz")]);

        sut.TryGetDocument(UriOf("Mod1.bas"), out var found);
        Assert.AreEqual("Sub Baz()\r\nEnd Sub", found.Text);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void AVersionThatIsNotLater_IsOutOfDate_AndLeavesTheDocumentAlone(int version)
    {
        var sut = Sut();
        sut.Open(UriOf("Mod1.bas"), "kept", 2);

        var outcome = sut.Change(UriOf("Mod1.bas"), version, [Full("stale")]);

        Assert.AreEqual(DocumentChangeOutcome.OutOfDate, outcome);
        sut.TryGetDocument(UriOf("Mod1.bas"), out var found);
        Assert.AreEqual("kept", found.Text);
        Assert.AreEqual(2, found.Version);
    }

    [TestMethod]
    public void AChangeToADocumentThatIsNotOpen_IsIgnored()
        => Assert.AreEqual(DocumentChangeOutcome.NotOpen, Sut().Change(UriOf("Nope.bas"), 2, [Full("x")]));

    [TestMethod]
    public void TheVersionsOfTheClient_AreKeptAsTheyAre_NotCounted()
    {
        var sut = Sut();
        sut.Open(UriOf("Mod1.bas"), "a", 10);

        sut.Change(UriOf("Mod1.bas"), 15, [Full("b")]);

        sut.TryGetDocument(UriOf("Mod1.bas"), out var found);
        Assert.AreEqual(15, found.Version);
    }

    // ---- didSave ----

    [TestMethod]
    public void WhenTheClientSaves_TheChangesAreNotUnsavedAnyMore_AndTheVersionIsTheSame()
    {
        var sut = Sut();
        sut.Open(UriOf("Mod1.bas"), "a", 1);
        sut.Change(UriOf("Mod1.bas"), 2, [Full("b")]);

        Assert.IsTrue(sut.Saved(UriOf("Mod1.bas"), text: null));

        sut.TryGetDocument(UriOf("Mod1.bas"), out var found);
        Assert.IsFalse(found.IsDirty);
        Assert.AreEqual(2, found.Version);
        Assert.AreEqual("b", found.Text);
    }

    [TestMethod]
    public void TheTextTheClientSaves_WhenItSaysWhatItIs_IsTheTextOfTheDocument()
    {
        var sut = Sut();
        sut.Open(UriOf("Mod1.bas"), "a", 1);

        sut.Saved(UriOf("Mod1.bas"), "saved text");

        sut.TryGetDocument(UriOf("Mod1.bas"), out var found);
        Assert.AreEqual("saved text", found.Text);
        Assert.IsFalse(found.IsDirty);
    }

    [TestMethod]
    public void ASaveOfADocumentThatIsNotThere_IsNotOne()
        => Assert.IsFalse(Sut().Saved(UriOf("Nope.bas"), null));

    // ---- didClose ----

    [TestMethod]
    public async Task ADocumentThatIsAFile_IsWhatIsOnDiskOnceTheClientClosesIt_AtALaterVersion()
    {
        var path = Path.Combine(Root, "Mod1.bas");
        _files.AddFile(path, new MockFileData("on disk"));
        var sut = Sut();
        sut.Initialize(Root);
        sut.Open(UriOf("Mod1.bas"), "unsaved edits", 1);
        sut.Change(UriOf("Mod1.bas"), 2, [Full("more unsaved edits")]);

        var closed = await sut.CloseAsync(UriOf("Mod1.bas"));

        Assert.IsNotNull(closed);
        Assert.AreEqual("on disk", closed.Text);
        Assert.AreEqual(3, closed.Version, "what was derived from the text of the client has to be derived again");
        Assert.IsFalse(closed.IsDirty);
        Assert.IsInstanceOfType<LoadedDocumentState>(StateOf(_state, UriOf("Mod1.bas")));
    }

    [TestMethod]
    public async Task ADocumentFromOutsideTheWorkspace_IsNotTrackedOnceTheClientClosesIt_ThoughItIsAFile()
    {
        // the workspace has no claim on it: it is addressed by its own path, and is the client's.
        var elsewhere = new Uri(Path.Combine(Path.GetTempPath(), "rdcore-elsewhere", "Program1.rdc"));
        _files.AddFile(elsewhere.LocalPath, new MockFileData("on disk"));
        var sut = Sut();
        sut.Open(elsewhere, "the client's text", 1);

        var closed = await sut.CloseAsync(elsewhere);

        Assert.IsNull(closed);
        Assert.IsFalse(sut.TryGetDocument(elsewhere, out _));
        Assert.IsFalse(_state.IsTracked(new TextDocumentIdentifier(elsewhere)));
    }

    [TestMethod]
    public async Task ADocumentThatIsNotAFile_IsNotTrackedOnceTheClientClosesIt()
    {
        var sut = Sut();
        sut.Open(UriOf("Untitled.bas"), "text", 1);

        var closed = await sut.CloseAsync(UriOf("Untitled.bas"));

        Assert.IsNull(closed);
        Assert.IsFalse(sut.TryGetDocument(UriOf("Untitled.bas"), out _));
        Assert.IsFalse(_state.IsTracked(new TextDocumentIdentifier(UriOf("Untitled.bas"))));
    }

    [TestMethod]
    public async Task ADocumentThatIsClosedTwice_IsClosedOnce()
    {
        var sut = Sut();
        sut.Open(UriOf("Untitled.bas"), "text", 1);
        await sut.CloseAsync(UriOf("Untitled.bas"));

        Assert.IsNull(await sut.CloseAsync(UriOf("Untitled.bas")));
    }
}
