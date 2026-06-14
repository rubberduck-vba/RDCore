using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Workspace;
using RDCore.LanguageServer.Workspace.States;
using System.Text;
using TextDocumentRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.LanguageServer.Workspace.Services;

internal interface IWorkspaceDocumentService
{
    IEnumerable<WorkspaceDocument> GetAllDocuments();
    void Initialize(string workspaceRoot);
    Task<bool> TryLoadAsync(TextDocumentIdentifier id);
    Task<bool> TrySaveAsync(TextDocumentIdentifier id);
    bool Unload(TextDocumentIdentifier id);
    void Edit(TextDocumentIdentifier id, string text);
    void Edit(TextDocumentIdentifier id, TextDocumentRange range, int rangeLength, string text);
    void Rename(TextDocumentIdentifier id, string newName);
    void Create(string relativePath);
}

internal class WorkspaceDocumentService(IDocumentStateProvider documentStateProvider, ILogger<WorkspaceDocumentService> logger,
    System.IO.Abstractions.IPath ioPath,
    System.IO.Abstractions.IFile ioFile) : IWorkspaceDocumentService
{
    private readonly Dictionary<TextDocumentIdentifier, WorkspaceDocument> _documents = [];

    private string _workspaceRoot = string.Empty;
    public void Initialize(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("WorkspaceDocumentService initialized with workspace root: {workspaceRoot}", workspaceRoot);
        }
        logger.LogInformation("✅ Initialize completed.");
    }

    public IEnumerable<WorkspaceDocument> GetAllDocuments() => [.. _documents.Values];

    public async Task<bool> TryLoadAsync(TextDocumentIdentifier id)
    {
        var path = id.Uri.GetFileSystemPath();
        var relativeUri = ioPath.GetRelativePath(_workspaceRoot, path);

        try
        {
            if (ioFile.Exists(path))
            {
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace("Loading workspace document from '{path}'...", path);
                }

                var content = await ioFile.ReadAllTextAsync(path);
                var document = new WorkspaceDocument(relativeUri, _workspaceRoot, content);

                documentStateProvider.OnDocumentLoaded(document.Id);
                logger.LogInformation("✅ Workspace document was loaded successfully.");

                return true;
            }
            else
            {
                var document = new WorkspaceDocument(relativeUri, _workspaceRoot);
                _documents[document.Id] = document;

                logger.LogWarning("⚠ Workspace document at '{uri}' is missing.", relativeUri);
                documentStateProvider.OnDocumentMissing(document.Id);
            }
        }
        catch (Exception exception)
        {
            var document = new WorkspaceDocument(relativeUri, _workspaceRoot);
            _documents[document.Id] = document;

            logger.LogWarning(exception, "❌ Workspace document '{uri}' could not be loaded.", relativeUri);
            documentStateProvider.OnDocumentLoadError(document.Id);
        }

        return false;
    }

    public void Create(string relativePath)
    {
        var id = new TextDocumentIdentifier(new Uri(ioPath.Combine(_workspaceRoot, relativePath)));
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

        var document = new WorkspaceDocument(relativePath, _workspaceRoot);
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
            var lines = workspaceDocument.Text.Split(Environment.NewLine);
            var linesBefore = range.Start.Line > 0 ? lines[..range.Start.Line] : [];
            var linesAfter = lines[(lines.Length - range.End.Line)..];

            var replacedLines = lines[range.Start.Line..range.End.Line];
            //var textLines = text.Split(Environment.NewLine);

            var textBefore = replacedLines.First()[..range.Start.Character];
            var textAfter = replacedLines.Last()[range.End.Character..];

            var builder = new StringBuilder(workspaceDocument.Text.Length + text.Length);
            builder.Append(textBefore);
            builder.Append(text);
            builder.Append(textAfter);
            var replacedLinesText = builder.ToString();

            var newText = string.Join(Environment.NewLine, linesBefore.Append(replacedLinesText).Concat(linesAfter));
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
                    var path = ioPath.Combine(_workspaceRoot, document.FileName);
                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        logger.LogTrace("Saving file: '{path}'...", path);
                    }

                    await ioFile.WriteAllTextAsync(path, document.Text);
                    _documents[id] = workspaceDocument.AsInitialVersion();

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

    public void Rename(TextDocumentIdentifier id, string newName)
    {
        var currentState = documentStateProvider.GetCurrentState(id);

        if (currentState is LoadedDocumentState or OpenedDocumentState
            && _documents.TryGetValue(id, out var d) && d is WorkspaceDocument document)
        {
            var relativePath = ioPath.GetRelativePath(_workspaceRoot, document.Id.Uri.GetFileSystemPath());
            var newRelativePath = ioPath.Combine(ioPath.GetDirectoryName(relativePath)!, newName);
            var newId = new TextDocumentIdentifier(new Uri(ioPath.Combine(_workspaceRoot, newRelativePath)));

            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogTrace("Renaming file '{oldPath}' to '{newPath}'...", relativePath, newRelativePath);
            }

            _documents.Remove(id);
            _documents[newId] = new WorkspaceDocument(newRelativePath, document.Text, document.Version);

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
