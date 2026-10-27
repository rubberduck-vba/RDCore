using Microsoft.Extensions.Logging;
using RDCore.SDK.Workspace;

namespace RDCore.LanguageServer.Workspace.Services;

/// <summary>
/// <strong>LSP 3.17</strong> <c>workspace/didCreateFiles</c>, <c>workspace/didRenameFiles</c> and <c>workspace/didDeleteFiles</c>: the client created, renamed or
/// deleted files or folders of the workspace, and the project is made what the folder holds now - written to its <c>.rdproj</c>, and loaded
/// (<see cref="IWorkspaceReloadService.ChangeAsync"/>).
/// </summary>
/// <remarks>
/// <para>
/// The server is the project file's only writer: a client tells it what it did to the file system, and does not write the project file itself. A source file of
/// the workspace's language is a module of the project, and a folder that holds one is a folder of the project; what a folder whose name starts with a dot holds
/// (<c>.git</c>, <c>.vscode</c>) is not, and neither is anything outside the workspace root.
/// </para>
/// <para>
/// A module name is unique in a project (<strong>RD-VBAL §2.2.3.3</strong>): a file created with the <c>VB_Name</c> of a module the project has is not added
/// to it, and the client is told so.
/// </para>
/// </remarks>
internal interface IWorkspaceFileOperationService
{
    /// <summary>The client created these files or folders; a folder is added with every source file it holds.</summary>
    /// <param name="uris">What was created.</param>
    /// <param name="token">A token that cancels the change.</param>
    Task<WorkspaceFileOperationResult?> CreatedAsync(IEnumerable<Uri> uris, CancellationToken token);

    /// <summary>The client renamed or moved these files or folders; a folder is renamed with what it holds.</summary>
    /// <param name="renames">What was renamed, and what it is called now.</param>
    /// <param name="token">A token that cancels the change.</param>
    Task<WorkspaceFileOperationResult?> RenamedAsync(IEnumerable<(Uri OldUri, Uri NewUri)> renames, CancellationToken token);

    /// <summary>The client deleted these files or folders; a folder is removed with what it held.</summary>
    /// <param name="uris">What was deleted.</param>
    /// <param name="token">A token that cancels the change.</param>
    Task<WorkspaceFileOperationResult?> DeletedAsync(IEnumerable<Uri> uris, CancellationToken token);
}

/// <summary>
/// What the client's file operations made of the workspace.
/// </summary>
/// <param name="Reload">What the change of the project made of the workspace.</param>
/// <param name="NotAdded">The source files that are not in the project, and the module name the project has already, by their path relative to the workspace root.</param>
internal sealed record class WorkspaceFileOperationResult(WorkspaceReloadResult Reload, IReadOnlyList<(string RelativeUri, string ModuleName)> NotAdded);

