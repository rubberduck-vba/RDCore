using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.States;
using RDCore.SDK.Workspace.Forms;
using System.Collections.Concurrent;
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

    /// <summary>
    /// Every file whose text is the workspace's code: the modules of the project, and the files a client opens. The files the project lists as other files
    /// (<see cref="SetOtherFiles"/>) are documents the workspace holds, and none of its code: a resource file, a <c>README</c> or a UserControl is never parsed.
    /// A document the client holds that is no file (<see cref="WorkspaceDocument.IsFile"/>) is no module of the workspace either: it is found by
    /// <see cref="TryGetModule"/>, and is a project of its own (<see cref="WorkspaceDocument.ProjectRoot"/>).
    /// </summary>
    IEnumerable<WorkspaceDocument> GetAllDocuments();

    /// <summary>
    /// A document whose text is code, by the address the client gives it: a module of the workspace, or a document the client holds that is no file. Not an
    /// other file of the project.
    /// </summary>
    /// <param name="documentUri">The address of the document, however the client spells it (<see cref="TryGetDocument"/>).</param>
    /// <param name="document">The document.</param>
    bool TryGetModule(Uri documentUri, out WorkspaceDocument document);

    /// <summary>
    /// Says which documents are the files the project lists as other files (<c>OtherFiles</c> of the <c>.rdproj</c>), and no module: they are loaded and
    /// tracked as any document is, found by <see cref="TryGetDocument"/>, and left out of <see cref="GetAllDocuments"/>. Whoever applies a project says so,
    /// each time it does.
    /// </summary>
    /// <param name="ids">Every other file of the project: one said before and not now is a document of code again.</param>
    void SetOtherFiles(IEnumerable<TextDocumentIdentifier> ids);

    /// <summary>
    /// A document the server tracks, by the address the client gives it.
    /// </summary>
    /// <remarks>
    /// An address is the document's however it is spelled, as <strong>LSP 3.17</strong> §URI has a client spell it: <c>file:///c%3A/Work/A.bas</c> is
    /// <c>file:///C:/Work/A.bas</c>.
    /// </remarks>
    /// <param name="documentUri">The address of the document.</param>
    /// <param name="document">The document.</param>
    bool TryGetDocument(Uri documentUri, out WorkspaceDocument document);
    void Initialize(string workspaceRoot);
    Task<bool> TryLoadAsync(TextDocumentIdentifier id);
    Task<bool> TrySaveAsync(TextDocumentIdentifier id);
    bool Unload(TextDocumentIdentifier id);
    /// <summary>
    /// Takes a document out of the workspace, whatever was made of it - loaded, missing, or unreadable - and stops tracking it: the project file no longer
    /// lists it. A document the client has open is the client's, and stays.
    /// </summary>
    /// <returns><see langword="false"/> when the document is open, or was not in the workspace.</returns>
    bool Remove(TextDocumentIdentifier id);
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
    // the workspace's documents are loaded in parallel (WorkspaceService.LoadAsync), while requests the server answers in parallel look them up: a plain
    // dictionary written and read at once gave those requests an internal error (HexIDE's findings desk #24).
    private readonly ConcurrentDictionary<TextDocumentIdentifier, WorkspaceDocument> _documents = [];

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

    // the files the project lists that are no modules: replaced whole each time a project is applied, while requests answered in parallel read it.
    private volatile IReadOnlySet<TextDocumentIdentifier> _otherFiles = new HashSet<TextDocumentIdentifier>();

    public IEnumerable<WorkspaceDocument> GetAllDocuments()
    {
        var others = _otherFiles;
        return [.. _documents.Where(document => document.Value.IsFile && !others.Contains(document.Key)).Select(document => document.Value)];
    }

    public void SetOtherFiles(IEnumerable<TextDocumentIdentifier> ids) => _otherFiles = new HashSet<TextDocumentIdentifier>(ids);

    // a document is keyed by its identifier, whose address is the document's however it is spelled: an identifier compares its DocumentUri, which is the
    // client's address normalized, where a Uri compares the spelling.
    public bool TryGetDocument(Uri documentUri, out WorkspaceDocument document)
        => _documents.TryGetValue(new TextDocumentIdentifier(documentUri), out document!);

    public bool TryGetModule(Uri documentUri, out WorkspaceDocument document)
        => TryGetDocument(documentUri, out document) && !_otherFiles.Contains(document.Id);

    // a version only goes up: a document read again from disk - changed outside the editor - is the next version when its text is other than it was, and what
    // was made of the version before (its parse) is not taken for what is made of this one.
    private int NextVersion(TextDocumentIdentifier id, string content)
        => _documents.TryGetValue(id, out var earlier)
            ? earlier.Version + (string.Equals(earlier.Text, content, StringComparison.Ordinal) ? 0 : 1)
            : 1;

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
                var document = WithCompanion(new WorkspaceDocument(relativeUri, WorkspaceRoot, content), path);
                document = document with { Version = NextVersion(document.Id, content) };
                _documents[document.Id] = document;

                documentStateProvider.OnDocumentLoaded(document.Id);
                logger.LogInformation("✅ Workspace document was loaded successfully.");

                return true;
            }
            else
            {
                var document = new WorkspaceDocument(relativeUri, WorkspaceRoot);
                document = document with { Version = NextVersion(document.Id, string.Empty) };
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

            if (_documents.TryRemove(id, out _))
            {
                documentStateProvider.OnDocumentUnloaded(id);

                logger.LogInformation("✅ Workspace document was unloaded successfully.");
                return true;
            }
        }

        logger.LogDebug("🐛 Workspace document could not be unloaded.");
        return false;
    }

    public bool Remove(TextDocumentIdentifier id)
    {
        if (documentStateProvider.IsTracked(id) && documentStateProvider.GetCurrentState(id) is OpenedDocumentState)
        {
            logger.LogInformation("📌 Workspace document is open in the client, and stays.");
            return false;
        }

        documentStateProvider.Forget(id);
        return _documents.TryRemove(id, out _);
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

    // a form module keeps its controls in a companion file next to it (a UserForm's .frx, which its text names), read with the text: it is the document's
    // too, and no client has it to send. A companion that cannot be read leaves the form without controls, and the parse of its designer says so.
    private WorkspaceDocument WithCompanion(WorkspaceDocument document, string path)
    {
        try
        {
            if (FormDesigner.CompanionOf(document.Text) is { } reference
                && ioPath.Combine(ioPath.GetDirectoryName(path) ?? string.Empty, reference.FileName) is var companionPath
                && ioFile.Exists(companionPath))
            {
                return document with { Companion = ioFile.ReadAllBytes(companionPath) };
            }
        }
        catch (FormDesignerFormatException exception)
        {
            logger.LogWarning("⚠️ The designer block of '{path}' cannot be read: {reason}", path, exception.Message);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "⚠️ The companion file of '{path}' could not be read.", path);
        }

        return document;
    }

    public WorkspaceDocument Open(Uri documentUri, string text, int version)
    {
        var id = new TextDocumentIdentifier(documentUri);
        if (_documents.TryGetValue(id, out var existing) && documentStateProvider.IsTracked(id) && documentStateProvider.GetCurrentState(id) is OpenedDocumentState)
        {
            logger.LogWarning("⚠️ Document '{uri}' was opened when it already was (v{existing}); the text the client sent is the document.", documentUri, existing.Version);
        }

        // a document the client opens need not be in the workspace folder: its own path is what addresses it then - and so it is when no workspace could
        // be loaded, which leaves no root to be relative to. Nor need it be a file: one that is not is addressed by the URI the client gives it.
        var document = documentUri.IsFile ? FileOpened(documentUri, text, version) : new WorkspaceDocument(documentUri, WorkspaceRoot, text, version);

        _documents[id] = document;
        documentStateProvider.OnDocumentOpenedByClient(id);

        logger.LogInformation("📂 Document '{uri}' was opened by the client at v{version}.", documentUri, version);
        return document;
    }

    private WorkspaceDocument FileOpened(Uri documentUri, string text, int version)
    {
        var path = DocumentUri.From(documentUri).GetFileSystemPath();
        var relativePath = WorkspaceRoot.Length == 0 ? path : ioPath.GetRelativePath(WorkspaceRoot, path);
        return WithCompanion(
            new WorkspaceDocument(relativePath.StartsWith("..", StringComparison.Ordinal) || ioPath.IsPathRooted(relativePath) ? path : relativePath, WorkspaceRoot, text, version),
            path);
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
            if (document.IsFile && !ioPath.IsPathRooted(document.RelativePath) && ioFile.Exists(path))
            {
                // what is on disk is the document again, and it may not be what the client had: a later version, so that whatever was derived from the text of
                // the client is derived again.
                var content = await ioFile.ReadAllTextAsync(path);
                var reloaded = WithCompanion(new WorkspaceDocument(document.RelativePath, document.WorkspaceRoot, content, document.Version + 1), path);
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
        _documents.TryRemove(id, out _);
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

            _documents.TryRemove(id, out _);
            _documents[newId] = new WorkspaceDocument(newRelativePath, WorkspaceRoot, document.Text, document.Version) { IsDirty = document.IsDirty, Companion = document.Companion };

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
