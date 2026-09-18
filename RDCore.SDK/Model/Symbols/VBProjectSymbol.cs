using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// The enclosing VBA project itself, as a resolvable, named symbol — the <c>Enclosing Project
/// namespace</c> tier of <strong>MS-VBAL 5.6.4</strong>'s <em>type binding context</em>: a qualified
/// type reference like <c>Project.ClassName</c> resolves <c>Project</c> here before looking up
/// <c>ClassName</c>.
/// </summary>
/// <remarks>
/// Scoped to the enclosing (workspace) project only for now: a referenced project's own module
/// namespace (MS-VBAL's further "Referenced Project" / "Module in Referenced Project" tiers) needs a
/// library symbol provider that doesn't exist yet — qualifying by a referenced project's name isn't
/// modeled here.
/// </remarks>
/// <param name="WorkspaceRoot">A <c>Uri</c> representing the absolute path to the workspace.</param>
/// <param name="Name">The project's own name (its <c>.rdproj</c>/VBA project name).</param>
public sealed record class VBProjectSymbol(Uri WorkspaceRoot, string Name)
    : Symbol(WorkspaceRoot, StaticSymbol.GlobalUri, Name, ScopeKind.Global, SymbolKindExt.Project)
{
    /// <summary>
    /// Resolves the type <paramref name="name"/> in the type binding context (<strong>MS-VBAL 5.6.4</strong>),
    /// honoring an optional <paramref name="qualifier"/> (a project name, e.g. the <c>Project</c> in
    /// <c>Project.ClassName</c>).
    /// </summary>
    /// <remarks>
    /// The qualifier is itself a name in the type binding context, and the first tier that matches it is
    /// the selected tier (<strong>MS-VBAL 5.6.10</strong>): a user-defined type or Enum declared in the
    /// enclosing module with the project's name is found first, is not a project, and so leaves the
    /// qualified name unbound. When the qualifier is the enclosing project, <paramref name="name"/> is
    /// looked up from the project's own scope, not from <paramref name="handle"/>, so nothing declared in
    /// the enclosing module can hide the project's module or type it names. A qualifier that doesn't
    /// resolve to a <see cref="VBProjectSymbol"/> stays unbound, rather than silently ignoring a qualifier
    /// that might have named something real (a referenced project) this doesn't model yet.
    /// </remarks>
    public static SymbolResolutionResult ResolveQualifiedType(ISymbolResolver resolver, string? qualifier, string name, Uri handle)
    {
        if (qualifier is null)
        {
            return resolver.ResolveType(name, ScopeKind.Global, handle);
        }

        return resolver.ResolveType(qualifier, ScopeKind.Global, handle).Symbol is VBProjectSymbol project
            ? resolver.ResolveType(name, ScopeKind.Global, project.WorkspaceRoot)
            : SymbolResolutionResult.Unbound;
    }
}
