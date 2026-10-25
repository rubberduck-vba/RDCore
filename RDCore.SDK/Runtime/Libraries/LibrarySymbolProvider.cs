using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Workspace;
using System.Collections.Immutable;

namespace RDCore.SDK.Runtime.Libraries;

/// <summary>
/// The symbols of the libraries a project references: the ones that are not the standard library, which is the platform's own
/// (<see cref="StdLibSymbolProvider"/>).
/// </summary>
/// <remarks>
/// The language server and the environment host each build their symbols from the same libraries, with this, so that a name the language server bound
/// binds to the same symbol in the host - which is why the symbols are addressed under the workspace and nothing else decides their address.
/// </remarks>
public sealed class LibrarySymbolProvider : ISymbolProvider
{
    private readonly Uri _workspaceRoot;
    private readonly bool _is64Bit;

    /// <summary>
    /// Creates the provider for libraries that are loaded.
    /// </summary>
    /// <param name="workspaceRoot">The workspace the symbols are addressed under.</param>
    /// <param name="libraries">The libraries (<see cref="Load"/>).</param>
    /// <param name="is64Bit">The pointer width of the environment.</param>
    public LibrarySymbolProvider(Uri workspaceRoot, ReferencedLibraries libraries, bool is64Bit = true)
    {
        _workspaceRoot = workspaceRoot;
        _is64Bit = is64Bit;
        Libraries = libraries;
    }

    /// <summary>
    /// The libraries, and the ones that could not be loaded.
    /// </summary>
    public ReferencedLibraries Libraries { get; }

    /// <summary>
    /// Loads the libraries a project references.
    /// </summary>
    /// <param name="source">Where the descriptions are.</param>
    /// <param name="references">The references of the project.</param>
    public static ReferencedLibraries Load(ILibrarySource source, IEnumerable<RDCoreReference> references)
        => ReferencedLibraries.Load(source, references.Select(reference => reference.Name), [StdLibSymbolProvider.LibraryName]);

    /// <summary>
    /// Reads the symbols of a library whether or not the project references it, and of the libraries it depends on: what tooling knows of the class of an object
    /// created by name (<see cref="Semantics.ObjectCreationFact"/>).
    /// </summary>
    /// <remarks>
    /// The symbols are never names of the project: the workspace is not composed with them, and no code can use them. They are addressed as the symbols of a
    /// referenced library are, so that the class they describe is the same one when the project does reference it.
    /// </remarks>
    /// <param name="source">Where the descriptions are.</param>
    /// <param name="workspaceRoot">The workspace the symbols are addressed under.</param>
    /// <param name="library">The name of the library.</param>
    /// <param name="is64Bit">The pointer width of the environment.</param>
    /// <returns>The symbols; none when the source has no library of the name, or it cannot be loaded.</returns>
    public static ImmutableArray<Symbol> ReadDescribed(ILibrarySource source, Uri workspaceRoot, string library, bool is64Bit = true)
    {
        var libraries = ReferencedLibraries.Load(source, [library], [StdLibSymbolProvider.LibraryName]);
        return libraries.Libraries.Any(loaded => string.Equals(loaded.Name, library, StringComparison.OrdinalIgnoreCase))
            ? new LibrarySymbolReader(workspaceRoot, is64Bit).Read(libraries.Libraries, libraries.Priorities)
            : [];
    }

    /// <inheritdoc/>
    public IEnumerable<Symbol> ProvideSymbols()=> new LibrarySymbolReader(_workspaceRoot, _is64Bit).Read(Libraries.Libraries, Libraries.Priorities);
}
