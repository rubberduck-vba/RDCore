using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
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
    /// <param name="takeLanguage">
    /// Takes the language the primary project's file says the workspace is written in (<see cref="SupportedLanguages.OfProject"/>), once the file is read and
    /// before anything is loaded in that language; <see langword="null"/> when the client named the language, which wins.
    /// </param>
    Task LoadAsync(string workspaceRoot, IReadOnlyList<string> folders, Func<string, IReadOnlyList<string>, CancellationToken, Task<string?>>? ask, CancellationToken token = default,
        Action<SupportedLanguage>? takeLanguage = null);

    /// <summary>
    /// Makes the workspace what its folders are once the client added some or removed some (<strong>LSP 3.17</strong> <c>workspace/didChangeWorkspaceFolders</c>),
    /// as <see cref="LoadAsync(string, IReadOnlyList{string})"/> makes it of the folders the client gave when it initialized: an added folder that is a project
    /// folder (<see cref="RDCore.SDK.Workspace.WorkspaceFolderRoles"/>) is a project, a file a project lists is loaded once a folder holds it, and refused once
    /// none does (<see cref="IWorkspaceProjects.Refused"/>). The project of a folder that was removed is dropped, with its documents no project that is left
    /// lists; the primary project is never dropped, and its folder stays one of the workspace.
    /// </summary>
    /// <remarks>
    /// Only the documents the projects gain are loaded, and only those they no longer have are let go of: the others are as they were. What is made of them
    /// (parsed, defined in the environment host) is the caller's to do (<see cref="IWorkspaceReloadService.BeginChangeFoldersAsync"/>).
    /// </remarks>
    /// <param name="added">The folders the client added.</param>
    /// <param name="removed">The folders the client removed.</param>
    /// <param name="token">A token that cancels the change before a project or a document is loaded.</param>
    Task<WorkspaceFoldersChange> ChangeFoldersAsync(IReadOnlyList<string> added, IReadOnlyList<string> removed, CancellationToken token = default);

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

