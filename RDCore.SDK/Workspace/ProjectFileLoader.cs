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
    /// <see cref="ProjectFile.Uri"/> with the workspace root everything else is relative to.
    /// </summary>
    /// <param name="workspaceRoot">The absolute path of the workspace root directory.</param>
    /// <param name="token">A token that cancels the read.</param>
    /// <exception cref="FileNotFoundException">No <c>.rdproj</c> exists under the workspace root.</exception>
    /// <exception cref="InvalidOperationException">The <c>.rdproj</c> could not be deserialized.</exception>
    Task<ProjectFile> LoadAsync(string workspaceRoot, CancellationToken token = default);
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
        var project = await JsonSerializer.DeserializeAsync<ProjectFile>(stream, cancellationToken: token);
        if (project is null)
        {
            throw new InvalidOperationException($"The {ProjectFile.FileName} at '{path}' could not be deserialized.");
        }

        return project.WithUri(workspaceRoot);
    }
}
