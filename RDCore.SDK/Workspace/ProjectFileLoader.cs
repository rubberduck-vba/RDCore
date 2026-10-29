using System.IO.Abstractions;
using System.Text.Json;

namespace RDCore.SDK.Workspace;

/// <summary>
/// Loads a workspace's <c>.rdproj</c> <see cref="ProjectFile"/> from disk, read-only. Every platform
/// component that needs the project structure — the language server's workspace services and the
/// environment host's session composition — resolves the project through this service so there is a
/// single deserialization path.
/// </summary>
public interface IProjectFileLoader
{
    /// <summary>
    /// Resolves the path a <c>.rdproj</c> would occupy under <paramref name="workspaceRoot"/> and
    /// reports whether a file exists there.
    /// </summary>
    /// <param name="workspaceRoot">The absolute path of the workspace root directory.</param>
    /// <param name="projectFilePath">The resolved <c>.rdproj</c> path, whether or not it exists.</param>
    /// <returns><c>true</c> if a file exists at <paramref name="projectFilePath"/>.</returns>
    bool TryLocate(string workspaceRoot, out string projectFilePath);

    /// <summary>
    /// Loads and deserializes the <c>.rdproj</c> under <paramref name="workspaceRoot"/>, stamping its
    /// <see cref="ProjectFile.Uri"/> with the workspace root everything else is relative to. A reference to the standard library, which a project file written
    /// before lists, is not read: every project has it (<see cref="RDCoreProject.References"/>).
    /// </summary>
    /// <param name="workspaceRoot">The absolute path of the workspace root directory.</param>
    /// <param name="token">A token that cancels the read.</param>
    /// <exception cref="FileNotFoundException">No <c>.rdproj</c> exists under the workspace root.</exception>
    /// <exception cref="InvalidDataException">The <c>.rdproj</c> is not JSON; the message says where it stops being JSON.</exception>
    /// <exception cref="InvalidOperationException">The <c>.rdproj</c> could not be deserialized.</exception>
    Task<ProjectFile> LoadAsync(string workspaceRoot, CancellationToken token = default);

    /// <summary>
    /// Loads the <c>.rdproj</c> under <paramref name="workspaceRoot"/> as <see cref="LoadAsync"/> does, or, when the folder has none, makes up the project
    /// the folder holds (<see cref="ProjectFile.IsSynthesized"/>): every source file of <paramref name="language"/> under it is a module, and nothing is
    /// written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A client that opens a folder - rather than a project - is given the project the folder is. The folder is searched to every depth, except the folders
    /// whose name starts with a dot (<c>.git</c>, <c>.github</c>) and links to other folders. Each folder that holds a module, at any depth, is a folder of the
    /// project. Paths are relative to the workspace root, separated by <c>/</c>, and in ordinal order, so the language server and the environment host, which
    /// each synthesize the project, make up the same one.
    /// </para>
    /// <para>
    /// A module name is unique in a project (<strong>RD-VBAL §2.2.3.3</strong>): of two files that declare the same <c>VB_Name</c>, the first in that order
    /// is the module, and the other is not in the project. The project is named after the folder when its name is an identifier, and <c>VBAProject</c>
    /// otherwise, as the VBE names a new one.
    /// </para>
    /// </remarks>
    /// <param name="workspaceRoot">The absolute path of the workspace root directory.</param>
    /// <param name="language">The language the workspace is written in, whose <see cref="SupportedLanguage.FileTypes"/> are its source files.</param>
    /// <param name="token">A token that cancels the read.</param>
    /// <exception cref="InvalidDataException">The <c>.rdproj</c> is not JSON; the message says where it stops being JSON.</exception>
    /// <exception cref="InvalidOperationException">The <c>.rdproj</c> could not be deserialized.</exception>
    Task<ProjectFile> LoadOrSynthesizeAsync(string workspaceRoot, SupportedLanguage language, CancellationToken token = default);

