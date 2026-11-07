using System.Collections.Immutable;

namespace RDCore.SDK.Runtime.Libraries;

/// <summary>
/// Why a library could not be loaded.
/// </summary>
public enum LibraryProblemKind
{
    /// <summary>
    /// The source has no library of the name, or, for one asked for by the identifier and version of its type library, none that they identify.
    /// </summary>
    NotFound = 0,

    /// <summary>The source has one, and it cannot be read.</summary>
    Unreadable = 1,

    /// <summary>The library depends on itself, through the libraries it depends on. Such a reference is rejected, not resolved.</summary>
    Cyclic = 2,

    /// <summary>A library it depends on could not be loaded, so neither can it.</summary>
    DependencyFailed = 3,
}

/// <summary>
/// A library that a project references, or that one it references depends on, and that could not be loaded.
/// </summary>
/// <param name="Name">The name of the library, as it was asked for.</param>
/// <param name="Kind">Why it could not be.</param>
/// <param name="Related">
/// What else the problem is about: for <see cref="LibraryProblemKind.Cyclic"/>, the libraries on the cycle in the order they depend on one another, ending with
/// the one the path started from; for <see cref="LibraryProblemKind.DependencyFailed"/>, the library that failed; for <see cref="LibraryProblemKind.Unreadable"/>,
/// the reason it cannot be read; for <see cref="LibraryProblemKind.NotFound"/>, the identity that was asked for (<see cref="LibraryIdentity"/>), when it gives
/// an identifier, followed by the name of the library the source has of that identifier, when it is another name.
/// </param>
public sealed record class LibraryProblem(string Name, LibraryProblemKind Kind, ImmutableArray<string> Related);

/// <summary>
/// The libraries a project references, loaded: those that were asked for and those they depend on, and the ones that could not be.
/// </summary>
/// <remarks>
/// <para>
/// A library is loaded as a whole or not at all. One that cannot be found or read is a problem, and so is every library that depends on it: what a library
/// declares is typed with what it depends on, and a description whose types name nothing is not one a project can be checked against.
/// </para>
/// <para>
/// Libraries that depend on one another are <em>rejected</em>, not resolved: each of the libraries on the cycle is a problem, with the cycle in it. There is
/// no order to load them in, and the platform does not make one up. (The types a single library declares may name one another freely; that is not a reference
/// between libraries.)
/// </para>
/// </remarks>
public sealed class ReferencedLibraries
{
    private ReferencedLibraries(ImmutableArray<LibraryDescription> libraries, ImmutableArray<LibraryProblem> problems, ImmutableDictionary<string, int> priorities)
    {
        Libraries = libraries;
        Problems = problems;
        Priorities = priorities;
    }

