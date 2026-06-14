using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Server.Services.States;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Workspace.Services;

/// <summary>
/// Defines methods for managing the workspace, including loading and saving the project file, adding and removing files and folders, and managing library references.
/// </summary>
/// <remarks>
/// Synchronizes the in-memory representation of the workspace with the file system and the project file on disk.
/// </remarks>
internal interface IWorkspaceService
{
    /// <summary>
    /// Asynchronously loads the workspace from the specified root directory, including all modules and other files defined in the project file.
    /// </summary>
    Task LoadAsync(string workspaceRoot);
    /// <summary>
    /// Asynchronously serializes the project file to disk.
    /// </summary>
    Task SaveAsync();

    /// <summary>
    /// Adds an existing file to the workspace.
    /// </summary>
    Task AddExistingFileAsync(string sourcePath, string? destinationFolder = default, string? destinationName = default);
    /// <summary>
    /// Creates a new file and adds it to the workspace.
    /// </summary>
    Task AddNewFileAsync(string name, string? destinationFolder = default);
    /// <summary>
    /// Removes the specified file from the workspace, optionally also deleting it from disk.
    /// </summary>
    void RemoveFile(TextDocumentIdentifier id, bool actuallyDelete = false);

    /// <summary>
    /// Creates a new folder and adds it to the workspace.
    /// </summary>
    void AddFolder(string relativePath);
    /// <summary>
    /// Removes the specified folder from the workspace, optionally also deleting it from disk (deletes all files and subdirectories under it).
    /// </summary>
    void RemoveFolder(string relativePath, bool actuallyDelete = false);

    /* TODO refactor to accept an absolute path or a GUID and implement a service to get a RDCoreReference for it */

    /// <summary>
    /// Adds a library reference to the project.
    /// </summary>
    void AddReference(RDCoreReference reference);
    /// <summary>
    /// Removes a library reference from the project.
    /// </summary>
    void RemoveReference(RDCoreReference reference);
}