    /// <summary>
    /// Loads the <c>.rdproj</c> under <paramref name="workspaceRoot"/> as <see cref="LoadAsync"/> does, when the folder has one that can be read: what is
    /// about to replace a project file keeps what it says (<see cref="VbpImport.Into"/>).
    /// </summary>
    /// <param name="workspaceRoot">The absolute path of the folder.</param>
    /// <param name="token">A token that cancels the read.</param>
    /// <returns>The project file; <see langword="null"/> when the folder has none, or one that is not JSON or not a project file.</returns>
    async Task<ProjectFile?> LoadIfReadableAsync(string workspaceRoot, CancellationToken token = default)
    {
        if (!TryLocate(workspaceRoot, out _))
        {
            return null;
        }

        try
        {
            return await LoadAsync(workspaceRoot, token);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <inheritdoc cref="IProjectFileLoader"/>
public sealed class ProjectFileLoader(IFileSystem fileSystem) : IProjectFileLoader
{
    /// <inheritdoc/>
    public bool TryLocate(string workspaceRoot, out string projectFilePath)
    {
        projectFilePath = fileSystem.Path.Combine(workspaceRoot, ProjectFile.FileName);
        return fileSystem.File.Exists(projectFilePath);
    }

    /// <inheritdoc/>
    public async Task<ProjectFile> LoadAsync(string workspaceRoot, CancellationToken token = default)
    {
        if (!TryLocate(workspaceRoot, out var path))
        {
            throw new FileNotFoundException($"No {ProjectFile.FileName} was found under the workspace root.", path);
        }

        await using var stream = fileSystem.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ProjectFile? project;
        try
        {
            project = await JsonSerializer.DeserializeAsync<ProjectFile>(stream, cancellationToken: token);
        }
        catch (JsonException exception)
        {
            // where the file stops being JSON, in the lines and columns an editor counts from 1.
            throw new InvalidDataException(string.Format(Exceptions.ProjectFile_InvalidJson, path,
                (exception.LineNumber ?? 0) + 1, (exception.BytePositionInLine ?? 0) + 1, exception.Message), exception);
        }

        if (project is null)
        {
            throw new InvalidOperationException($"The {ProjectFile.FileName} at '{path}' could not be deserialized.");
        }

        // the standard library is every project's own, and no reference of it: a project file written before lists it - with the path of Office's VBE7.DLL -
        // and is read without it, which it is written without the next time.
        var loaded = project.WithUri(workspaceRoot);
        return loaded.ProjectInfo is { } info && info.References.Any(reference => reference.IsStandardLibrary)
            ? loaded with { ProjectInfo = info with { References = [.. info.References.Where(reference => !reference.IsStandardLibrary)] } }
            : loaded;
    }

    /// <inheritdoc/>
    public async Task<ProjectFile> LoadOrSynthesizeAsync(string workspaceRoot, SupportedLanguage language, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (TryLocate(workspaceRoot, out _))
        {
            return await LoadAsync(workspaceRoot, token);
        }

        var modules = new List<RDCoreModule>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new List<string>();
        await ScanAsync(workspaceRoot, workspaceRoot, language, modules, names, folders, token);

        var project = new RDCoreProject
        {
            Name = ProjectNameOf(fileSystem.Path.GetFileName(fileSystem.Path.TrimEndingDirectorySeparator(workspaceRoot))),
            Modules = [.. modules],
            Folders = [.. folders.Order(StringComparer.Ordinal)],
        };

        return new ProjectFile(workspaceRoot, project) { IsSynthesized = true };
    }

    private async Task ScanAsync(string root, string directory, SupportedLanguage language,
        List<RDCoreModule> modules, HashSet<string> names, List<string> folders, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string[] files, directories;
        try
        {
            files = [.. fileSystem.Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal)];
            directories = [.. fileSystem.Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // a folder that cannot be read holds nothing of the project.
            return;
        }

        foreach (var file in files.Where(file => IsSourceFile(file, language)))
        {
            var relativeUri = RelativeUriOf(root, file);
            var name = ModuleName.Resolve(await ReadSourceAsync(file, token), relativeUri);
            if (names.Add(name))
            {
                modules.Add(new RDCoreModule { RelativeUri = relativeUri });
            }
        }

        foreach (var subdirectory in directories)
        {
            if (fileSystem.Path.GetFileName(subdirectory).StartsWith('.')
                || fileSystem.File.GetAttributes(subdirectory).HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            var count = modules.Count;
            await ScanAsync(root, subdirectory, language, modules, names, folders, token);
            if (modules.Count > count)
            {
                folders.Add(RelativeUriOf(root, subdirectory));
            }
        }
    }

    private bool IsSourceFile(string path, SupportedLanguage language)
    {
        var extension = fileSystem.Path.GetExtension(path);
        return language.FileTypes.Any(fileType => string.Equals(fileType.TrimStart('*'), extension, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> ReadSourceAsync(string path, CancellationToken token)
    {
        try
        {
            return await fileSystem.File.ReadAllTextAsync(path, token);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // the file name names a module whose source cannot be read.
            return null;
        }
    }

    private string RelativeUriOf(string root, string path) => fileSystem.Path.GetRelativePath(root, path).Replace('\\', '/');

    // an identifier (MS-VBAL §3.3.5): a letter, then letters, digits and underscores; not VBA, which names the standard library.
    private static string ProjectNameOf(string? folderName)
        => folderName is { Length: > 0 and <= 255 } name
            && char.IsAsciiLetter(name[0])
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            && !string.Equals(name, "VBA", StringComparison.OrdinalIgnoreCase)
            ? name
            : "VBAProject";
}
