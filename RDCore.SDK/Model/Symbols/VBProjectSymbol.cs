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
    /// The lookup is positional. A bare name, and the last part of a qualified one, is bound by
    /// <see cref="ISymbolResolver.ResolveType"/>; the qualifier is a namespace, bound by
    /// <see cref="ISymbolResolver.ResolveQualifier"/>, where neither a user-defined type nor an Enum type is a
    /// candidate — neither can contain a type. A <c>Type</c> of the enclosing module that shares the project's
    /// name therefore does not stand in for the project in <c>Project.ClassName</c>, though it is still what the bare
    /// name <c>Project</c> means. When the qualifier is the enclosing project, <paramref name="name"/> is looked up
    /// from the project's own scope, not from <paramref name="handle"/>, so nothing declared in the enclosing module
    /// can hide the project's module or type it names. A qualifier that doesn't resolve to a
    /// <see cref="VBProjectSymbol"/> stays unbound, rather than silently ignoring a qualifier that might have named
    /// something real (a referenced project) this doesn't model yet.
    /// </remarks>
    public static SymbolResolutionResult ResolveQualifiedType(ISymbolResolver resolver, string? qualifier, string name, Uri handle)
    {
        if (qualifier is null)
        {
            return resolver.ResolveType(name, ScopeKind.Global, handle);
        }

        return resolver.ResolveQualifier(qualifier, ScopeKind.Global, handle).Symbol switch
        {
            VBProjectSymbol project => resolver.ResolveProjectType(project, name),
            VBModuleSymbol module => ResolveTypeOfModule(resolver, module, name, handle),
            _ => SymbolResolutionResult.Unbound,
        };
    }

    /// <summary>
    /// Gets the class module a type was built from, as the composition has it now.
    /// </summary>
    /// <remarks>
    /// A declared type carries the class as it was when the type was built, and the class is what its members are read from. It is found by the name it was
    /// declared with <em>and</em> the library that declared it: the order of the references decides what a bare name means, and this is not a bare name.
    /// A class of the workspace has no library, and is found by its name as it always was.
    /// </remarks>
    /// <param name="resolver">The resolver names are bound by.</param>
    /// <param name="declared">The class module a type holds.</param>
    /// <returns>The class as it is now, or <see langword="null"/> when the composition no longer has it.</returns>
    public static VBClassModuleSymbol? ResolveClass(ISymbolResolver resolver, VBClassModuleSymbol declared)
        => ResolveQualifiedType(resolver, declared.GetProperty(SymbolProperties.Library), declared.Name, StaticSymbol.GlobalUri).Symbol as VBClassModuleSymbol;

    // MS-VBAL §5.6.12: "<l-expression> is classified as a procedural module or a type referencing a class defined in a class module", and the module has an
    // accessible UDT or Enum definition of the name. The type is the module's own: a type that the module's scope reaches by being the project's is not the
    // module's, and one that is Private to the module is not accessible to anything outside it.
    private static SymbolResolutionResult ResolveTypeOfModule(ISymbolResolver resolver, VBModuleSymbol module, string name, Uri handle)
    {
        var found = resolver.ResolveType(name, ScopeKind.Global, module.Uri);
        if (found.Symbol is not { } type || !string.Equals(type.ParentUri.AbsoluteUri, module.Uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
        {
            return SymbolResolutionResult.Unbound;
        }

        return IsWithin(handle, module) || ScopeTreeBuilder.IsProjectVisible(type) ? found : SymbolResolutionResult.Unbound;
    }

    // the symbol at the handle is the module, or declared in it: a symbol's address is its parent's with the fragment extended by its own name.
    private static bool IsWithin(Uri handle, VBModuleSymbol module)
    {
        var inside = handle.Fragment.TrimStart('#');
        var path = module.Uri.Fragment.TrimStart('#');
        return handle.GetLeftPart(UriPartial.Path) == module.Uri.GetLeftPart(UriPartial.Path)
            && (string.Equals(inside, path, StringComparison.OrdinalIgnoreCase) || inside.StartsWith(path + ".", StringComparison.OrdinalIgnoreCase));
    }
}
