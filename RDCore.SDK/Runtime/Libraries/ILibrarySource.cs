using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;

namespace RDCore.SDK.Runtime.Libraries;

/// <summary>
/// Where the descriptions of libraries are found: by the name of the library, or by the identity of the type library a reference means
/// (<see cref="LibraryIdentity"/>).
/// </summary>
public interface ILibrarySource
{
    /// <summary>
    /// Gets the description of the library of a name: the one a reference that gives no identifier finds.
    /// </summary>
    /// <param name="name">The name of the library; the case of it does not matter, as it does not in VBA.</param>
    /// <param name="description">The description.</param>
    /// <returns><see langword="false"/> when the source has no library of the name.</returns>
    /// <exception cref="InvalidDataException">The source has one, and it cannot be read.</exception>
    bool TryGet(string name, [NotNullWhen(true)] out LibraryDescription? description);

    /// <summary>
    /// The names of the libraries the source has descriptions of, each once; none for a source that cannot say.
    /// </summary>
    IEnumerable<string> Names => [];

    /// <summary>
    /// The descriptions the source has of libraries of a name: the library of the name (<see cref="TryGet(string, out LibraryDescription?)"/>) and every other
    /// type library of that name it describes.
    /// </summary>
    /// <param name="name">The name of the libraries.</param>
    /// <exception cref="InvalidDataException">The source has one, and it cannot be read.</exception>
    IEnumerable<LibraryDescription> DescriptionsOf(string name) => TryGet(name, out var description) ? [description] : [];

    /// <summary>
    /// Gets the description of the library an identity means: the library of the name when it gives no identifier, and otherwise, of the descriptions of the
    /// name, the one made from a type library of that identifier and a version it accepts (<see cref="LibraryIdentity.Choose"/>).
    /// </summary>
    /// <param name="identity">The library, as a reference or a dependency asks for it.</param>
    /// <param name="description">The description.</param>
    /// <returns><see langword="false"/> when the source has no description the identity means.</returns>
    /// <exception cref="InvalidDataException">The source has one of the name, and it cannot be read.</exception>
    bool TryGet(LibraryIdentity identity, [NotNullWhen(true)] out LibraryDescription? description)
    {
        if (identity.Guid is null)
        {
            return TryGet(identity.Name, out description);
        }

        description = identity.Choose(DescriptionsOf(identity.Name));
        return description is not null;
    }

    /// <summary>
    /// Gets the description of the library a type library's identifier names: the library a <c>.vbp</c> references by its <c>{GUID}</c>.
    /// </summary>
    /// <param name="guid">The identifier the machine gave the library (<see cref="LibraryOrigin.Guid"/>).</param>
    /// <param name="description">The description; of the highest version, when the source describes several.</param>
    /// <returns><see langword="false"/> when no description of the source was made from a library of that identifier.</returns>
    bool TryGetByGuid(Guid guid, [NotNullWhen(true)] out LibraryDescription? description)
    {
        foreach (var name in Names)
        {
            if (new LibraryIdentity(name, guid).Choose(DescriptionsOf(name)) is { } candidate)
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
/// <para>
/// It is the <c>Symbols</c> folder of the platform (<see cref="Platform.IPlatformEnvironment.Resolve"/>), where the descriptions that ship with it are deployed.
/// A folder that is not there has no libraries, and is not an error: a platform with none to ship is a platform whose projects reference none.
/// </para>
/// <para>
/// <c>&lt;Name&gt;.json</c> is the library of the name. Another type library of the name is <c>&lt;Name&gt;.&lt;qualifier&gt;.json</c>, the qualifier the
/// stem of its file (<c>DAO.dao2535.json</c>): a library's name is an identifier, so the first <c>.</c> of a file's name ends it, and the qualifier only keeps
/// the files apart. Which of them a reference means is what their content says they were made from (<see cref="LibraryOrigin"/>).
/// </para>
/// </remarks>
/// <param name="fileSystem">The file system the folder is read from.</param>
/// <param name="directory">The folder.</param>
public sealed class DirectoryLibrarySource(IFileSystem fileSystem, string directory) : ILibrarySource
{
    /// <summary>
    /// The extension of a description file.
    /// </summary>
    public const string Extension = ".json";

    /// <summary>
    /// The name of the library a file describes, by its name: <c>DAO</c> for <c>DAO.json</c> and for <c>DAO.dao2535.json</c>.
    /// </summary>
    /// <param name="fileName">The name of the file, with or without its folder.</param>
    public static string LibraryNameOf(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(Path.GetFileName(fileName));
        var dot = stem.IndexOf('.');
        return dot < 0 ? stem : stem[..dot];
    }

    /// <inheritdoc/>
    public IEnumerable<string> Names => Files().Select(LibraryNameOf).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public bool TryGet(string name, [NotNullWhen(true)] out LibraryDescription? description)
    {
        // a name with a "." in it is a file's stem with its qualifier, which is not the name of a library.
        description = null;
        if (string.IsNullOrWhiteSpace(name) || name.Contains('.'))
        {
            return false;
        }

        var file = Files().FirstOrDefault(path => string.Equals(fileSystem.Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            return false;
        }

        description = Read(file, name);
        return true;
    }

    /// <inheritdoc/>
    public IEnumerable<LibraryDescription> DescriptionsOf(string name)
        => string.IsNullOrWhiteSpace(name)
            ? []
            : [.. Files().Where(path => string.Equals(LibraryNameOf(path), name, StringComparison.OrdinalIgnoreCase)).Select(path => Read(path, name))];

    private IEnumerable<string> Files() => fileSystem.Directory.Exists(directory) ? fileSystem.Directory.EnumerateFiles(directory, "*" + Extension) : [];

    private LibraryDescription Read(string file, string name)
    {
        LibraryDescription description;
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
            ? description
            : throw new InvalidDataException($"{fileSystem.Path.GetFileName(file)}: the file describes the library '{description.Name}'.");
    }
}
