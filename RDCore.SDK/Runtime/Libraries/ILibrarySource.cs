using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;

namespace RDCore.SDK.Runtime.Libraries;

/// <summary>
/// Where the descriptions of libraries are found, by the name of the library.
/// </summary>
public interface ILibrarySource
{
    /// <summary>
    /// Gets the description of a library.
    /// </summary>
    /// <param name="name">The name of the library; the case of it does not matter, as it does not in VBA.</param>
    /// <param name="description">The description.</param>
    /// <returns><see langword="false"/> when the source has no library of the name.</returns>
    /// <exception cref="InvalidDataException">The source has one, and it cannot be read.</exception>
    bool TryGet(string name, [NotNullWhen(true)] out LibraryDescription? description);

    /// <summary>
    /// The names of the libraries the source has descriptions of; none for a source that cannot say.
    /// </summary>
    IEnumerable<string> Names => [];

    /// <summary>
    /// Gets the description of the library a type library's identifier names: the library a <c>.vbp</c> references by its <c>{GUID}</c>.
    /// </summary>
    /// <param name="guid">The identifier the machine gave the library (<see cref="LibraryOrigin.Guid"/>).</param>
    /// <param name="description">The description.</param>
    /// <returns><see langword="false"/> when no description of the source was made from a library of that identifier.</returns>
    bool TryGetByGuid(Guid guid, [NotNullWhen(true)] out LibraryDescription? description)
    {
        foreach (var name in Names)
        {
            if (TryGet(name, out var candidate) && System.Guid.TryParse(candidate.Origin?.Guid, out var origin) && origin == guid)
            {
                description = candidate;
                return true;
            }
        }

        description = null;
        return false;
    }
}

/// <summary>
/// A source of library descriptions that is a folder of files: <c>Scripting.json</c> describes the library <c>Scripting</c>.
/// </summary>
/// <remarks>
/// It is the <c>Symbols</c> folder of the platform (<see cref="Platform.IPlatformEnvironment.Resolve"/>), where the descriptions that ship with it are deployed.
/// A folder that is not there has no libraries, and is not an error: a platform with none to ship is a platform whose projects reference none.
/// </remarks>
/// <param name="fileSystem">The file system the folder is read from.</param>
/// <param name="directory">The folder.</param>
public sealed class DirectoryLibrarySource(IFileSystem fileSystem, string directory) : ILibrarySource
{
    /// <summary>
    /// The extension of a description file.
    /// </summary>
    public const string Extension = ".json";

    /// <inheritdoc/>
    public IEnumerable<string> Names => fileSystem.Directory.Exists(directory)
        ? fileSystem.Directory.EnumerateFiles(directory, "*" + Extension).Select(path => fileSystem.Path.GetFileNameWithoutExtension(path))
        : [];

    /// <inheritdoc/>
    public bool TryGet(string name, [NotNullWhen(true)] out LibraryDescription? description)
    {
        description = null;
        if (string.IsNullOrWhiteSpace(name) || !fileSystem.Directory.Exists(directory))
        {
            return false;
        }

        var file = fileSystem.Directory.EnumerateFiles(directory, "*" + Extension)
            .FirstOrDefault(path => string.Equals(fileSystem.Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            return false;
        }

        try
        {
            description = LibraryJson.Read(fileSystem.File.ReadAllText(file));
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"{fileSystem.Path.GetFileName(file)}: {exception.Message}", exception);
        }

        // a file is the library its content says it is: one that is named for another is a copy that was not renamed, and is not either.
        return string.Equals(description.Name, name, StringComparison.OrdinalIgnoreCase)
            ? true
            : throw new InvalidDataException($"{fileSystem.Path.GetFileName(file)}: the file describes the library '{description.Name}'.");
    }
}