    /// <summary>
    /// No libraries, and no problems.
    /// </summary>
    public static ReferencedLibraries None { get; } = new([], [], ImmutableDictionary<string, int>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The reference precedence of each library that was loaded, by its name: of two libraries that declare a name, the one with the lower number is the one the
    /// name means (<see cref="Model.Symbols.Abstract.SymbolProperties.LibraryPriority"/>).
    /// </summary>
    /// <remarks>
    /// A reference that occurs earlier in the list of a project's references has the higher reference precedence (<strong>MS-VBAL §4.1</strong>), and the
    /// references are in the order of the project file (<strong>RD-VBAL §2.3.1.2</strong>): the first library the project references is <c>1</c>, the next
    /// <c>2</c>, and so on, a reference to a provided library, or a second one to the same library, not counted, and a reference that fails to load keeping its
    /// number. A library that is loaded only because another depends on it comes after every library the project references, in the order it was
    /// loaded. The standard library, which the platform provides and which comes before every reference, is not in it.
    /// </remarks>
    public ImmutableDictionary<string, int> Priorities { get; }

    /// <summary>
    /// The libraries that were loaded, each after the libraries it depends on.
    /// </summary>
    public ImmutableArray<LibraryDescription> Libraries { get; }

    /// <summary>
    /// The libraries that could not be loaded.
    /// </summary>
    public ImmutableArray<LibraryProblem> Problems { get; }

    /// <summary>
    /// Loads the libraries a project references.
    /// </summary>
    /// <remarks>
    /// A library is found by its identity (<see cref="ILibrarySource.TryGet(LibraryIdentity, out LibraryDescription?)"/>): the library of its name, or the
    /// description of the type library it gives. A session has one library of a name: a dependency whose name the project references is the library the
    /// project references, whatever identity the library that depends on it states (<strong>RD-VBAL §2.3.1.2</strong>).
    /// </remarks>
    /// <param name="source">Where the descriptions are.</param>
    /// <param name="references">
    /// The libraries the project references, in the order of its references; a second reference to a name is left out. The standard library (<c>VBA</c>) is the
    /// platform's own, and is not asked of the source.
    /// </param>
    /// <param name="provided">The names of the libraries the platform provides itself.</param>
    public static ReferencedLibraries Load(ILibrarySource source, IEnumerable<LibraryIdentity> references, IReadOnlyCollection<string> provided)
    {
        var referenced = references.Where(reference => !provided.Contains(reference.Name, StringComparer.OrdinalIgnoreCase))
            .DistinctBy(reference => reference.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var loader = new Loader(source, provided, referenced.ToDictionary(reference => reference.Name, StringComparer.OrdinalIgnoreCase));
        foreach (var reference in referenced)
        {
            loader.Load(reference, []);
        }

        var loaded = loader.Loaded.Select(library => library.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var priorities = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < referenced.Length; i++)
        {
            if (loaded.Contains(referenced[i].Name))
            {
                priorities[referenced[i].Name] = i + 1;
            }
        }

        var dependencies = loader.Loaded.Where(library => !priorities.ContainsKey(library.Name)).ToArray();
        for (var i = 0; i < dependencies.Length; i++)
        {
            priorities[dependencies[i].Name] = referenced.Length + 1 + i;
        }

        return new ReferencedLibraries([.. loader.Loaded], [.. loader.Problems], priorities.ToImmutable());
    }

    // `claimed` is the identity of each name the project references: what a dependency of that name loads.
    private sealed class Loader(ILibrarySource source, IReadOnlyCollection<string> provided, IReadOnlyDictionary<string, LibraryIdentity> claimed)
    {
        private readonly Dictionary<string, LibraryDescription> _loaded = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LibraryProblem> _failed = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<LibraryDescription> _order = [];
        private readonly List<LibraryProblem> _problems = [];

        public IReadOnlyList<LibraryDescription> Loaded => _order;

        public IReadOnlyList<LibraryProblem> Problems => _problems;

        // true when the library is loaded. `path` is the libraries being loaded that depend on this one, outermost first. A library of a name that is already
        // loaded is that library, whatever identity is asked for.
        public bool Load(LibraryIdentity asked, ImmutableArray<string> path)
        {
            var name = asked.Name;
            var identity = claimed.TryGetValue(name, out var referenced) ? referenced : asked;
            if (provided.Contains(name, StringComparer.OrdinalIgnoreCase) || _loaded.ContainsKey(name))
            {
                return true;
            }

            if (_failed.ContainsKey(name))
            {
                return false;
            }

            if (path.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                RejectCycle(name, path);
                return false;
            }

            LibraryDescription? description;
            try
            {
                if (!source.TryGet(identity, out description))
                {
                    return Fail(new LibraryProblem(name, LibraryProblemKind.NotFound, NotFound(identity)));
                }
            }
            catch (InvalidDataException exception)
            {
                return Fail(new LibraryProblem(name, LibraryProblemKind.Unreadable, [exception.Message]));
            }

            var below = path.Add(name);
            foreach (var dependency in description.DependsOn)
            {
                if (!Load(dependency, below))
                {
                    // a library on a cycle has already been rejected by the one that found the cycle, and says so.
                    if (!_failed.ContainsKey(name))
                    {
                        Fail(new LibraryProblem(name, LibraryProblemKind.DependencyFailed, [dependency.Name]));
                    }

                    return false;
                }
            }

            _loaded[name] = description;
            _order.Add(description);
            return true;
        }

        // the identity that was asked for, and the library its identifier is of when that is another name: the reference names one library and identifies
        // another, and neither is chosen for it.
        private ImmutableArray<string> NotFound(LibraryIdentity identity)
        {
            if (identity.Guid is not { } guid)
            {
                return [];
            }

            return source.TryGetByGuid(guid, out var other) && !string.Equals(other.Name, identity.Name, StringComparison.OrdinalIgnoreCase)
                ? [identity.ToString(), other.Name]
                : [identity.ToString()];
        }

        private bool Fail(LibraryProblem problem)
        {
            _failed[problem.Name] = problem;
            _problems.Add(problem);
            return false;
        }

        // every library on the cycle is rejected, each with the cycle: the path from the library that was reached again, back to it.
        private void RejectCycle(string reached, ImmutableArray<string> path)
        {
            var start = path.ToList().FindIndex(candidate => string.Equals(candidate, reached, StringComparison.OrdinalIgnoreCase));
            var cycle = path.Skip(start).Append(reached).ToImmutableArray();
            foreach (var member in cycle.Take(cycle.Length - 1))
            {
                if (!_failed.ContainsKey(member))
                {
                    Fail(new LibraryProblem(member, LibraryProblemKind.Cyclic, cycle));
                }
            }
        }
    }
}