/// <summary>
/// What a change of the workspace's folders made of the documents of its projects (<see cref="IWorkspaceService.ChangeFoldersAsync"/>).
/// </summary>
/// <param name="Loaded">The documents of code the projects gained, which are to be parsed.</param>
/// <param name="LeftThePrimaryProject">The modules the primary project no longer has, with the name of their document: the environment host forgets them.</param>
/// <param name="Added">The files the projects have now that they did not.</param>
/// <param name="Removed">The files they no longer have.</param>
internal sealed record class WorkspaceFoldersChange(
    IReadOnlyList<TextDocumentIdentifier> Loaded,
    IReadOnlyList<(TextDocumentIdentifier Id, string Name)> LeftThePrimaryProject,
    int Added,
    int Removed)
{
    /// <summary>
    /// The folders are what they were, or what changed of them changed no file of a project.
    /// </summary>
    public static WorkspaceFoldersChange None { get; } = new([], [], 0, 0);

    /// <summary>
    /// Whether no project gained a file, nor lost one.
    /// </summary>
    public bool IsUnchanged => Added == 0 && Removed == 0;
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

    public async Task LoadAsync(string workspaceRoot, IReadOnlyList<string> folders, Func<string, IReadOnlyList<string>, CancellationToken, Task<string?>>? ask, CancellationToken token = default,
        Action<SupportedLanguage>? takeLanguage = null)
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

        // the source files and the libraries are the language's: what the project file says of it is taken before either is loaded.
        if (takeLanguage is not null && SupportedLanguages.OfProject(ProjectInfo) is { } language)
        {
            takeLanguage(language);
            logger.LogInformation("The workspace is written in {language} ('{id}'), as its project file says.", language.Name, language.Id);
        }

        // the root is a folder of the workspace whether or not the client lists it among its folders.
        string[] all = [workspaceRoot, .. folders];
        _projects.Initialize(workspaceRoot, folders, all.Where(HoldsAProject));
        await LoadOtherProjectsAsync(known: []);
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
    // A folder that has its project already keeps it; one that was a folder of the workspace before (known) was said to be no project then.
    private async Task LoadOtherProjectsAsync(IReadOnlyList<string> known)
    {
        var primary = _projects.Projects[0].Root;
        foreach (var root in _projects.Folders.Roots.Where(root => !WorkspacePaths.Same(primary, root)))
        {
            if (_projects.Projects.Any(project => WorkspacePaths.Same(project.Root, root)))
            {
                continue;
            }

            if (_projects.Roles().First(entry => WorkspacePaths.Same(entry.Root, root)).Role != WorkspaceFolderRole.Project)
            {
                if (!known.Any(folder => WorkspacePaths.Same(folder, root)))
                {
                    logger.LogInformation("📁 '{folder}' holds sources a project lists, and is no project.", root);
                }

                continue;
            }

            var project = await projectFileLoader.LoadOrSynthesizeAsync(root, language());
            _projects.Add(new WorkspaceProject(root, project));
            logger.LogInformation("📁 Project '{name}' was loaded from '{folder}'.", project.ProjectInfo.Name, root);
        }
    }

    // a file a project lists: the project's folder, the file's address, whether it is an other file and no module, and the host document of a document module.
    private readonly record struct Listed(string Root, TextDocumentIdentifier Id, bool IsOther, DocClassType? Super);

    // a file a project lists is the same file of the same project whichever way the folder is spelled.
    private sealed class ListedComparer : IEqualityComparer<Listed>
    {
        public static ListedComparer Instance { get; } = new();

        public bool Equals(Listed x, Listed y) => WorkspacePaths.Same(x.Root, y.Root) && x.Id == y.Id;

        public int GetHashCode(Listed obj) => HashCode.Combine(WorkspacePaths.Comparer.GetHashCode(WorkspacePaths.Normalized(obj.Root)), obj.Id);
    }

    // each project's files: a path that climbs out of its folder (..\Shared\Util.bas) or is absolute is one only if a folder of the workspace holds it, and is refused
    // otherwise.
    private (List<Listed> Files, List<(string Project, string RelativeUri)> Refused) Listing()
    {
        var listed = new List<Listed>();
        var refused = new List<(string Project, string RelativeUri)>();
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
                        refused.Add((project.Root, file.RelativeUri));
                        continue;
                    }
                }

                listed.Add(new Listed(project.Root, new TextDocumentIdentifier(path), isOther, (file as RDCoreModule)?.Super));
            }
        }

        return (listed, refused);
    }

    private void Refuse(string project, string relativeUri)
    {
        _projects.Refuse(project, relativeUri);
        logger.LogWarning("⚠️ '{file}' is listed by project '{project}' and no folder of the workspace holds it: it is refused.", relativeUri, project);
    }

    // what the projects list as other files, and what they say their document modules are, said to the document service before any of their files is loaded.
    private void SayWhatTheProjectsList(List<Listed> listed)
    {
        // what a project lists as other files is loaded and tracked, and never parsed as code: a resource file, a README.
        documentService.SetOtherFiles(listed.Where(entry => entry.IsOther).Select(entry => entry.Id).Except(listed.Where(entry => !entry.IsOther).Select(entry => entry.Id)));

        // what the projects say their document modules are, before any is loaded: the first project that lists a file says it.
        foreach (var project in listed.GroupBy(entry => entry.Root))
        {
            documentService.SetDocumentModules(project.Key, project.Where(entry => !entry.IsOther && entry.Super is not null)
                .Select(entry => KeyValuePair.Create(entry.Id, entry.Super!.Value)));
        }
    }

    private async Task LoadWorkspaceFilesAsync()
    {
        var (listed, refused) = Listing();
        foreach (var (project, relativeUri) in refused)
        {
            Refuse(project, relativeUri);
        }

        var files = listed.Select(entry => entry.Id).Distinct().ToList();

        // seed every known document as Unloaded before loading: the document service transitions
        // state per file and each transition is only valid from a tracked Unloaded state.
        documentStateProvider.Initialize(files);
        SayWhatTheProjectsList(listed);

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

    public async Task<WorkspaceFoldersChange> ChangeFoldersAsync(IReadOnlyList<string> added, IReadOnlyList<string> removed, CancellationToken token = default)
    {
        // a workspace that was not loaded has no folders to change: it is loaded when the server is started again.
        if (projectFileService.Project?.ProjectInfo is null || _projects.Projects.Count == 0)
        {
            logger.LogInformation("The workspace folders changed, and no workspace is loaded: nothing is done.");
            return WorkspaceFoldersChange.None;
        }

        var primary = _projects.Projects[0].Root;
        var known = _projects.Folders.Roots;
        bool IsKnown(string folder) => known.Any(root => WorkspacePaths.Same(root, folder));

        // the folder the server was started on is the primary project's, which the environment host runs: it stays a folder of the workspace, and its project stays.
        foreach (var folder in removed.Where(folder => WorkspacePaths.Same(folder, primary)))
        {
            logger.LogWarning("⚠️ The client removed '{folder}', the folder of the primary project: the project the server runs is kept, and its folder with it.", folder);
        }

        var gone = removed.Where(folder => !WorkspacePaths.Same(folder, primary) && IsKnown(folder)).ToList();
        var come = added.Where(folder => !IsKnown(folder) || gone.Any(other => WorkspacePaths.Same(other, folder))).Distinct(WorkspacePaths.Comparer).ToList();
        if (gone.Count == 0 && come.Count == 0)
        {
            return WorkspaceFoldersChange.None;
        }

        token.ThrowIfCancellationRequested();
        var before = Listing();

        // the folders that are gone go first, and their projects with them; those that came are folders after the ones that are left, as at initialize.
        _projects.RemoveFolders(gone);
        var dropped = new List<string>();
        foreach (var root in gone)
        {
            var project = _projects.Projects.FirstOrDefault(candidate => WorkspacePaths.Same(candidate.Root, root));
            if (project is not null && _projects.Remove(project.Root))
            {
                dropped.Add(project.Root);
                logger.LogInformation("📁 Project '{name}' was dropped: its folder '{folder}' is no folder of the workspace now.", project.Project.ProjectInfo.Name, project.Root);
            }
        }

        _projects.AddFolders(come, come.Where(HoldsAProject));

        // a folder that came is a project's as one given at initialize is; one that was a source folder of a project that is gone may be one now.
        await LoadOtherProjectsAsync(known: [.. known.Where(root => !gone.Any(folder => WorkspacePaths.Same(folder, root)))]);

        var after = Listing();
        var had = new HashSet<Listed>(before.Files, ListedComparer.Instance);
        var has = new HashSet<Listed>(after.Files, ListedComparer.Instance);
        var gained = after.Files.Where(entry => !had.Contains(entry)).Distinct(ListedComparer.Instance).ToList();
        var lost = before.Files.Where(entry => !has.Contains(entry)).Distinct(ListedComparer.Instance).ToList();

        // a file that was refused and that a folder holds now is no longer refused, nor one of a project that is gone; one that no folder holds now is.
        foreach (var (project, relativeUri) in _projects.Refused.Where(refused => !after.Refused.Contains(refused)))
        {
            _projects.Admit(project, relativeUri);
            logger.LogInformation("📁 '{file}' is listed by project '{project}', and is no longer refused.", relativeUri, project);
        }

        var stillRefused = _projects.Refused;
        foreach (var (project, relativeUri) in after.Refused.Where(refused => !stillRefused.Contains(refused)))
        {
            Refuse(project, relativeUri);
        }

        SayWhatTheProjectsList(after.Files);
        foreach (var root in dropped)
        {
            documentService.SetDocumentModules(root, []);
        }

        // only the documents the projects gained are loaded: those loaded already keep the text, the version and the state they have, and a file another project
        // loaded before is one more project's. The projects load them one after the other, as at initialize.
        var loaded = new List<TextDocumentIdentifier>();
        foreach (var project in gained.GroupBy(entry => entry.Root))
        {
            var results = await Task.WhenAll(project.Select(async entry =>
            {
                var wasADocument = documentService.TryGetDocument(entry.Id.Uri.ToUri(), out _);
                documentStateProvider.Track(entry.Id);
                return (Entry: entry, IsNew: !wasADocument, IsLoaded: await documentService.TryLoadAsync(entry.Id, project.Key));
            }));

            // a document another project had was parsed already, and one the client has open was parsed when it was opened.
            loaded.AddRange(results.Where(result => result.IsNew && result.IsLoaded && !result.Entry.IsOther && !documentService.IsOpen(result.Entry.Id))
                .Select(result => result.Entry.Id));
        }

        // what a project no longer has is let go of once the others have what they gained: a document another project lists stays, one the client has open too.
        var leftThePrimaryProject = new List<(TextDocumentIdentifier Id, string Name)>();
        foreach (var entry in lost)
        {
            if (!entry.IsOther && WorkspacePaths.Same(entry.Root, primary) && documentService.TryGetDocument(entry.Id.Uri.ToUri(), out var document))
            {
                leftThePrimaryProject.Add((entry.Id, document.Name));
            }

            documentService.Leave(entry.Id, entry.Root);
        }

        logger.LogInformation("📁 The workspace folders changed ({come} added, {gone} removed): {gained} file(s) of its projects loaded, {lost} let go of.",
            come.Count, gone.Count, gained.Count, lost.Count);
        return new WorkspaceFoldersChange([.. loaded.Distinct()], leftThePrimaryProject, gained.Count, lost.Count);
    }
}