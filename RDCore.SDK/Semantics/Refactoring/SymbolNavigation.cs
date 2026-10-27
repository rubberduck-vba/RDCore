using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;

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
/// Navigates from a name to what it refers to (<strong>LSP 3.17</strong> §Goto Definition Request, §Goto Declaration Request): the name is bound the way a
/// rename binds it (<see cref="SymbolRename"/>), so that what a navigation finds is what a rename of the name would rename.
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

    /// <summary>
    /// Where the symbol named at <paramref name="position"/> of <paramref name="module"/> is declared (<strong>LSP 3.17</strong> §Goto Declaration Request):
    /// the one place that declares it first, whichever of its definitions are compiled.
    /// </summary>
    /// <param name="context">The workspace.</param>
    /// <param name="module">The module.</param>
    /// <param name="position">A position in the name, or at its end.</param>
    /// <returns>
    /// The name and the place that declares its symbol, as the one site of <see cref="SymbolNavigationTarget.Sites"/>: the first written, of every branch of
    /// an <c>#If</c> and every procedure of a property, so that it is the same whatever branch the precompiler compiles. <see langword="null"/> where no name
    /// is written, or where what the name refers to is not known.
    /// </returns>
    public static SymbolNavigationTarget? DeclarationAt(RenameContext context, Uri module, SourcePosition position)
    {
        if (SymbolAt(context, module, position) is not { } named)
        {
            return null;
        }

        var (origin, symbol) = named;
        IReadOnlyList<SymbolSite> sites = NameOccurrences.IsDeclaredBy(context, symbol)
            && DefinedTogether(context, symbol).SelectMany(Sites).OrderBy(site => site.Site.Range.Start).FirstOrDefault() is { Site: { } first }
            ? [first]
            : [];
        return new SymbolNavigationTarget(origin, symbol, sites);
    }

    /// <summary>
    /// Where the symbol named at <paramref name="position"/> of <paramref name="module"/> is referred to, in every module of the workspace
    /// (<strong>LSP 3.17</strong> §Find References Request): each name that calls it or reads it, each name that assigns it, and each type name that names it.
    /// </summary>
    /// <param name="context">The workspace.</param>
    /// <param name="module">The module.</param>
    /// <param name="position">A position in the name, or at its end.</param>
    /// <param name="includeDeclaration">Whether the names its declarations declare are references too, and the attributes of a member.</param>
    /// <returns>
    /// The name and each place that refers to its symbol, as a site whose range is the name, by module and then in the order written. <see langword="null"/>
    /// where no name is written, or where what the name refers to is not known; a name whose meaning is not known where it is written is not a reference.
    /// </returns>
    /// <remarks>
    /// Each procedure of a property is referred to by the names that call it (<strong>MS-VBAL §5.3.1.7</strong>): its <c>Property Get</c> by those that read
    /// the property, its <c>Property Let</c> by the targets of Let-assignments, and its <c>Property Set</c> by the targets of Set-assignments. The procedure is
    /// the one whose name is at <paramref name="position"/>, or the one the name there calls; a rename renames all of them (<see cref="OccurrencesAt"/>). The
    /// name of a function or a <c>Property Get</c> that its own body assigns, or reads as a simple name, is its result, and refers to no procedure
    /// (<strong>MS-VBAL §5.6.2.1</strong>); with an argument list, it is a recursive call, and a reference.
    /// </remarks>
    public static SymbolNavigationTarget? ReferencesAt(RenameContext context, Uri module, SourcePosition position, bool includeDeclaration = true)
        => NamesOf(context, module, position, includeDeclaration, byAccess: true);

    /// <summary>
    /// Every name that refers to the symbol named at <paramref name="position"/> of <paramref name="module"/>, in every module of the workspace, its
    /// declarations included: what a rename of it renames, and each type name that names it - whichever procedure of a property it calls.
    /// </summary>
    /// <param name="context">The workspace.</param>
    /// <param name="module">The module.</param>
    /// <param name="position">A position in the name, or at its end.</param>
    /// <returns>As <see cref="ReferencesAt"/> answers; the procedures of a property are one symbol.</returns>
    public static SymbolNavigationTarget? OccurrencesAt(RenameContext context, Uri module, SourcePosition position)
        => NamesOf(context, module, position, includeDeclaration: true, byAccess: false);

    private static SymbolNavigationTarget? NamesOf(RenameContext context, Uri module, SourcePosition position, bool includeDeclaration, bool byAccess)
    {
        if (SymbolAt(context, module, position) is not { } named)
        {
            return null;
        }

        var (origin, symbol) = named;
        var together = DefinedTogether(context, symbol).ToList();
        var symbols = together.Select(defined => defined.SemanticId).ToHashSet();
        var isType = symbol is VBModuleSymbol or VBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol;
        var occurrences = NameOccurrences.Find(context, symbol.Name, context.Modules).Occurrences;

        // how each module's names are used, found once per module.
        var assigned = new Dictionary<string, IReadOnlyDictionary<SourcePosition, NameAccess>>(StringComparer.Ordinal);
        NameAccess AccessOf(Uri at, SourcePosition start)
        {
            if (!assigned.TryGetValue(at.AbsoluteUri, out var names))
            {
                names = context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == at.AbsoluteUri) is { } source
                    ? NameOccurrences.AssignedIn(source)
                    : new Dictionary<SourcePosition, NameAccess>();
                assigned[at.AbsoluteUri] = names;
            }

            return names.TryGetValue(start, out var access) ? access : NameAccess.Read;
        }

        // the procedure of a property that is referred to: the one whose name is declared there, or the one the name there calls - its Get, for the result
        // its Get assigns; a property that has no such procedure is referred to by no use.
        NameAccess? called = null;
        SemanticId? accessor = null;
        if (byAccess && AccessorOf(symbol) is { } declared)
        {
            var declares = occurrences.Any(occurrence => occurrence.Kind == OccurrenceKind.Declaration
                && occurrence.Module.AbsoluteUri == module.AbsoluteUri && occurrence.Range == origin);
            called = declares ? declared : AccessOf(module, origin.Start) is var used && used != NameAccess.Result ? used : NameAccess.Read;
            accessor = together.FirstOrDefault(defined => AccessorOf(defined) == called)?.SemanticId;
        }

        var sites = new List<SymbolSite>();
        foreach (var occurrence in occurrences)
        {
            var declaration = occurrence.Kind is OccurrenceKind.Declaration || (byAccess && occurrence.Kind is OccurrenceKind.Attribute);
            if (declaration && !includeDeclaration)
            {
                continue;
            }

            // a name that means no value where it is written is the type it names: a class that is created with New.
            var refers = occurrence.Binding is { } binding
                ? symbols.Contains(binding)
                : isType && occurrence.Scope is { } scope && IsTheType(context, symbol, scope.Uri);
            if (refers && byAccess)
            {
                refers = occurrence.Kind switch
                {
                    OccurrenceKind.Declaration => called is null || occurrence.Binding == accessor,
                    OccurrenceKind.Attribute => true,
                    _ => AccessOf(occurrence.Module, occurrence.Range.Start) is var access && access != NameAccess.Result
                        && (called is null || (accessor is not null && access == called)),
                };
            }

            if (refers)
            {
                sites.Add(new SymbolSite(occurrence.Module, occurrence.Range, occurrence.Range));
            }
        }

        if (isType)
        {
            foreach (var source in context.Modules.Where(candidate => candidate.Source.Contains(symbol.Name, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var (name, range) in NameOccurrences.TypeNamesIn(source))
                {
                    if (string.Equals(name, symbol.Name, StringComparison.OrdinalIgnoreCase)
                        && IsTheType(context, symbol, NameOccurrences.ScopeOf(context, source, range.Start).Uri))
                    {
                        sites.Add(new SymbolSite(source.Module, range, range));
                    }
                }
            }
        }

        return new SymbolNavigationTarget(origin, symbol, [.. sites
            .DistinctBy(site => (site.Module.AbsoluteUri, site.Range))
            .OrderBy(site => site.Module.AbsoluteUri, StringComparer.Ordinal)
            .ThenBy(site => site.Range.Start)]);
    }

    // what calls a procedure of a property: a read calls its Get, a Let-assignment its Let, a Set-assignment its Set; null for anything else.
    private static NameAccess? AccessorOf(Symbol symbol) => symbol switch
    {
        VBPropertyGetMemberSymbol => NameAccess.Read,
        VBPropertyLetMemberSymbol => NameAccess.Let,
        VBPropertySetMemberSymbol => NameAccess.Set,
        _ => null,
    };

    // whether the type a name names where it is written is the symbol.
    private static bool IsTheType(RenameContext context, Symbol type, Uri scope)
        => context.Resolver.ResolveType(type.Name, ScopeKind.Local, scope).Symbol is { } resolved && resolved.SemanticId == type.SemanticId;

    /// <summary>
    /// Where the type of the symbol named at <paramref name="position"/> of <paramref name="module"/> is defined (<strong>LSP 3.17</strong> §Goto Type
    /// Definition Request): the class, <c>Type</c> or <c>Enum</c> a variable, a constant, a parameter or a function is declared as - of an array, its items'.
    /// </summary>
    /// <param name="context">The workspace.</param>
    /// <param name="module">The module.</param>
    /// <param name="position">A position in the name, or at its end.</param>
    /// <returns>
    /// The name and the places that define the type, the type itself for a name that names one; no place for an intrinsic type, or a type of a library.
    /// <see langword="null"/> where no name is written, or where what the name refers to is not known.
    /// </returns>
    public static SymbolNavigationTarget? TypeDefinitionAt(RenameContext context, Uri module, SourcePosition position)
    {
        if (SymbolAt(context, module, position) is not { } named)
        {
            return null;
        }

        var (origin, symbol) = named;
        var type = symbol is VBModuleSymbol or VBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol ? symbol : TypeSymbolOf(symbol);
        IReadOnlyList<SymbolSite> sites = type is not null && NameOccurrences.IsDeclaredBy(context, type) ? Definitions(type) : [];
        return new SymbolNavigationTarget(origin, symbol, sites);
    }

    /// <summary>
    /// Where the symbol named at <paramref name="position"/> of <paramref name="module"/> is implemented (<strong>LSP 3.17</strong> §Goto Implementation
    /// Request): for a class, each class of the workspace that implements it, at its <c>Implements</c> directive; for a member of a class, the procedure of
    /// each of those classes that implements it - <c>IShape_Area</c> for <c>Area</c> of <c>IShape</c> (<strong>MS-VBAL §5.2.4.2</strong>).
    /// </summary>
    /// <param name="context">The workspace.</param>
    /// <param name="module">The module.</param>
    /// <param name="position">A position in the name, or at its end.</param>
    /// <returns>
    /// The name and each place that implements its symbol; no place for a class that no class implements, or a symbol that is no class or member of one.
    /// <see langword="null"/> where no name is written, or where what the name refers to is not known.
    /// </returns>
    public static SymbolNavigationTarget? ImplementationsAt(RenameContext context, Uri module, SourcePosition position)
    {
        if (SymbolAt(context, module, position) is not { } named)
        {
            return null;
        }

        var (origin, symbol) = named;
        var sites = new List<SymbolSite>();
        if (symbol is VBClassModuleSymbol implemented)
        {
            foreach (var implementer in ImplementersOf(context, implemented))
            {
                // the ranges are of the directives, one for each name written; the interfaces also have the one every class implements implicitly.
                var index = implementer.ImplementedInterfaceNames.ToList()
                    .FindIndex(name => string.Equals(name, implemented.Name, StringComparison.OrdinalIgnoreCase));
                var range = index >= 0 && index < implementer.ImplementedInterfaceRanges.Length ? implementer.ImplementedInterfaceRanges[index] : default;
                sites.Add(new SymbolSite(NameOccurrences.ModuleOf(implementer.Uri), range, range));
            }
        }
        else if (symbol is VBTypeMemberSymbol
            && NameOccurrences.SymbolOf(context, new SemanticId(NameOccurrences.ModuleOf(symbol.Uri))) is VBClassModuleSymbol owner)
        {
            foreach (var implementer in ImplementersOf(context, owner))
            {
                foreach (var member in DefinedTogether(context, symbol).OfType<VBTypeMemberSymbol>())
                {
                    if (implementer.FindImplementation(owner, member, AccessOf(member)) is { } implementation)
                    {
                        sites.AddRange(Definitions(implementation));
                    }
                }
            }
        }

        return new SymbolNavigationTarget(origin, symbol, [.. sites
            .DistinctBy(site => (site.Module.AbsoluteUri, site.Range))
            .OrderBy(site => site.Module.AbsoluteUri, StringComparer.Ordinal)
            .ThenBy(site => site.Range.Start)]);
    }

    // the classes of the workspace that implement a class as an interface.
    internal static IEnumerable<VBClassModuleSymbol> ImplementersOf(RenameContext context, VBClassModuleSymbol implemented)
        => context.Modules
            .Select(candidate => NameOccurrences.SymbolOf(context, new SemanticId(candidate.Module)))
            .OfType<VBClassModuleSymbol>()
            .Where(candidate => candidate.ImplementedInterfaces.Any(implementedBy => implementedBy.SemanticId == implemented.SemanticId));

    // which procedure of an implementation implements an accessor of a property: the one of its kind.
    private static ImplementationAccess AccessOf(VBTypeMemberSymbol member) => member switch
    {
        VBPropertyLetMemberSymbol => ImplementationAccess.Let,
        VBPropertySetMemberSymbol => ImplementationAccess.Set,
        _ => ImplementationAccess.Get,
    };

    // the class, Type or Enum a value is declared as - an array's items' - as the type knows it.
    private static Symbol? TypeSymbolOf(Symbol symbol)
    {
        var type = symbol is ITypedSymbol typed ? typed.ResolvedType : null;
        while (type is VBArrayType array)
        {
            type = array.ItemType;
        }

        return type switch
        {
            VBClassType @class => @class.Symbol,
            VBUserDefinedType udt => udt.Symbol,
            VBEnumType @enum => @enum.Symbol,
            _ => null,
        };
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

    /// <summary>
    /// The symbol named at <paramref name="position"/> of <paramref name="module"/>, bound as a rename binds it: a value, or the type a name that means no
    /// value names - a class that is created, a <c>Type</c> a variable is declared as.
    /// </summary>
    /// <param name="context">The workspace.</param>
    /// <param name="module">The module.</param>
    /// <param name="position">A position in the name, or at its end.</param>
    /// <returns>Where the name is written, and its symbol; <see langword="null"/> where no name is written, or where what it refers to is not known.</returns>
    public static (SourceRange Origin, Symbol Symbol)? SymbolAt(RenameContext context, Uri module, SourcePosition position)
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
        var sites = Sites(symbol);
        var compiled = sites.Where(site => site.State != DefinitionState.Dead).ToList();
        return [.. (compiled.Count > 0 ? compiled : sites).Select(site => site.Site)];
    }

    // every site of a symbol, compiled or not, in the order written; a module's is its document.
    private static IReadOnlyList<(SymbolSite Site, DefinitionState State)> Sites(Symbol symbol)
    {
        var declaring = NameOccurrences.ModuleOf(symbol.Uri);
        if (symbol is not WorkspaceSymbol declared)
        {
            return symbol is VBModuleSymbol ? [(new SymbolSite(declaring, default, default), DefinitionState.Live)] : [];
        }

        return [.. declared.AllDefinitions
            .Select(definition => (new SymbolSite(declaring, definition.Range, definition.SelectionRange), definition.State))
            .OrderBy(site => site.Item1.Range.Start)];
    }

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start <= position && position <= range.End;
}
