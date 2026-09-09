using System.IO.Abstractions;
using System.Text.Json;

namespace RDCore.SDK.Workspace;

/// <summary>
/// Writes a workspace's <c>.rdproj</c> <see cref="ProjectFile"/> to disk — the write counterpart of
/// <see cref="IProjectFileLoader"/>. Used to scaffold a new workspace and (eventually) to persist
/// edits a client makes to the project structure.
/// </summary>
public interface IProjectFileWriter
{
    /// <summary>
    /// Serializes <paramref name="project"/> to <c>&lt;project.Uri&gt;/.rdproj</c>, overwriting any
    /// existing file. The parent directory must exist. <see cref="ProjectFile.IsDirty"/> is not
    /// consulted — the caller decides whether a write is warranted.
    /// </summary>
    /// <param name="project">The project to write; its <see cref="ProjectFile.Uri"/> is the workspace root.</param>
    /// <param name="token">A token that cancels the write.</param>
    /// <exception cref="IOException">The file could not be written.</exception>
    Task SaveAsync(ProjectFile project, CancellationToken token = default);
}

/// <inheritdoc cref="IProjectFileWriter"/>
public sealed class ProjectFileWriter(IFileSystem fileSystem) : IProjectFileWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <inheritdoc/>
    public async Task SaveAsync(ProjectFile project, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var path = fileSystem.Path.Combine(project.Uri, ProjectFile.FileName);
        await using var stream = fileSystem.File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, project, SerializerOptions, token);
    }
}
