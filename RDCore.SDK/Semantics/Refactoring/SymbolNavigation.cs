using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;

namespace RDCore.SDK.Semantics.Refactoring;

/// <summary>
/// One place of a module of the workspace where a symbol is declared.
/// </summary>
/// <param name="Module">The address of the module.</param>
/// <param name="Range">The whole declaration: a procedure from its first line to its <c>End</c>, a variable with its <c>As</c> clause.</param>
/// <param name="SelectionRange">The name the declaration declares, which a client selects.</param>
public sealed record class SymbolSite(Uri Module, SourceRange Range, SourceRange SelectionRange);

/// <summary>
/// Where a navigation from a name leads: the name it starts from, and the places it leads to.
/// </summary>
/// <param name="Origin">Where the name is written, in the module the navigation starts from.</param>
/// <param name="Symbol">The symbol the name refers to.</param>
/// <param name="Sites">The places it leads to, in the order they are written; none for a symbol that no module of the workspace declares.</param>
public sealed record class SymbolNavigationTarget(SourceRange Origin, Symbol Symbol, IReadOnlyList<SymbolSite> Sites);

/// <summary>
/// Navigates from a name to what it refers to (<strong>LSP 3.17</strong> §Goto Definition Request): the name is bound the way a rename binds it
/// (<see cref="SymbolRename"/>), so that what a navigation finds is what a rename of the name would rename.
/// </summary>
/// <remarks>
/// A name declared in more than one branch of an <c>#If</c> is defined by each branch that is compiled
/// (<see cref="WorkspaceSymbol.Definitions"/>, <see cref="DefinitionState"/>); a branch whose state is not known yet is taken to be compiled. A property is
/// defined by each of its procedures, its <c>Property Get</c>, <c>Let</c> and <c>Set</c>. A module is defined by its document, and a type named in an <c>As</c>
/// clause is the type it declares. A member
/// that code accesses late-bound - on an <c>Object</c> or a <c>Variant</c> - is not known to refer to any symbol, and leads nowhere.
/// </remarks>
public static class SymbolNavigation
{
    /// <summary>
    /// Where the symbol named at <paramref name="position"/> of <paramref name="module"/> is defined.
    /// </summary>
    /// <param name="context">The workspace.</param>
    /// <param name="module">The module.</param>
    /// <param name="position">A position in the name, or at its end.</param>
    /// <returns>
    /// The name and the places that define its symbol; <see langword="null"/> where no name is written, or where what the name refers to is not known.
    /// </returns>
    public static SymbolNavigationTarget? DefinitionsAt(RenameContext context, Uri module, SourcePosition position)
    {
        if (SymbolAt(context, module, position) is not { } named)
        {
            return null;
        }

        var (origin, symbol) = named;
        IReadOnlyList<SymbolSite> sites = NameOccurrences.IsDeclaredBy(context, symbol)
            ? [.. DefinedTogether(context, symbol).SelectMany(Definitions).OrderBy(site => site.Range.Start)]
            : [];
        return new SymbolNavigationTarget(origin, symbol, sites);
    }

    // a property is defined by each of its procedures - its Property Get, Let and Set: what an access calls is not known from the name alone.
    private static IEnumerable<Symbol> DefinedTogether(RenameContext context, Symbol symbol)
    {
        if (symbol is not (VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol)
            || !context.ScopeTree.TryGetScope(NameOccurrences.ModuleOf(symbol.Uri), out var module))
        {
            return [symbol];
        }

        var accessors = module.DeclaredAs(symbol.Name)
            .Where(declared => declared is VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol)
            .ToList();
        return accessors.Count > 0 ? accessors : [symbol];
    }

    // the name at the position, and the symbol it refers to there: a value, or the type a name that means no value names - a class that is created, a Type a
    // variable is declared as.
    private static (SourceRange Origin, Symbol Symbol)? SymbolAt(RenameContext context, Uri module, SourcePosition position)
    {
        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } source)
        {
            return null;
        }

        if (NameOccurrences.NameAt(source, position) is not { } name)
        {
            return NameOccurrences.TypeNameAt(source, position) is { } typeName
                && context.Resolver.ResolveType(typeName.Name, ScopeKind.Local, NameOccurrences.ScopeOf(context, source, position).Uri).Symbol is { } declared
                ? (typeName.Range, declared)
                : null;
        }

        var found = NameOccurrences.Find(context, name, [source]);
        if (found.Unknown.Any(occurrence => Contains(occurrence.Range, position)))
        {
            return null;
        }

        var here = found.Occurrences.FirstOrDefault(occurrence => Contains(occurrence.Range, position));
        if (here.Binding is { } binding && NameOccurrences.SymbolOf(context, binding) is { } symbol)
        {
            return (here.Range, symbol);
        }

        return here.Scope is { } scope && context.Resolver.ResolveType(name, ScopeKind.Local, scope.Uri).Symbol is { } type
            ? (here.Range, type)
            : null;
    }

    // the sites of a symbol that are compiled, or every site where none is; a module is defined by its whole document.
    private static IReadOnlyList<SymbolSite> Definitions(Symbol symbol)
    {
        var declaring = NameOccurrences.ModuleOf(symbol.Uri);
        if (symbol is not WorkspaceSymbol declared)
        {
            return symbol is VBModuleSymbol ? [new SymbolSite(declaring, default, default)] : [];
        }

        var definitions = declared.AllDefinitions.ToList();
        var compiled = definitions.Where(definition => definition.State != DefinitionState.Dead).ToList();
        return [.. (compiled.Count > 0 ? compiled : definitions).Select(definition => new SymbolSite(declaring, definition.Range, definition.SelectionRange))];
    }

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start <= position && position <= range.End;
}
