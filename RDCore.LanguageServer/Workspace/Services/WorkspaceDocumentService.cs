using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.States;
using System.Text;
using TextDocumentRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Workspace.Services;

internal interface IWorkspaceDocumentService
{
    /// <summary>
    /// The workspace root this service was initialized with, exactly as it arrived — empty before
    /// <see cref="Initialize"/>.
    /// </summary>
    /// <remarks>
    /// Anything that addresses a symbol has to derive it from the same root string every time. A
    /// second copy of "the workspace root" that differs only in the case of its drive letter yields
    /// symbol URIs that compare unequal, which means the same declaration defined twice and a name
    /// that then resolves to neither of them.
    /// </remarks>
    string WorkspaceRoot { get; }

    IEnumerable<WorkspaceDocument> GetAllDocuments();
    bool TryGetDocument(Uri documentUri, out WorkspaceDocument document);
    void Initialize(string workspaceRoot);
    Task<bool> TryLoadAsync(TextDocumentIdentifier id);
    Task<bool> TrySaveAsync(TextDocumentIdentifier id);
    bool Unload(TextDocumentIdentifier id);
    void Edit(TextDocumentIdentifier id, string text);
    void Edit(TextDocumentIdentifier id, TextDocumentRange range, int rangeLength, string text);
    void Rename(TextDocumentIdentifier id, string newName);
    void Create(string relativePath);

    /// <summary>
    /// <strong>LSP 3.17</strong> <c>textDocument/didOpen</c>: a client has opened a document, and from now on its text is the client's.
    /// </summary>
    /// <remarks>
    /// The document need not be in the workspace folder, nor exist on disk, nor have been loaded: the text the client sends is the document, whatever state it
    /// was in. A document that is already open is replaced by the one the client sends.
    /// </remarks>
    /// <param name="documentUri">The address of the document.</param>
    /// <param name="text">The text the client has.</param>
    /// <param name="version">The version the client gives it.</param>
    /// <returns>The document as it is now.</returns>
    WorkspaceDocument Open(Uri documentUri, string text, int version);

    /// <summary>
    /// <strong>LSP 3.17</strong> <c>textDocument/didChange</c>: a client has edited a document it has open.
    /// </summary>
    /// <remarks>
    /// The edits are applied in the order they come, each to the text the one before it left. A version that is not later than the document's is a change
    /// that is out of date, and is not applied: versions only go up, and applying one that does not would put the text of the server and the text of the client out
    /// of step for good.
    /// </remarks>
    /// <param name="documentUri">The address of the document.</param>
    /// <param name="version">The version the client gives the text once it is edited.</param>
    /// <param name="changes">The edits: each a range and its replacement, or the whole text.</param>
    DocumentChangeOutcome Change(Uri documentUri, int version, IEnumerable<TextDocumentContentChangeEvent> changes);

    /// <summary>
    /// <strong>LSP 3.17</strong> <c>textDocument/didSave</c>: a client has saved a document it has open.
    /// </summary>
    /// <param name="documentUri">The address of the document.</param>
    /// <param name="text">The text that was saved, when the client says what it was: it is the text of the document then.</param>
    /// <returns><see langword="false"/> when the server does not have the document open.</returns>
    bool Saved(Uri documentUri, string? text);

    /// <summary>
    /// <strong>LSP 3.17</strong> <c>textDocument/didClose</c>: a client has closed a document, and its text is no longer the client's.
    /// </summary>
    /// <remarks>
    /// What is on disk is the document again, when there is a file; a document that is not a file is not one any more.
    /// </remarks>
    /// <param name="documentUri">The address of the document.</param>
    /// <returns>The document as it is now, or <see langword="null"/> when there is none.</returns>
    Task<WorkspaceDocument?> CloseAsync(Uri documentUri);
}

/// <summary>
/// What became of a change a client sent.
/// </summary>
internal enum DocumentChangeOutcome
{
    /// <summary>The edits were applied.</summary>
    Applied,

    /// <summary>The server does not have the document open, so there is nothing to apply the edits to.</summary>
    NotOpen,

    /// <summary>The version is not later than the document's: the change is out of date.</summary>
    OutOfDate,
}

