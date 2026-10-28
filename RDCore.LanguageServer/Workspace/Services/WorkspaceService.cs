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
    /// Loads the workspace a client opened with several folders: the project of the first, which is the one the server runs, and the project of each of the
    /// others that is a project folder (<see cref="RDCore.SDK.Workspace.WorkspaceFolderRoles"/>). A file a project lists that no folder holds is refused
    /// (<see cref="IWorkspaceProjects.Refused"/>).
    /// </summary>
    /// <param name="workspaceRoot">The first folder, the primary project's.</param>
    /// <param name="folders">The folders the client gave, the first included or not.</param>
    Task LoadAsync(string workspaceRoot, IReadOnlyList<string> folders);
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
    States.IDocumentStateProvider documentStateProvider,
    Func<SupportedLanguage> language,
    IWorkspaceProjects projects,
    RDCore.SDK.Workspace.IProjectFileLoader projectFileLoader) : IWorkspaceService
{
    private readonly IWorkspaceProjects _projects = projects;
    private RDCoreProject ProjectInfo => projectFileService.Project.ProjectInfo!;

    // the files of the language the workspace is written in, which the client says when it initializes: this is not asked before then.
    private HashSet<string>? _sourceExtensions;
    private HashSet<string> SourceExtensions
        => _sourceExtensions ??= [.. language().FileTypes.Select(ext => ext[1..].ToLowerInvariant())];

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

    public Task LoadAsync(string workspaceRoot) => LoadAsync(workspaceRoot, [workspaceRoot]);

    public async Task LoadAsync(string workspaceRoot, IReadOnlyList<string> folders)
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
        // the root is a folder of the workspace whether or not the client lists it among its folders.
        string[] all = [workspaceRoot, .. folders];
        _projects.Initialize(workspaceRoot, folders, all.Where(HoldsAProject));
        await LoadOtherProjectsAsync();
        await LoadWorkspaceFilesAsync();

        logger.LogInformation("✅ LoadAsync completed. Workspace was loaded successfully.");
    }

    private bool HoldsAProject(string folder) => ioFile.Exists(ioPath.Combine(folder, ProjectFile.FileName));

    // the other folders of the workspace that are project folders, in the order the client gave them: whether a folder is one depends on what the projects before it list.
    private async Task LoadOtherProjectsAsync()
    {
        var primary = _projects.Projects[0].Root;
        foreach (var root in _projects.Folders.Roots.Where(root => !WorkspacePaths.Same(primary, root)))
        {
            if (_projects.Roles().First(entry => WorkspacePaths.Same(entry.Root, root)).Role != WorkspaceFolderRole.Project)
            {
                logger.LogInformation("📁 '{folder}' holds sources a project lists, and is no project.", root);
                continue;
            }

            var project = await projectFileLoader.LoadOrSynthesizeAsync(root, language());
            _projects.Add(new WorkspaceProject(root, project));
            logger.LogInformation("📁 Project '{name}' was loaded from '{folder}'.", project.ProjectInfo.Name, root);
        }
    }

    private async Task LoadWorkspaceFilesAsync()
    {
        // each project's files: a path that climbs out of its folder (..\Shared\Util.bas) or is absolute is one only if a folder of the workspace holds it.
        var listed = new List<(string Root, TextDocumentIdentifier Id, bool IsOther)>();
        foreach (var project in _projects.Projects)
        {
            var info = project.Project.ProjectInfo;
            foreach (var (file, isOther) in info.Modules.Select(module => ((RDCoreFile)module, false))
                .Concat(info.OtherFiles.Select(other => (other, !info.Modules.Any(module => string.Equals(module.RelativeUri, other.RelativeUri, WorkspacePaths.Comparison))))))
            {
                var path = ioPath.Combine(project.Root, file.RelativeUri);
                if (ioPath.IsPathRooted(file.RelativeUri) || WorkspacePaths.Climbs(file.RelativeUri))
                {
                    path = ioPath.GetFullPath(path);
                    if (!_projects.Folders.Contains(path))
                    {
                        _projects.Refuse(project.Root, file.RelativeUri);
                        logger.LogWarning("⚠️ '{file}' is listed by project '{project}' and no folder of the workspace holds it: it is refused.", file.RelativeUri, project.Root);
                        continue;
                    }
                }

                listed.Add((project.Root, new TextDocumentIdentifier(path), isOther));
            }
        }

        var files = listed.Select(entry => entry.Id).Distinct().ToList();

        // seed every known document as Unloaded before loading: the document service transitions
        // state per file and each transition is only valid from a tracked Unloaded state.
        documentStateProvider.Initialize(files);

        // what a project lists as other files is loaded and tracked, and never parsed as code: a resource file, a README.
        documentService.SetOtherFiles(listed.Where(entry => entry.IsOther).Select(entry => entry.Id).Except(listed.Where(entry => !entry.IsOther).Select(entry => entry.Id)));

        // the projects load their files one after the other, so that the project that lists a file first is the one the document belongs to; the files of one
        // project load together.
        foreach (var project in listed.GroupBy(entry => entry.Root))
        {
            await Task.WhenAll(project.Select(entry => documentService.TryLoadAsync(entry.Id, project.Key)));
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("✅ LoadWorkspaceFiles completed ({files} tasks).", files.Count);
        }
    }
}