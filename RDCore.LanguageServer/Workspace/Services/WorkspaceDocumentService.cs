using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.States;
using RDCore.SDK.Workspace;
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
    /// Every document whose text is code: the modules of the project, and what a client opens. The files the project lists as other files
    /// (<see cref="SetOtherFiles"/>) are documents the workspace holds, and none of its code: a resource file, a <c>README</c> or a UserControl is never parsed.
    /// </summary>
    IEnumerable<WorkspaceDocument> GetAllDocuments();

    /// <summary>
    /// Loads a file a project lists, as <see cref="TryLoadAsync(TextDocumentIdentifier)"/> does for the primary project's: addressed relative to that project's
    /// folder. A file another project listed before is the same document, and this project is one more it is a module of.
    /// </summary>
    /// <param name="id">The file.</param>
    /// <param name="projectRoot">The folder of the project that lists it.</param>
    Task<bool> TryLoadAsync(TextDocumentIdentifier id, string projectRoot);

    /// <summary>
    /// Says which documents are the files the project lists as other files (<c>OtherFiles</c> of the <c>.rdproj</c>), and no module: they are loaded and
    /// tracked as any document is, found by <see cref="TryGetDocument"/>, and left out of <see cref="GetAllDocuments"/>. Whoever applies a project says so,
    /// each time it does.
    /// </summary>
    /// <param name="ids">Every other file of the project: one said before and not now is a document of code again.</param>
    void SetOtherFiles(IEnumerable<TextDocumentIdentifier> ids);

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

    /// <summary>
    /// Makes a document one that is to be read from disk again: it is tracked as <c>Unloaded</c>, whatever state it was in. A document the client has open is the client's,
    /// and is left as it is.
    /// </summary>
    /// <param name="id">The document.</param>
    /// <returns><see langword="false"/> when the client has the document open.</returns>
    bool Reset(TextDocumentIdentifier id);

    /// <summary>
    /// Moves a document to another path, as the client renamed its file: the document keeps its text, its version, whether it is open, and the projects it is a module
    /// of - nothing is read from disk, so that the text a client has open and has not saved is not lost. A document the client has open at the new path is kept instead - its text, version and open state - and gains the projects of the old one; one that was only read from disk is replaced.
    /// </summary>
    /// <param name="id">The document at the path it had.</param>
    /// <param name="newId">The path it has now.</param>
    /// <returns><see langword="false"/> when there is no such document.</returns>
    bool Move(TextDocumentIdentifier id, TextDocumentIdentifier newId);

    /// <summary>
    /// Says a project no longer lists a document: it is a module of one project less. The document is taken out of the workspace when it was the last project's and the
    /// client does not have it open; one the client has open stays, a module of no project.
    /// </summary>
    /// <param name="id">The document.</param>
    /// <param name="projectRoot">The folder of the project that no longer lists it.</param>
    void Leave(TextDocumentIdentifier id, string projectRoot);
    void Edit(TextDocumentIdentifier id, string text);
    void Edit(TextDocumentIdentifier id, TextDocumentRange range, int rangeLength, string text);
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

    // the changes of a document that read it and write it back - the client's, the project's, a move to another path - are made one at a time, with the state they leave it
    // in: one never overwrites another with what it read before, and nothing sees a document that is at neither of two paths.
    private readonly object _sync = new();

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
        return [.. _documents.Where(document => !others.Contains(document.Key)).Select(document => document.Value)];
    }

    public void SetOtherFiles(IEnumerable<TextDocumentIdentifier> ids) => _otherFiles = new HashSet<TextDocumentIdentifier>(ids);

    public bool TryGetDocument(Uri documentUri, out WorkspaceDocument document)
    {
        document = _documents.Values.FirstOrDefault(candidate => candidate.Id.Uri.ToUri().Equals(documentUri))!;
        return document is not null;
    }

    // a version only goes up: a document read again from disk - changed outside the editor - is the next version when its text is other than it was, and what
    // was made of the version before (its parse) is not taken for what is made of this one.
    private int NextVersion(TextDocumentIdentifier id, string content)
        => _documents.TryGetValue(id, out var earlier)
            ? earlier.Version + (string.Equals(earlier.Text, content, StringComparison.Ordinal) ? 0 : 1)
            : 1;

    public Task<bool> TryLoadAsync(TextDocumentIdentifier id) => TryLoadAsync(id, WorkspaceRoot);

    public async Task<bool> TryLoadAsync(TextDocumentIdentifier id, string projectRoot)
    {
        var path = id.Uri.GetFileSystemPath();

        // a file another project listed is the same document: it has the text, the version and the state it has, and is a module of this project too.
        lock (_sync)
        {
            if (_documents.TryGetValue(id, out var listed) && !listed.Projects.Any(root => WorkspacePaths.Same(root, projectRoot)))
            {
                _documents[id] = listed with { Projects = [.. listed.Projects, projectRoot] };
                return true;
            }
        }

        // the file is read before the document is written, and the lock is for the step that writes it: what the document is then - whose it is, whether the client
        // opened it while the file was read - is looked at when it is written, not when the reading began.
        string? content = null;
        ReadOnlyMemory<byte> companion = default;
        Exception? failure = null;
        try
        {
            if (ioFile.Exists(path))
            {
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace("Loading workspace document from '{path}'...", path);
                }

                content = await ioFile.ReadAllTextAsync(path);
                companion = ReadCompanion(content, path);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        lock (_sync)
        {
            _documents.TryGetValue(id, out var shared);

            // a document the client has open is the client's: what is on disk is not its text, and it is not Unloaded to be loaded.
            if (IsOpen(id))
            {
                logger.LogInformation("📌 Workspace document is open in the client, and what is on disk is not read over it.");
                return shared is not null && failure is null && content is not null;
            }

            // the first project to load a document is the one it is addressed from; a project that lists it later does not move it.
            var owner = shared?.WorkspaceRoot ?? projectRoot;
            var projects = shared?.Projects ?? [projectRoot];
            var relativeUri = ioPath.GetRelativePath(owner, path);

            if (failure is not null)
            {
                var broken = new WorkspaceDocument(relativeUri, owner) { Projects = projects };
                _documents[broken.Id] = broken;

                logger.LogWarning(failure, "❌ Workspace document '{uri}' could not be loaded.", relativeUri);
                documentStateProvider.OnDocumentLoadError(broken.Id);
                return false;
            }

            if (content is not null)
            {
                var document = new WorkspaceDocument(relativeUri, owner, content) { Projects = projects, Companion = companion };
                document = document with { Version = NextVersion(document.Id, content) };
                _documents[document.Id] = document;

                documentStateProvider.OnDocumentLoaded(document.Id);
                logger.LogInformation("✅ Workspace document was loaded successfully.");

                return true;
            }

            var missing = new WorkspaceDocument(relativeUri, owner) { Projects = projects };
            missing = missing with { Version = NextVersion(missing.Id, string.Empty) };
            _documents[missing.Id] = missing;

            logger.LogWarning("⚠ Workspace document at '{uri}' is missing.", relativeUri);
            documentStateProvider.OnDocumentMissing(missing.Id);
            return false;
        }
    }

    public bool Reset(TextDocumentIdentifier id)
    {
        lock (_sync)
        {
            if (IsOpen(id))
            {
                return false;
            }

            documentStateProvider.Forget(id);
            documentStateProvider.Track(id);
            return true;
        }
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
        lock (_sync)
        {
            if (IsOpen(id))
            {
                logger.LogInformation("📌 Workspace document is open in the client, and stays.");
                return false;
            }

            documentStateProvider.Forget(id);
            return _documents.TryRemove(id, out _);
        }
    }

    public void Leave(TextDocumentIdentifier id, string projectRoot)
    {
        lock (_sync)
        {
            if (!_documents.TryGetValue(id, out var document))
            {
                return;
            }

            var remaining = document.Projects.Where(root => !WorkspacePaths.Same(root, projectRoot)).ToList();
            if (remaining.Count == 0 && !IsOpen(id))
            {
                documentStateProvider.Forget(id);
                _documents.TryRemove(id, out _);
                return;
            }

            // another project still lists it, or the client has it open: it is the document of the projects that are left, and of none at all when the client keeps it open.
            _documents[id] = document with { Projects = remaining };
        }
    }

    private bool IsOpen(TextDocumentIdentifier id) => documentStateProvider.IsTracked(id) && documentStateProvider.GetCurrentState(id) is OpenedDocumentState;

    public bool Move(TextDocumentIdentifier id, TextDocumentIdentifier newId)
    {
        lock (_sync)
        {
            if (!_documents.TryGetValue(id, out var document))
            {
                return false;
            }

            var wasOpen = IsOpen(id);

            // the document is addressed from the root of the project that loaded it first; a path that climbs out of it is the file it names.
            var root = document.WorkspaceRoot;
            var moved = new WorkspaceDocument(ioPath.GetRelativePath(root, newId.Uri.GetFileSystemPath()), root, document.Text, document.Version)
            {
                IsDirty = document.IsDirty,
                Companion = document.Companion,
                Projects = document.Projects,
            };

            // a client closes the old path and opens the new one before it says the file was renamed: a document the client has open at the new path is the client's - its text,
            // its version, and that it is open - and is a module of the projects the old one was. One that is not open was only read from disk, and the document that was moved
            // is the document.
            var keptTheOneThere = _documents.TryGetValue(moved.Id, out var there) && IsOpen(moved.Id);
            if (there is not null)
            {
                var projects = (IReadOnlyList<string>)[.. there.Projects.Union(document.Projects, WorkspacePaths.Comparer)];
                _documents[moved.Id] = keptTheOneThere
                    ? there with { Projects = projects, Companion = there.Companion.IsEmpty ? document.Companion : there.Companion }
                    : moved with { Projects = projects };
            }
            else
            {
                _documents[moved.Id] = moved;
            }

            // the document is at its new path before it is gone from the old one: there is no moment it is at neither.
            _documents.TryRemove(id, out _);
            documentStateProvider.Forget(id);

            if (!keptTheOneThere)
            {
                documentStateProvider.Forget(moved.Id);
                documentStateProvider.Track(moved.Id);
                documentStateProvider.OnDocumentLoaded(moved.Id);
                if (wasOpen)
                {
                    documentStateProvider.OnDocumentOpened(moved.Id);
                }
            }

            return true;
        }
    }

    public void Edit(TextDocumentIdentifier id, string text)
    {
        lock (_sync)
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
        }

        logger.LogInformation("✅ Workspace document content was updated successfully.");
    }

    public void Edit(TextDocumentIdentifier id, TextDocumentRange range, int rangeLength, string text)
    {
        lock (_sync)
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

                    // what was written is what is saved: a document that changed - or moved - while it was written is not.
                    lock (_sync)
                    {
                        if (_documents.TryGetValue(id, out var current) && string.Equals(current.Text, workspaceDocument.Text, StringComparison.Ordinal))
                        {
                            _documents[id] = current.AsSaved();
                        }
                    }

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
    // It is read before the document is written, and with no lock held: it is file I/O.
    private ReadOnlyMemory<byte> ReadCompanion(string text, string path)
    {
        try
        {
            if (FormDesigner.CompanionOf(text) is { } reference
                && ioPath.Combine(ioPath.GetDirectoryName(path) ?? string.Empty, reference.FileName) is var companionPath
                && ioFile.Exists(companionPath))
            {
                return ioFile.ReadAllBytes(companionPath);
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

        return default;
    }

    public WorkspaceDocument Open(Uri documentUri, string text, int version)
    {
        var id = new TextDocumentIdentifier(documentUri);
        var path = DocumentUri.From(documentUri).GetFileSystemPath();
        var companion = ReadCompanion(text, path);

        // the document is the client's from the moment it is open, with the projects it was a module of at that moment: a move to this path, or out of it, is made before it or after it.
        lock (_sync)
        {
            if (_documents.TryGetValue(id, out var existing) && documentStateProvider.IsTracked(id) && documentStateProvider.GetCurrentState(id) is OpenedDocumentState)
            {
                logger.LogWarning("⚠️ Document '{uri}' was opened when it already was (v{existing}); the text the client sent is the document.", documentUri, existing.Version);
            }

            // a document the client opens need not be in the workspace folder: its own path is what addresses it then - and so it is when no workspace could
            // be loaded, which leaves no root to be relative to.
            // a document that is a module of projects stays one when the client opens it, and is addressed from the project that loaded it.
            var owner = existing?.WorkspaceRoot ?? WorkspaceRoot;
            var relativePath = owner.Length == 0 ? path : ioPath.GetRelativePath(owner, path);
            var document = new WorkspaceDocument(relativePath.StartsWith("..", StringComparison.Ordinal) || ioPath.IsPathRooted(relativePath) ? path : relativePath, owner, text, version)
            {
                Projects = existing?.Projects ?? [owner],
                Companion = companion,
            };

            _documents[id] = document;
            documentStateProvider.OnDocumentOpenedByClient(id);

            logger.LogInformation("📂 Document '{uri}' was opened by the client at v{version}.", documentUri, version);
            return document;
        }
    }

    public DocumentChangeOutcome Change(Uri documentUri, int version, IEnumerable<TextDocumentContentChangeEvent> changes)
    {
        var id = new TextDocumentIdentifier(documentUri);

        // read and written back in one step, with the projects the document is a module of when it is written: a project that took or left it meanwhile keeps its say.
        lock (_sync)
        {
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
    }

    public bool Saved(Uri documentUri, string? text)
    {
        var id = new TextDocumentIdentifier(documentUri);
        lock (_sync)
        {
            if (!_documents.TryGetValue(id, out var document))
            {
                return false;
            }

            // the client says what it saved, or it does not: either way, what is saved is the text of the document. When it is not the text the server has, the
            // server was behind, and the text is one version later than what was derived from the one it had.
            _documents[id] = (text is null || text == document.Text ? document : document.WithText(text, document.Version + 1)).AsSaved();
            return true;
        }
    }

    public async Task<WorkspaceDocument?> CloseAsync(Uri documentUri)
    {
        var id = new TextDocumentIdentifier(documentUri);
        WorkspaceDocument document;
        lock (_sync)
        {
            if (!_documents.TryGetValue(id, out var found))
            {
                return null;
            }

            document = found;

            // the client does not have it open from here on: a document that is open again by the time the file is read is a reopen, and the text the client sent is not
            // replaced by what is on disk.
            if (IsOpen(id))
            {
                documentStateProvider.OnDocumentClosed(id);
            }
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
                var companion = ReadCompanion(content, path);

                // the document is what it is when this is written, not what it was before the file was read: it may have moved on, or a project may have taken it.
                lock (_sync)
                {
                    if (!_documents.TryGetValue(id, out var current))
                    {
                        return null;
                    }

                    if (IsOpen(id))
                    {
                        logger.LogInformation("📂 Document '{uri}' was opened again while the client's close was made; the text the client sent stays.", documentUri);
                        return current;
                    }

                    var reloaded = new WorkspaceDocument(current.RelativePath, current.WorkspaceRoot, content, current.Version + 1) { Projects = current.Projects, Companion = companion };
                    _documents[id] = reloaded;
                    if (!documentStateProvider.IsTracked(id) || documentStateProvider.GetCurrentState(id) is not LoadedDocumentState)
                    {
                        documentStateProvider.OnDocumentOpenedByClient(id);
                        documentStateProvider.OnDocumentClosed(id);
                    }

                    logger.LogInformation("📁 Document '{uri}' was closed by the client; what is on disk is the document again (v{version}).", documentUri, reloaded.Version);
                    return reloaded;
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "❌ Document '{uri}' could not be read from disk once the client closed it.", documentUri);
        }

        // not the workspace's, not a file, or not one that can be read: there is no document without the client's text - unless the client opened it again.
        lock (_sync)
        {
            if (IsOpen(id) && _documents.TryGetValue(id, out var reopened))
            {
                logger.LogInformation("📂 Document '{uri}' was opened again while the client's close was made; the text the client sent stays.", documentUri);
                return reopened;
            }

            _documents.TryRemove(id, out _);
            documentStateProvider.Forget(id);
        }

        logger.LogInformation("📁 Document '{uri}' was closed by the client and is no longer tracked.", documentUri);
        return null;
    }

}
