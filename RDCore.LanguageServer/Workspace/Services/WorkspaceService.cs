using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Services.Workspace;
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
    /// Loads the workspace as <see cref="LoadAsync(string, IReadOnlyList{string})"/> does, and offers the user to regenerate a project file that can't be read
    /// (<see cref="ProjectFileRegeneration"/>): when they accept, the file is kept next to itself, and the project is made again from the source files of its
    /// folder, and written.
    /// </summary>
    /// <param name="workspaceRoot">The first folder, the primary project's.</param>
    /// <param name="folders">The folders the client gave, the first included or not.</param>
    /// <param name="ask">
    /// Asks the user to choose among the buttons, and answers the one they chose, or <see langword="null"/>; <see langword="null"/> when nobody is there to ask
    /// (<see cref="RDCore.SDK.Platform.Protocol.RDCoreInitializationOptions.Unattended"/>), and a project file that can't be read is not loaded.
    /// </param>
    /// <param name="token">A token that cancels the question.</param>
    Task LoadAsync(string workspaceRoot, IReadOnlyList<string> folders, Func<string, IReadOnlyList<string>, CancellationToken, Task<string?>>? ask, CancellationToken token = default);
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

internal class WorkspaceService(IServerStateProvider serverStateProvider,
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

    public Task LoadAsync(string workspaceRoot, IReadOnlyList<string> folders) => LoadAsync(workspaceRoot, folders, ask: null);

    public async Task LoadAsync(string workspaceRoot, IReadOnlyList<string> folders, Func<string, IReadOnlyList<string>, CancellationToken, Task<string?>>? ask, CancellationToken token = default)
    {
        if (serverStateProvider.State.Value != ServerStateValue.Initializing)
        {
            logger.LogWarning("Operation is invalid in the current state, workspace will not be loaded; an exception will be thrown.");
            throw new InvalidServerStateException();
        }

        // the root is the workspace's before its project file is read: a project file that can't be read loads no project, and what the client runs is still run
        // there, as in a folder with no modules.
        documentService.Initialize(workspaceRoot);
        await LoadProjectFileAsync(workspaceRoot, ask, token);
        LogSchemaOf(projectFileService.Project);

        // the root is a folder of the workspace whether or not the client lists it among its folders.
        string[] all = [workspaceRoot, .. folders];
        _projects.Initialize(workspaceRoot, folders, all.Where(HoldsAProject));
        await LoadOtherProjectsAsync();
        await LoadWorkspaceFilesAsync();

        logger.LogInformation("✅ LoadAsync completed. Workspace was loaded successfully.");
    }

    // a project file that can't be read is regenerated from the source files of its folder, when the user says to; the file is kept next to it first.
    private async Task LoadProjectFileAsync(string workspaceRoot, Func<string, IReadOnlyList<string>, CancellationToken, Task<string?>>? ask, CancellationToken token)
    {
        try
        {
            await projectFileService.LoadAsync(workspaceRoot);
            return;
        }
        catch (InvalidDataException exception) when (ask is not null)
        {
            var path = ioPath.Combine(workspaceRoot, ProjectFile.FileName);
            var backup = ProjectFileRegeneration.BackupPathOf(path, ioFile.Exists);
            if (!await ProjectFileRegeneration.AskAsync(path, backup, exception, TextOf(path), ask, token))
            {
                logger.LogWarning("⚠️ The project file can't be read, and was not regenerated: the workspace will not be loaded.");
                throw;
            }

            ioFile.Move(path, backup);
            logger.LogWarning("⚠️ The project file can't be read: it is kept as '{backup}', and regenerated from the source files of its folder.", backup);
            try
            {
                // the folder has no project file now: its project is the one its source files make, which is written.
                await projectFileService.LoadAsync(workspaceRoot);
                await projectFileService.SaveAsync();
            }
            catch (Exception regeneration)
            {
                // a project file that could not be written in its place is put back as it was: a folder left without one would be made up again at the next start,
                // without a word, and without what only the file said. The project its folder made is let go of, as for a project file the user would not have
                // regenerated: a change to it would otherwise be written over the file that was put back.
                projectFileService.Unload();
                try
                {
                    if (ioFile.Exists(path))
                    {
                        ioFile.Delete(path);
                    }

                    ioFile.Move(backup, path);
                    logger.LogError("❌ The project file could not be regenerated, and is back as it was: {reason}", regeneration.Message);
                }
                catch (Exception restore)
                {
                    logger.LogError("❌ The project file could not be regenerated ({reason}), nor put back: it is kept as '{backup}' ({restore}).",
                        regeneration.Message, backup, restore.Message);
                    // the user is the one to put it back: the load fails with where the file is, which the client is shown.
                    throw new ProjectFileNotPutBackException(path, backup, regeneration, restore);
                }

                throw;
            }
        }
    }

    // the text of a project file that couldn't be read as a project, for the question to say where it stops being valid in characters; none when it can't be read
    // at all, and the question then says it in the reader's bytes.
    private string? TextOf(string path)
    {
        try
        {
            return ioFile.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // a project file is loaded whatever format it says it is in: a format that is not this platform's is said in the log, and only there.
    private void LogSchemaOf(ProjectFile project)
    {
        switch (ProjectFile.SchemaOf(project.Version))
        {
            case ProjectFileSchemaMatch.Older:
                logger.LogInformation("📄 The project file is in the format {version}, older than this platform's {schema}: it is written in {schema} when it is next saved.",
                    project.Version, ProjectFile.SchemaVersion, ProjectFile.SchemaVersion);
                break;
            case ProjectFileSchemaMatch.NewerMinor:
                logger.LogWarning("⚠️ The project file is in the format {version}, newer than this platform's {schema}: what this platform doesn't know of it is kept as it is.",
                    project.Version, ProjectFile.SchemaVersion);
                break;
            case ProjectFileSchemaMatch.NewerMajor:
                logger.LogWarning("⚠️ The project file is in the format {version}, a major newer than this platform's {schema}: it was read as this platform reads its own, and may not say all it means to.",
                    project.Version, ProjectFile.SchemaVersion);
                break;
            case ProjectFileSchemaMatch.Unparseable:
                logger.LogWarning("⚠️ The project file says its format is '{version}', which is no format's version: it was read as this platform's {schema}.",
                    project.Version, ProjectFile.SchemaVersion);
                break;
        }
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
        var listed = new List<(string Root, TextDocumentIdentifier Id, bool IsOther, DocClassType? Super)>();
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

                listed.Add((project.Root, new TextDocumentIdentifier(path), isOther, (file as RDCoreModule)?.Super));
            }
        }

        var files = listed.Select(entry => entry.Id).Distinct().ToList();

        // seed every known document as Unloaded before loading: the document service transitions
        // state per file and each transition is only valid from a tracked Unloaded state.
        documentStateProvider.Initialize(files);

        // what a project lists as other files is loaded and tracked, and never parsed as code: a resource file, a README.
        documentService.SetOtherFiles(listed.Where(entry => entry.IsOther).Select(entry => entry.Id).Except(listed.Where(entry => !entry.IsOther).Select(entry => entry.Id)));

        // what the projects say their document modules are, before any is loaded: the first project that lists a file says it.
        foreach (var project in listed.GroupBy(entry => entry.Root))
        {
            documentService.SetDocumentModules(project.Key, project.Where(entry => !entry.IsOther && entry.Super is not null)
                .Select(entry => KeyValuePair.Create(entry.Id, entry.Super!.Value)));
        }

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