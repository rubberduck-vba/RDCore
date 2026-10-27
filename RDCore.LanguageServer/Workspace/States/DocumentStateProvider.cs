using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.LanguageServer.Workspace.States;

public interface IDocumentStateProvider
{
    void Initialize(IEnumerable<TextDocumentIdentifier> workspaceDocumentIds);
    DocumentState GetCurrentState(TextDocumentIdentifier id);
    /// <summary>
    /// Sets the document state to <c>Unloaded</c>.
    /// </summary>
    /// <remarks>
    /// The initial state for all workspace documents when a workspace is first opened. The file may or may not exist in the workspace folder, and the language server has not yet attempted to load it. A document in this state could ultimately transition to any other state depending on whether the file exists and can be loaded successfully, and whether it is opened in the editor.
    /// </remarks>
    void OnDocumentUnloaded(TextDocumentIdentifier id);
    /// <summary>
    /// Sets the document state to <c>Loaded</c> if the current state is <c>Unloaded</c> or <c>Opened</c>, otherwise throws an <see cref="InvalidDocumentStateException"/>.
    /// </summary>
    /// <remarks>
    /// The normal ready state for a workspace document. The file exists in the workspace folder and is correctly loaded, regardless of whether it is currently opened in the editor or not.
    /// </remarks>
    void OnDocumentLoaded(TextDocumentIdentifier id);
    /// <summary>
    /// Sets the document state to <c>Missing</c> if the current state is <c>Unloaded</c>, otherwise throws an <see cref="InvalidDocumentStateException"/>.
    /// </summary>
    /// <remarks>
    /// A workspace document could not be found in the workspace folder. The file may have been deleted, moved, or renamed outside of the editor, or there may be a mismatch between the workspace configuration and the actual files in the workspace folder.
    /// This state is terminal and can only transition back to <c>Unloaded</c> if the document is manually unloaded, which may be necessary to trigger a reload if the file is later restored to the workspace folder.
    /// </remarks>
    void OnDocumentMissing(TextDocumentIdentifier id);
    /// <summary>
    /// Sets the document state to <c>LoadError</c> if the current state is <c>Unloaded</c>, otherwise throws an <see cref="InvalidDocumentStateException"/>.
    /// </summary>
    /// <remarks>
    /// A workspace document could not be loaded, likely due to an I/O error or invalid file format. The file exists in the workspace folder, but the language server was unable to read and parse its contents.
    /// This state is terminal and can only transition back to <c>Unloaded</c> if the document is manually unloaded, which may be necessary to trigger a reload if the file is later restored to the workspace folder.
    /// </remarks>
    void OnDocumentLoadError(TextDocumentIdentifier id);
    /// <summary>
    /// Sets the document state to <c>Opened</c> if the current state is <c>Loaded</c>, otherwise throws an <see cref="InvalidDocumentStateException"/>.
    /// </summary>
    /// <remarks>
    /// A document tab was opened in the editor.
    /// </remarks>
    void OnDocumentOpened(TextDocumentIdentifier id);
    /// <summary>
    /// Sets the document state to <c>Loaded</c> if the current state is <c>Opened</c>, otherwise throws an <see cref="InvalidDocumentStateException"/>.
    /// </summary>
    /// <remarks>
    /// A document tab was closed in the editor, but the file still exists in the workspace and is correctly loaded.
    /// </remarks>
    void OnDocumentClosed(TextDocumentIdentifier id);
    /// <summary>
    /// Whether the document has a state at all: it is one the server knows of.
    /// </summary>
    bool IsTracked(TextDocumentIdentifier id);
    /// <summary>
    /// Sets the document state to <c>Opened</c>, whatever it was.
    /// </summary>
    /// <remarks>
    /// <strong>LSP 3.17</strong> <c>textDocument/didOpen</c>: from that moment the text the client sends is the document, and what is on disk - or that there is
    /// nothing, or that it could not be read - is no longer what the server goes by. A document that was missing, that could not be loaded, or that the server did not know
    /// of, is a document the client has open all the same.
    /// </remarks>
    void OnDocumentOpenedByClient(TextDocumentIdentifier id);
    /// <summary>
    /// Stops tracking the document: it has no state any more.
    /// </summary>
    void Forget(TextDocumentIdentifier id);
    /// <summary>
    /// Starts tracking a document the workspace has gained since it was initialized (<see cref="Initialize"/>), as <c>Unloaded</c>; a document already tracked
    /// keeps its state.
    /// </summary>
    void Track(TextDocumentIdentifier id);
}

public class DocumentStateProvider(ILogger<DocumentStateProvider> logger) : IDocumentStateProvider
{
    private readonly Dictionary<TextDocumentIdentifier, DocumentState> _state = [];

    // the workspace's documents are loaded in parallel (WorkspaceService.LoadAsync) while requests read their states: each transition is a read and a
    // write that no other may come between.
    private readonly Lock _gate = new();

    public DocumentState GetCurrentState(TextDocumentIdentifier id)
    {
        lock (_gate)
        {
            return _state[id];
        }
    }

    public void OnDocumentUnloaded(TextDocumentIdentifier id) => Transition(id, value => value is LoadedDocumentState, DocumentState.Unloaded, DocumentStateValue.Unloaded);
    public void OnDocumentLoaded(TextDocumentIdentifier id) => Transition(id, value => value is UnloadedDocumentState, DocumentState.Loaded, DocumentStateValue.Loaded);
    public void OnDocumentMissing(TextDocumentIdentifier id) => Transition(id, value => value is UnloadedDocumentState, DocumentState.Missing, DocumentStateValue.Missing);
    public void OnDocumentLoadError(TextDocumentIdentifier id) => Transition(id, value => value is UnloadedDocumentState, DocumentState.LoadError, DocumentStateValue.LoadError);
    public void OnDocumentOpened(TextDocumentIdentifier id) => Transition(id, value => value is LoadedDocumentState, DocumentState.Opened, DocumentStateValue.Opened);
    public void OnDocumentClosed(TextDocumentIdentifier id) => Transition(id, value => value is OpenedDocumentState, DocumentState.Loaded, DocumentStateValue.Loaded);

    public bool IsTracked(TextDocumentIdentifier id)
    {
        lock (_gate)
        {
            return _state.ContainsKey(id);
        }
    }

    public void OnDocumentOpenedByClient(TextDocumentIdentifier id)
    {
        lock (_gate)
        {
            _state[id] = DocumentState.Opened;
        }
    }

    public void Forget(TextDocumentIdentifier id)
    {
        lock (_gate)
        {
            _state.Remove(id);
        }
    }

    public void Track(TextDocumentIdentifier id)
    {
        lock (_gate)
        {
            _state.TryAdd(id, DocumentState.Unloaded);
        }
    }

    public void Initialize(IEnumerable<TextDocumentIdentifier> workspaceDocumentIds)
    {
        int count;
        lock (_gate)
        {
            _state.Clear();
            foreach (var id in workspaceDocumentIds)
            {
                _state[id] = DocumentState.Unloaded;
            }

            count = _state.Count;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("✅ Initialize completed; tracking {documents} unloaded workspace document(s).", count);
        }
    }

    // the document goes to the state when it is in one it may go there from; anything else is a transition the lifecycle does not have.
    private void Transition(TextDocumentIdentifier id, Func<DocumentState, bool> from, DocumentState to, DocumentStateValue toValue)
    {
        lock (_gate)
        {
            _state[id] = _state.TryGetValue(id, out var value) && from(value) ? to : throw new InvalidDocumentStateException(toValue);
        }
    }
}