internal class WorkspaceDocumentService(IDocumentStateProvider documentStateProvider, ILogger<WorkspaceDocumentService> logger,
    System.IO.Abstractions.IPath ioPath,
    System.IO.Abstractions.IFile ioFile) : IWorkspaceDocumentService
{
    private readonly Dictionary<TextDocumentIdentifier, WorkspaceDocument> _documents = [];

    public string WorkspaceRoot { get; private set; } = string.Empty;

    public void Initialize(string workspaceRoot)
    {
        WorkspaceRoot = workspaceRoot;
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("WorkspaceDocumentService initialized with workspace root: {workspaceRoot}", workspaceRoot);
        }
        logger.LogInformation("✅ Initialize completed.");
    }

    public IEnumerable<WorkspaceDocument> GetAllDocuments() => [.. _documents.Values];

    public bool TryGetDocument(Uri documentUri, out WorkspaceDocument document)
    {
        document = _documents.Values.FirstOrDefault(candidate => candidate.Id.Uri.ToUri().Equals(documentUri))!;
        return document is not null;
    }

    public async Task<bool> TryLoadAsync(TextDocumentIdentifier id)
    {
        var path = id.Uri.GetFileSystemPath();
        var relativeUri = ioPath.GetRelativePath(WorkspaceRoot, path);

        try
        {
            if (ioFile.Exists(path))
            {
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace("Loading workspace document from '{path}'...", path);
                }

                var content = await ioFile.ReadAllTextAsync(path);
                var document = new WorkspaceDocument(relativeUri, WorkspaceRoot, content);
                _documents[document.Id] = document;

                documentStateProvider.OnDocumentLoaded(document.Id);
                logger.LogInformation("✅ Workspace document was loaded successfully.");

                return true;
            }
            else
            {
                var document = new WorkspaceDocument(relativeUri, WorkspaceRoot);
                _documents[document.Id] = document;

                logger.LogWarning("⚠ Workspace document at '{uri}' is missing.", relativeUri);
                documentStateProvider.OnDocumentMissing(document.Id);
            }
        }
        catch (Exception exception)
        {
            var document = new WorkspaceDocument(relativeUri, WorkspaceRoot);
            _documents[document.Id] = document;

            logger.LogWarning(exception, "❌ Workspace document '{uri}' could not be loaded.", relativeUri);
            documentStateProvider.OnDocumentLoadError(document.Id);
        }

        return false;
    }

    public void Create(string relativePath)
    {
        var id = new TextDocumentIdentifier(new Uri(ioPath.Combine(WorkspaceRoot, relativePath)));
        if (_documents.ContainsKey(id))
        {
            logger.LogWarning("⚠️ Workspace document '{uri}' already exists and cannot be created.", relativePath);
            throw new InvalidOperationException("Document already exists.");
        }

        var path = id.Uri.GetFileSystemPath();
        File.Create(path);

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("Created new file '{path}'.", path);
        }

        var document = new WorkspaceDocument(relativePath, WorkspaceRoot);
        _documents[id] = document;
        documentStateProvider.OnDocumentLoaded(document.Id);

        logger.LogInformation("Workspace document was created successfully.");
    }

    public bool Unload(TextDocumentIdentifier id)
    {
        if (_documents.TryGetValue(id, out var document))
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Unloading workspace document '{uri}'...", document.RelativePath);
            }

            if (document.IsDirty)
            {
                logger.LogWarning("⚠️ Workspace document has unsaved changes being discarded.");
            }

            if (_documents.Remove(id))
            {
                documentStateProvider.OnDocumentUnloaded(id);

                logger.LogInformation("✅ Workspace document was unloaded successfully.");
                return true;
            }
        }

        logger.LogDebug("🐛 Workspace document could not be unloaded.");
        return false;
    }

    public void Edit(TextDocumentIdentifier id, string text)
    {
        var currentState = documentStateProvider.GetCurrentState(id);
        if (currentState is LoadedDocumentState or OpenedDocumentState
            && _documents.TryGetValue(id, out var document) && document is WorkspaceDocument workspaceDocument)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Editing workspace document '{uri}'...", workspaceDocument.RelativePath);
            }

            _documents[id] = workspaceDocument.WithText(text);
        }
        else
        {
            logger.LogWarning("🐛 Workspace document could not be edited because it is not in a valid state ({state}).", currentState.Value);
            throw new InvalidDocumentStateException();
        }

        logger.LogInformation("✅ Workspace document content was updated successfully.");
    }

    public void Edit(TextDocumentIdentifier id, TextDocumentRange range, int rangeLength, string text)
    {
        var currentState = documentStateProvider.GetCurrentState(id);
        if (currentState is LoadedDocumentState or OpenedDocumentState
            && _documents.TryGetValue(id, out var document) && document is WorkspaceDocument workspaceDocument)
        {
            var newText = DocumentText.Apply(workspaceDocument.Text, range, text);
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Editing workspace document '{uri}' at {range}...", workspaceDocument.RelativePath, range);
            }

            _documents[id] = workspaceDocument.WithText(newText);
        }
        else
        {
            logger.LogWarning("🐛 Workspace document could not be edited because it is not in a valid state ({state}).", currentState.Value);
            throw new InvalidDocumentStateException();
        }

        logger.LogInformation("✅ Workspace document content was updated successfully.");
    }

    public async Task<bool> TrySaveAsync(TextDocumentIdentifier id)
    {
        var currentState = documentStateProvider.GetCurrentState(id);
        if (currentState is LoadedDocumentState or OpenedDocumentState
            && _documents.TryGetValue(id, out var document) && document is WorkspaceDocument workspaceDocument)
        {
            if (workspaceDocument.IsDirty)
            {
                try
                {
                    var path = ioPath.Combine(WorkspaceRoot, document.FileName);
                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        logger.LogTrace("Saving file: '{path}'...", path);
                    }

                    await ioFile.WriteAllTextAsync(path, document.Text);
                    _documents[id] = workspaceDocument.AsSaved();

                    logger.LogInformation("Workspace document was saved successfully.");
                    return true;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "❌ Workspace document could not be saved.");
                }
            }
            else
            {
                logger.LogWarning("⚠️ Workspace document has no changes to save.");
            }
        }
        else
        {
            logger.LogWarning("🐛 Workspace document could not be saved because it is not in a valid state ({state}).", currentState.Value);
            throw new InvalidDocumentStateException();
        }

        return false;
    }

    public WorkspaceDocument Open(Uri documentUri, string text, int version)
    {
        var id = new TextDocumentIdentifier(documentUri);
        if (_documents.TryGetValue(id, out var existing) && documentStateProvider.IsTracked(id) && documentStateProvider.GetCurrentState(id) is OpenedDocumentState)
        {
            logger.LogWarning("⚠️ Document '{uri}' was opened when it already was (v{existing}); the text the client sent is the document.", documentUri, existing.Version);
        }

        // a document the client opens need not be in the workspace folder: its own path is what addresses it then.
        var path = DocumentUri.From(documentUri).GetFileSystemPath();
        var relativePath = ioPath.GetRelativePath(WorkspaceRoot, path);
        var document = new WorkspaceDocument(relativePath.StartsWith("..", StringComparison.Ordinal) || ioPath.IsPathRooted(relativePath) ? path : relativePath, WorkspaceRoot, text, version);

        _documents[id] = document;
        documentStateProvider.OnDocumentOpenedByClient(id);

        logger.LogInformation("📂 Document '{uri}' was opened by the client at v{version}.", documentUri, version);
        return document;
    }

    public DocumentChangeOutcome Change(Uri documentUri, int version, IEnumerable<TextDocumentContentChangeEvent> changes)
    {
        var id = new TextDocumentIdentifier(documentUri);
        if (!_documents.TryGetValue(id, out var document)
            || !documentStateProvider.IsTracked(id) || documentStateProvider.GetCurrentState(id) is not OpenedDocumentState)
        {
            logger.LogWarning("🐛 A change to '{uri}' (v{version}) was ignored: the document is not open.", documentUri, version);
            return DocumentChangeOutcome.NotOpen;
        }

        if (version <= document.Version)
        {
            logger.LogWarning("🐛 A change to '{uri}' was ignored: v{version} is not later than v{current}.", documentUri, version, document.Version);
            return DocumentChangeOutcome.OutOfDate;
        }

        var text = document.Text;
        foreach (var change in changes)
        {
            // a change with no range is the whole text.
            text = change.Range is { } range ? DocumentText.Apply(text, range, change.Text) : change.Text;
        }

        _documents[id] = document.WithText(text, version);
        return DocumentChangeOutcome.Applied;
    }

    public bool Saved(Uri documentUri, string? text)
    {
        var id = new TextDocumentIdentifier(documentUri);
        if (!_documents.TryGetValue(id, out var document))
        {
            return false;
        }

        // the client says what it saved, or it does not: either way, what is saved is the text of the document. When it is not the text the server has, the
        // server was behind, and the text is one version later than what was derived from the one it had.
        _documents[id] = (text is null || text == document.Text ? document : document.WithText(text, document.Version + 1)).AsSaved();
        return true;
    }

    public async Task<WorkspaceDocument?> CloseAsync(Uri documentUri)
    {
        var id = new TextDocumentIdentifier(documentUri);
        if (!_documents.TryGetValue(id, out var document))
        {
            return null;
        }

        // a document the client opened from outside the workspace is addressed by its own path, and the workspace has no claim on it: it is the client's, and
        // is gone once the client closes it - whether or not there is a file. (A program a shell loads is one: the file is the user's, not the project's.)
        var path = DocumentUri.From(documentUri).GetFileSystemPath();
        try
        {
            if (!ioPath.IsPathRooted(document.RelativePath) && ioFile.Exists(path))
            {
                // what is on disk is the document again, and it may not be what the client had: a later version, so that whatever was derived from the text of
                // the client is derived again.
                var content = await ioFile.ReadAllTextAsync(path);
                var reloaded = new WorkspaceDocument(document.RelativePath, document.WorkspaceRoot, content, document.Version + 1);
                _documents[id] = reloaded;
                documentStateProvider.OnDocumentOpenedByClient(id);
                documentStateProvider.OnDocumentClosed(id);

                logger.LogInformation("📁 Document '{uri}' was closed by the client; what is on disk is the document again (v{version}).", documentUri, reloaded.Version);
                return reloaded;
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "❌ Document '{uri}' could not be read from disk once the client closed it.", documentUri);
        }

        // not the workspace's, not a file, or not one that can be read: there is no document without the client's text.
        _documents.Remove(id);
        documentStateProvider.Forget(id);
        logger.LogInformation("📁 Document '{uri}' was closed by the client and is no longer tracked.", documentUri);
        return null;
    }

    public void Rename(TextDocumentIdentifier id, string newName)
    {
        var currentState = documentStateProvider.GetCurrentState(id);

        if (currentState is LoadedDocumentState or OpenedDocumentState
            && _documents.TryGetValue(id, out var d) && d is WorkspaceDocument document)
        {
            var relativePath = ioPath.GetRelativePath(WorkspaceRoot, document.Id.Uri.GetFileSystemPath());
            var newRelativePath = ioPath.Combine(ioPath.GetDirectoryName(relativePath)!, newName);
            var newId = new TextDocumentIdentifier(new Uri(ioPath.Combine(WorkspaceRoot, newRelativePath)));

            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Renaming file '{oldPath}' to '{newPath}'...", relativePath, newRelativePath);
            }

            _documents.Remove(id);
            _documents[newId] = new WorkspaceDocument(newRelativePath, WorkspaceRoot, document.Text, document.Version) { IsDirty = document.IsDirty };

            documentStateProvider.OnDocumentUnloaded(id);
            documentStateProvider.OnDocumentLoaded(newId);
            if (currentState is OpenedDocumentState)
            {
                documentStateProvider.OnDocumentOpened(id);
            }

            logger.LogInformation("✅ Workspace file rename operation completed successfully.");
        }
        else
        {
            logger.LogWarning("🐛 Workspace document could not be renamed because it is not in a valid state ({state}).", currentState.Value);
            throw new InvalidDocumentStateException();
        }
    }
}