internal sealed class WorkspaceFileOperationService(
    IWorkspaceReloadService reload,
    System.IO.Abstractions.IPath ioPath,
    System.IO.Abstractions.IFile ioFile,
    System.IO.Abstractions.IDirectory ioDirectory,
    Func<SupportedLanguage> language,
    ILogger<WorkspaceFileOperationService> logger) : IWorkspaceFileOperationService
{
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public Task<WorkspaceFileOperationResult?> CreatedAsync(IEnumerable<Uri> uris, CancellationToken token)
    {
        var created = uris.ToList();
        return ChangeAsync((project, notAdded) =>
        {
            foreach (var uri in created)
            {
                if (RelativeUriOf(project, uri) is { } path)
                {
                    project = Add(project, path, notAdded);
                }
            }

            return project;
        }, token);
    }

    public Task<WorkspaceFileOperationResult?> RenamedAsync(IEnumerable<(Uri OldUri, Uri NewUri)> renames, CancellationToken token)
    {
        var renamed = renames.ToList();
        return ChangeAsync((project, notAdded) =>
        {
            foreach (var (oldUri, newUri) in renamed)
            {
                var oldPath = RelativeUriOf(project, oldUri);
                var newPath = RelativeUriOf(project, newUri);
                if (oldPath is not null && newPath is not null && Lists(project, oldPath))
                {
                    project = Rename(project, oldPath, newPath);
                }
                else
                {
                    // moved in from outside the workspace, or out of it, or renamed from what was not a source file: it is what was created or deleted.
                    project = oldPath is null ? project : Remove(project, oldPath);
                    project = newPath is null ? project : Add(project, newPath, notAdded);
                }
            }

            return project;
        }, token);
    }

    public Task<WorkspaceFileOperationResult?> DeletedAsync(IEnumerable<Uri> uris, CancellationToken token)
    {
        var deleted = uris.ToList();
        return ChangeAsync((project, _) =>
        {
            foreach (var uri in deleted)
            {
                if (RelativeUriOf(project, uri) is { } path)
                {
                    project = Remove(project, path);
                }
            }

            return project;
        }, token);
    }

    // the operations are done in the order the client did them, each from what the one before made of the project: a file created and then renamed is renamed.
    private readonly object _gate = new();
    private Task _last = Task.CompletedTask;

    private Task<WorkspaceFileOperationResult?> ChangeAsync(Func<ProjectFile, List<(string, string)>, ProjectFile> change, CancellationToken token)
    {
        lock (_gate)
        {
            var next = _last.ContinueWith(_ => ChangeInOrderAsync(change, token), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            _last = next;
            return next;
        }
    }

    private async Task<WorkspaceFileOperationResult?> ChangeInOrderAsync(Func<ProjectFile, List<(string, string)>, ProjectFile> change, CancellationToken token)
    {
        var notAdded = new List<(string, string)>();
        if (await reload.ChangeAsync(project => change(project, notAdded), token) is not { } result)
        {
            return null;
        }

        if (logger.IsEnabled(LogLevel.Information) && !result.IsUnchanged)
        {
            logger.LogInformation("🗂️ The client's file operations changed the project: {added} file(s) added, {removed} removed.", result.Added, result.Removed);
        }

        return new WorkspaceFileOperationResult(result, notAdded);
    }

    // a file is a module when it is a source file, and nothing of the project otherwise; a folder is added with what it holds.
    private ProjectFile Add(ProjectFile project, string path, List<(string, string)> notAdded)
    {
        var fullPath = ioPath.Combine(project.Uri, path);
        if (ioDirectory.Exists(fullPath))
        {
            var files = ioDirectory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
                .Select(file => ioPath.GetRelativePath(project.Uri, file).Replace('\\', '/'))
                .Where(file => !IsUnderADotFolder(file))
                .Order(StringComparer.Ordinal);
            foreach (var file in files)
            {
                project = AddModule(project, file, notAdded);
            }

            return project;
        }

        return AddModule(project, path, notAdded);
    }

    private ProjectFile AddModule(ProjectFile project, string path, List<(string, string)> notAdded)
    {
        if (!IsSourceFile(path) || project.ProjectInfo.Modules.Any(module => IsPath(module.RelativeUri, path)))
        {
            return project;
        }

        var name = ModuleName.Resolve(ReadSource(ioPath.Combine(project.Uri, path)), path);
        if (project.ProjectInfo.Modules.Any(module => string.Equals(NameOf(project, module), name, StringComparison.OrdinalIgnoreCase)))
        {
            notAdded.Add((path, name));
            return project;
        }

        project = project.WithModule(new RDCoreModule { RelativeUri = path });
        return WithFoldersOf(project, path);
    }

    // a folder holds what is under it: a path is the folder's when it is the folder or under it.
    private static ProjectFile Remove(ProjectFile project, string path)
    {
        var info = project.ProjectInfo;
        return project with
        {
            ProjectInfo = info with
            {
                Modules = [.. info.Modules.Where(module => !IsUnder(module.RelativeUri, path))],
                OtherFiles = [.. info.OtherFiles.Where(file => !IsUnder(file.RelativeUri, path))],
                Folders = [.. info.Folders.Where(folder => !IsUnder(folder, path))],
            },
        };
    }

    // what the project lists keeps what it is (a document module keeps its document class) and is listed under its new path; a module renamed to what is not a
    // source file is no longer a module.
    private ProjectFile Rename(ProjectFile project, string oldPath, string newPath)
    {
        var info = project.ProjectInfo;
        project = project with
        {
            ProjectInfo = info with
            {
                Modules = [.. info.Modules
                    .Select(module => IsUnder(module.RelativeUri, oldPath) ? module with { RelativeUri = Moved(module.RelativeUri, oldPath, newPath) } : module)
                    .Where(module => IsSourceFile(module.RelativeUri))],
                OtherFiles = [.. info.OtherFiles
                    .Select(file => IsUnder(file.RelativeUri, oldPath) ? file with { RelativeUri = Moved(file.RelativeUri, oldPath, newPath) } : file)],
                Folders = [.. info.Folders.Select(folder => IsUnder(folder, oldPath) ? Moved(folder, oldPath, newPath) : folder)],
            },
        };

        return WithFoldersOf(project, newPath);
    }

    // every folder a listed path is in is a folder of the project.
    private static ProjectFile WithFoldersOf(ProjectFile project, string path)
    {
        var folders = project.ProjectInfo.Folders.ToList();
        for (var separator = path.IndexOf('/'); separator > 0; separator = path.IndexOf('/', separator + 1))
        {
            var folder = path[..separator];
            if (!folders.Any(existing => IsPath(existing, folder)))
            {
                folders.Add(folder);
            }
        }

        return folders.Count == project.ProjectInfo.Folders.Length
            ? project
            : project with { ProjectInfo = project.ProjectInfo with { Folders = [.. folders.Order(StringComparer.Ordinal)] } };
    }

    private static bool Lists(ProjectFile project, string path)
        => project.ProjectInfo.Modules.Any(module => IsUnder(module.RelativeUri, path))
            || project.ProjectInfo.OtherFiles.Any(file => IsUnder(file.RelativeUri, path))
            || project.ProjectInfo.Folders.Any(folder => IsUnder(folder, path));

    // the path relative to the workspace root, separated by '/'; nothing for what is outside it, the project file, or what a dot folder holds.
    private string? RelativeUriOf(ProjectFile project, Uri uri)
    {
        if (!uri.IsFile)
        {
            return null;
        }

        var relative = ioPath.GetRelativePath(project.Uri, uri.LocalPath).Replace('\\', '/');
        return relative == "." || relative.StartsWith("../", StringComparison.Ordinal) || relative == ".." || ioPath.IsPathRooted(relative) || IsUnderADotFolder(relative)
            ? null
            : relative;
    }

    private static bool IsUnderADotFolder(string relativePath) => relativePath.Split('/').Any(segment => segment.StartsWith('.'));

    private bool IsSourceFile(string path)
    {
        var extension = ioPath.GetExtension(path);
        return language().FileTypes.Any(fileType => string.Equals(fileType.TrimStart('*'), extension, StringComparison.OrdinalIgnoreCase));
    }

    private string NameOf(ProjectFile project, RDCoreModule module)
        => ModuleName.Resolve(ReadSource(ioPath.Combine(project.Uri, module.RelativeUri)), module.RelativeUri);

    private string? ReadSource(string path)
    {
        try
        {
            return ioFile.Exists(path) ? ioFile.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogTrace(exception, "Could not read '{path}' to resolve its module name; the file name is its name.", path);
            return null;
        }
    }

    private static string Normalized(string path) => path.Replace('\\', '/');

    private static bool IsPath(string path, string other) => string.Equals(Normalized(path), other, PathComparison);

    private static bool IsUnder(string path, string folder)
    {
        var normalized = Normalized(path);
        return string.Equals(normalized, folder, PathComparison) || normalized.StartsWith(folder + "/", PathComparison);
    }

    private static string Moved(string path, string oldPath, string newPath) => newPath + Normalized(path)[oldPath.Length..];
}
