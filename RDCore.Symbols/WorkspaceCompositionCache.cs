using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Symbols;

/// <summary>
/// The compositions of a workspace made last (<see cref="WorkspaceSymbolResolver.ComposeWithScopes"/>), reused for as long as what they are made of is the
/// same: what tooling asks of the workspace at each key - a completion list, a signature, a rename - is asked of the same modules most of the time.
/// </summary>
/// <remarks>
/// <para>
/// A composition is a function of the parses of the modules, the libraries, the resolver it falls back to and how undeclared names are declared, and nothing
/// else. It is reused when each of those is the very one it was made of: a parse is the same parse, and not one equal to it - a document's parse is
/// kept for as long as its text is (<c>ParsingClientService</c>), and so is a fragment's. A module whose text changed has another parse, and the workspace is
/// composed again.
/// </para>
/// <para>
/// A composition holds the symbols of the whole workspace: only the few made last are kept. What a composition's scope tree has resolved is true of that
/// tree, and stays with it (<see cref="ScopeTree"/>).
/// </para>
/// </remarks>
/// <param name="capacity">How many compositions are kept: those used last.</param>
public sealed class WorkspaceCompositionCache(int capacity = WorkspaceCompositionCache.DefaultCapacity)
{
    /// <summary>
    /// How many compositions are kept unless said otherwise: a few - the module with the line being typed taken out, and with a member access put in its
    /// place, and the workspace a rename composes.
    /// </summary>
    public const int DefaultCapacity = 4;

    private readonly object _sync = new();
    private readonly LinkedList<(Key Key, WorkspaceComposition Composition)> _recent = new();

    /// <summary>How many times a composition was made, rather than reused: what tells a cache that works from one that does not.</summary>
    public int Composed { get; private set; }

    /// <summary>
    /// The composition of the workspace (<see cref="WorkspaceSymbolResolver.ComposeWithScopes"/>): the one made last of the very same modules, or a new one.
    /// </summary>
    /// <param name="workspaceRoot">The workspace.</param>
    /// <param name="modules">The modules, each with the parse it is composed of.</param>
    /// <param name="fallback">The resolver the workspace falls back to.</param>
    /// <param name="projectName">The project's name, when it is known.</param>
    /// <param name="implicitScope">Where a name that is not declared is declared.</param>
    /// <param name="libraries">The symbols of the libraries the project references.</param>
    public WorkspaceComposition GetOrCompose(
        Uri workspaceRoot, IReadOnlyList<(Uri ModuleUri, ModuleType ModuleType, ModuleParseResult Parse)> modules, ISymbolResolver fallback,
        string? projectName = null, ImplicitDeclarationScope implicitScope = ImplicitDeclarationScope.Procedure, IReadOnlyList<Symbol>? libraries = null)
    {
        var key = new Key(workspaceRoot.AbsoluteUri, projectName, implicitScope, fallback, libraries, modules);
        lock (_sync)
        {
            for (var node = _recent.First; node is not null; node = node.Next)
            {
                if (node.Value.Key.IsMadeOfTheSame(key))
                {
                    _recent.Remove(node);
                    _recent.AddFirst(node);
                    return node.Value.Composition;
                }
            }
        }

        // a workspace is composed outside the lock: two requests that miss compose it each, and the one stored last is kept.
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(workspaceRoot, modules, fallback, projectName, implicitScope, libraries);
        lock (_sync)
        {
            Composed++;
            _recent.AddFirst((key, composition));
            while (_recent.Count > Math.Max(1, capacity))
            {
                _recent.RemoveLast();
            }
        }

        return composition;
    }

    // what a composition is made of; the parses, the libraries and the resolver are compared as the objects they are.
    private sealed record class Key(
        string Root, string? ProjectName, ImplicitDeclarationScope ImplicitScope, ISymbolResolver Fallback, IReadOnlyList<Symbol>? Libraries,
        IReadOnlyList<(Uri ModuleUri, ModuleType ModuleType, ModuleParseResult Parse)> Modules)
    {
        public bool IsMadeOfTheSame(Key other)
        {
            if (Root != other.Root || ProjectName != other.ProjectName || ImplicitScope != other.ImplicitScope
                || !ReferenceEquals(Fallback, other.Fallback) || !ReferenceEquals(Libraries, other.Libraries) || Modules.Count != other.Modules.Count)
            {
                return false;
            }

            for (var index = 0; index < Modules.Count; index++)
            {
                var (uri, type, parse) = Modules[index];
                var (otherUri, otherType, otherParse) = other.Modules[index];
                if (type != otherType || !ReferenceEquals(parse, otherParse) || uri.AbsoluteUri != otherUri.AbsoluteUri)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
