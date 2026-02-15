using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Workspace.States;
using System.Text;
using TextDocumentRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Workspace.Services;

internal interface IWorkspaceDocumentService
{
    IEnumerable<WorkspaceDocument> GetAllDocuments();
    void Initialize(string workspaceRoot);
    Task<bool> TryLoadAsync(string relativePath);
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
    }

    public IEnumerable<WorkspaceDocument> GetAllDocuments() => [.. _documents.Values];

    public async Task<bool> TryLoadAsync(string relativePath)
    {
        try
        {
            if (ioFile.Exists(relativePath))
            {
                var content = await ioFile.ReadAllTextAsync(relativePath);
                var document = new WorkspaceDocument(relativePath, _workspaceRoot, content);
                documentStateProvider.OnDocumentLoaded(document.Id);

                logger.LogInformation("Workspace document at '{relativePath}' was loaded successfully.", relativePath);
                return true;
            }
            else
            {
                var document = new WorkspaceDocument(relativePath, _workspaceRoot);
                _documents[document.Id] = document;

                logger.LogWarning("Workspace document at '{relativePath}' is missing.", relativePath);
                documentStateProvider.OnDocumentMissing(document.Id);
            }
        }
        catch (Exception exception)
        {
            var document = new WorkspaceDocument(relativePath, _workspaceRoot);
            _documents[document.Id] = document;

            logger.LogWarning(exception, "Workspace document at '{relativePath}' could not be loaded.", relativePath);
            documentStateProvider.OnDocumentLoadError(document.Id);
        }

        return false;
    }

    public void Create(string relativePath)
    {
        var id = new TextDocumentIdentifier(new Uri(ioPath.Combine(_workspaceRoot, relativePath)));
        if (_documents.ContainsKey(id))
        {
            logger.LogWarning("Workspace document at '{relativePath}' already exists and cannot be created.", relativePath);
            throw new InvalidOperationException("Document already exists.");
        }

        File.Create(id.Uri.GetFileSystemPath());
        var document = new WorkspaceDocument(relativePath, _workspaceRoot);
        _documents[id] = document;
        documentStateProvider.OnDocumentLoaded(document.Id);

        logger.LogInformation("Workspace document at '{relativePath}' was created successfully.", relativePath);
    }

    public bool Unload(TextDocumentIdentifier id)
    {
        if (_documents.TryGetValue(id, out var document))
        {
            if (document.IsDirty)
            {
                logger.LogWarning("Workspace document id '{id}' has unsaved changes being discarded.", id);
            }

            if (_documents.Remove(id))
            {
                documentStateProvider.OnDocumentUnloaded(id);

                logger.LogInformation("Workspace document id '{id}' was unloaded successfully.", id);
                return true;
            }
        }

        logger.LogWarning("Workspace document id '{id}' could not be unloaded.", id);
        return false;
    }

    public void Edit(TextDocumentIdentifier id, string text)
    {
        var currentState = documentStateProvider.GetCurrentState(id);
        if (currentState is LoadedDocumentState or OpenedDocumentState
            && _documents.TryGetValue(id, out var document) && document is WorkspaceDocument workspaceDocument)
        {
            _documents[id] = workspaceDocument.WithText(text);
        }
        else
        {
            logger.LogWarning("Workspace document id '{id}' could not be edited because it is not in a valid state ({state}).", id, currentState.Value);
            throw new InvalidDocumentStateException();
        }
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
            var textLines = text.Split(Environment.NewLine);

            var textBefore = replacedLines.First()[..range.Start.Character];
            var textAfter = replacedLines.Last()[range.End.Character..];

            var builder = new StringBuilder(workspaceDocument.Text.Length + text.Length);
            builder.Append(textBefore);
            builder.Append(text);
            builder.Append(textAfter);
            var replacedLinesText = builder.ToString();

            var newText = string.Join(Environment.NewLine, linesBefore.Append(replacedLinesText).Concat(linesAfter));
            _documents[id] = workspaceDocument.WithText(newText);
        }
        else
        {
            logger.LogWarning("Workspace document id '{id}' could not be edited because it is not in a valid state ({state}).", id, currentState.Value);
            throw new InvalidDocumentStateException();
        }
    }

    public async Task<bool> TrySaveAsync(TextDocumentIdentifier id)
    {
        if (documentStateProvider.GetCurrentState(id) is LoadedDocumentState or OpenedDocumentState
            && _documents.TryGetValue(id, out var document) && document is WorkspaceDocument workspaceDocument)
        {
            if (!workspaceDocument.IsDirty)
            {
                logger.LogWarning("Workspace document id '{id}' has no changes to save.", id);
            }

            try
            {
                var path = ioPath.Combine(_workspaceRoot, document.FileName);
                await ioFile.WriteAllTextAsync(path, document.Text);

                _documents[id] = workspaceDocument.AsInitialVersion();

                logger.LogInformation("Workspace document id '{id}' was saved successfully.", id);
                return true;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Workspace document id '{id}' could not be saved.", id);
            }
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

            _documents.Remove(id);
            _documents[newId] = new WorkspaceDocument(newRelativePath, document.Text, document.Version);

            documentStateProvider.OnDocumentUnloaded(id);
            documentStateProvider.OnDocumentLoaded(newId);
            if (currentState is OpenedDocumentState)
            {
                documentStateProvider.OnDocumentOpened(id);
            }

            logger.LogInformation("Workspace document id '{id}' was renamed to '{newName}' successfully.", id, newName);
        }
        else
        {
            logger.LogWarning("Workspace document id '{id}' could not be renamed because it is not in a valid state ({state}).", id, currentState.Value);
            throw new InvalidDocumentStateException();
        }
    }
}