internal class WorkspaceService(Version serverVersion, IServerStateProvider serverStateProvider,
    ILogger<WorkspaceService> logger,
    System.IO.Abstractions.IPath ioPath,
    System.IO.Abstractions.IFile ioFile,
    System.IO.Abstractions.IDirectory ioDirectory,
    IProjectFileService projectFileService,
    IWorkspaceDocumentService documentService,
    IEnumerable<SupportedLanguage> supportedLanguages) : IWorkspaceService
{
    private RDCoreProject ProjectInfo => projectFileService.Project.ProjectInfo!;
    private HashSet<string> SourceExtensions { get; } = [.. supportedLanguages.SelectMany(language => language.FileTypes.Select(ext => ext[1..].ToLowerInvariant()))];

    public async Task SaveAsync()
    {
        if (serverStateProvider.State is not RunningServerState and not ShuttingDownServerState)
        {
            logger.LogWarning("This operation is only permitted in Running and ShuttingDown server states.");
            throw new InvalidServerStateException();
        }

        await projectFileService.SaveAsync();
    }

    public async Task AddExistingFileAsync(string sourcePath, string? destinationFolder = default, string? destinationName = default)
    {
        var filename = destinationName ?? ioPath.GetFileName(sourcePath);
        var absoluteDestinationPath = ioPath.Combine(projectFileService.Project.Uri, destinationFolder ?? string.Empty, filename);
        ioFile.Copy(sourcePath, absoluteDestinationPath, overwrite: true);

        var extension = ioPath.GetExtension(sourcePath).ToLowerInvariant();

        var document = new WorkspaceDocument(ioPath.GetRelativePath(projectFileService.Project.Uri, absoluteDestinationPath), projectFileService.Project.Uri);
        _ = await documentService.TryLoadAsync(document.RelativePath);

        if (SourceExtensions.Contains(extension))
        {
            projectFileService.AddSourceFile(document);
        }
        else
        {
            projectFileService.AddDocument(document);
        }
    }

    public async Task AddNewFileAsync(string name, string? destinationFolder = default)
    {
        var fullPath = ioPath.Combine(projectFileService.Project.Uri, destinationFolder ?? string.Empty, name);
        var extension = ioPath.GetExtension(name).ToLowerInvariant();
        ioFile.Create(fullPath);

        var document = new WorkspaceDocument(ioPath.GetRelativePath(projectFileService.Project.Uri, fullPath), projectFileService.Project.Uri);
        _ = await documentService.TryLoadAsync(document.RelativePath);

        if (SourceExtensions.Contains(extension))
        {
            projectFileService.AddSourceFile(document);
        }
        else
        {
            projectFileService.AddDocument(document);
        }
    }

    public void AddFolder(string relativePath)
    {
        var fullPath = ioPath.Combine(projectFileService.Project.Uri, relativePath);
        ioDirectory.CreateDirectory(fullPath);

        projectFileService.AddFolder(relativePath);
    }

    public void RemoveFile(TextDocumentIdentifier id, bool actuallyDelete = false)
    {
        var workspaceRoot = projectFileService.Project.Uri;
        var absolutePath = id.Uri.GetFileSystemPath();
        var relativeUri = ioPath.GetRelativePath(workspaceRoot, absolutePath);

        var file = projectFileService.Project.ProjectInfo.Modules.SingleOrDefault(e => e.RelativeUri == relativeUri)
            ?? projectFileService.Project.ProjectInfo.OtherFiles.SingleOrDefault(e => e.RelativeUri == relativeUri);

        if (file is not null)
        {
            if (file is RDCoreModule module)
            {
                projectFileService.RemoveSourceFile(module);

            }
            else
            {
                projectFileService.RemoveDocument(file);
            }

            documentService.Unload(id);

            if (actuallyDelete)
            {
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace("Deleting file '{path}'...", absolutePath);
                }

                ioFile.Delete(absolutePath);
                logger.LogInformation("File was deleted successfully.");
            }
        }
    }

    public void RemoveFolder(string relativePath, bool actuallyDelete = false)
    {
        var affectedFiles = projectFileService.Project.ProjectInfo.GetFolderFiles(relativePath).ToList();
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("This operation will remove {affectedFiles} file(s) from the project.", affectedFiles.Count);
        }

        foreach (var document in affectedFiles)
        {
            if (document is RDCoreModule module)
            {
                projectFileService.RemoveSourceFile(module);
            }
            else
            {
                projectFileService.RemoveDocument(document);
            }
        }

        var affectedFolders = projectFileService.Project.ProjectInfo.Folders.Where(f => f.StartsWith(relativePath)).ToList();
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("This operation will remove {affectedFolders} folder(s) from the project.", affectedFolders.Count);
        }

        foreach (var folder in affectedFolders)
        {
            projectFileService.RemoveFolder(folder);
        }

        if (actuallyDelete)
        {
            var fullPath = ioPath.Combine(projectFileService.Project.Uri, relativePath);
            if (ioDirectory.Exists(fullPath))
            {
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace("Recursively deleting directory '{path}'...", fullPath);
                }
                ioDirectory.Delete(fullPath, recursive: true);
                logger.LogInformation("✅ Directory was recursively deleted successfully.");
            }
            else
            {
                logger.LogWarning("⚠️ Directory '{path}' was not found; no files or folders were deleted.", fullPath);
            }
        }
        else
        {
            logger.LogTrace("Directory will not be deleted from disk.");
        }

        logger.LogInformation("✅ RemoveFolder completed. A folder was successfully removed from the workspace.");
    }

    public void AddReference(RDCoreReference reference)
    {
        projectFileService.AddReference(reference);
        // TODO load reference metadata and make it available to the rest of the server

        logger.LogInformation("✅ AddReference completed. A reference was successfully added to the workspace.");
    }

    public void RemoveReference(RDCoreReference reference)
    {
        projectFileService.RemoveReference(reference);
        // TODO unload reference metadata from language services, update diagnostics

        logger.LogInformation("✅ RemoveReference completed. A reference was successfully removed from the workspace.");
    }

    public async Task LoadAsync(string workspaceRoot)
    {
        if (serverStateProvider.State.Value != ServerStateValue.Initializing)
        {
            logger.LogWarning("Operation is invalid in the current state, workspace will not be loaded; an exception will be thrown.");
            throw new InvalidServerStateException();
        }

        await projectFileService.LoadAsync(workspaceRoot);
        var version = projectFileService.Project.Version;
        if (new Version(version) > serverVersion)
        {
            logger.LogWarning("This project was created with an ulterior version and will not be loaded; an exception will be thrown.");
            throw new NotSupportedException($"This project was created with a version ({version}) greater than the currently running version ({serverVersion.ToString(3)}).");
        }

        documentService.Initialize(workspaceRoot);
        await LoadWorkspaceFilesAsync();

        logger.LogInformation("✅ LoadAsync completed. Workspace was loaded successfully.");
    }

    private async Task LoadWorkspaceFilesAsync()
    {
        var workspaceRoot = projectFileService.Project.Uri;
        var files = ProjectInfo.Modules
            .Concat(ProjectInfo.OtherFiles)
            .Select(file => new TextDocumentIdentifier(ioPath.Combine(workspaceRoot, file.RelativeUri)))
            .ToList();

        await Task.WhenAll(files.Select(documentService.TryLoadAsync));
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("✅ LoadWorkspaceFiles completed ({files} tasks).", files.Count);
        }
    }
}