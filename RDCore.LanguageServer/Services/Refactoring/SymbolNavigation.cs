using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using System.Globalization;
using System.Resources;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Services.Refactoring;

public sealed record class SymbolSite(Uri Module, SourceRange Range, SourceRange SelectionRange);

public sealed record class SymbolNavigationTarget(SourceRange Origin, Symbol Symbol, IReadOnlyList<SymbolSite> Sites)
{
    // RD-VBAL §2.6.1
    public IReadOnlyList<SymbolSite> Unresolved { get; init; } = [];

    private static readonly ResourceManager Messages = new("RDCore.SDK.Semantics.Refactoring.RefactoringMessages", typeof(RefactoringTitles).Assembly);

    public string? IncompleteMessage(Func<Uri, SourcePosition, string>? locate = null, CultureInfo? culture = null)
    {
        if (Unresolved is not [var first, ..])
        {
            return null;
        }

        culture ??= CultureInfo.CurrentUICulture;
        var where = locate?.Invoke(first.Module, first.Range.Start) ?? $"{SymbolUri.ModuleNameOf(first.Module)} {first.Range.Start.Line + 1}:{first.Range.Start.Character + 1}";
        return string.Format(culture, Messages.GetString("References_Incomplete", culture)!, Symbol.Name, where);
    }
}

// a name is bound the way a rename binds it, so a navigation finds what a rename renames.
// an #If branch whose state is not known counts as compiled; a late-bound member refers to no known symbol, and leads nowhere.
public static class SymbolNavigation
{
    public static SymbolNavigationTarget? DefinitionsAt(RenameContext context, Uri module, SourcePosition position)
    {
        if (RenameBinding.SymbolAt(context, module, position) is not { } named)
        {
            return null;
        }

        var (origin, symbol) = named;
        IReadOnlyList<SymbolSite> sites = NameOccurrences.IsDeclaredBy(context, symbol)
            ? [.. RenameBinding.DefinedTogether(context, symbol).SelectMany(Definitions).OrderBy(site => site.Range.Start)]
            : [];
        return new SymbolNavigationTarget(origin, symbol, sites);
    }

    // the first declaration written, of every #If branch and property procedure: the same whatever branch the precompiler compiles.
    public static SymbolNavigationTarget? DeclarationAt(RenameContext context, Uri module, SourcePosition position)
    {
        if (RenameBinding.SymbolAt(context, module, position) is not { } named)
        {
            return null;
        }

        var (origin, symbol) = named;
        IReadOnlyList<SymbolSite> sites = NameOccurrences.IsDeclaredBy(context, symbol)
            && RenameBinding.DefinedTogether(context, symbol).SelectMany(Sites).OrderBy(site => site.Site.Range.Start).FirstOrDefault() is { Site: { } first }
            ? [first]
            : [];
        return new SymbolNavigationTarget(origin, symbol, sites);
    }

    // MS-VBAL §5.3.1.7, §5.6.2.1
    public static SymbolNavigationTarget? ReferencesAt(RenameContext context, Uri module, SourcePosition position, bool includeDeclaration = true)
        => NamesOf(context, module, position, includeDeclaration, byAccess: true);

    public static SymbolNavigationTarget? OccurrencesAt(RenameContext context, Uri module, SourcePosition position)
        => NamesOf(context, module, position, includeDeclaration: true, byAccess: false);

    private static SymbolNavigationTarget? NamesOf(RenameContext context, Uri module, SourcePosition position, bool includeDeclaration, bool byAccess)
    {
        if (RenameBinding.SymbolAt(context, module, position) is not { } named)
        {
            return null;
        }

        var (origin, symbol) = named;
        var together = RenameBinding.DefinedTogether(context, symbol).ToList();
        var symbols = together.Select(defined => defined.SemanticId).ToHashSet();
        var isType = symbol is VBModuleSymbol or VBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol;
        var found = NameOccurrences.Find(context, symbol.Name, context.Modules);
        var occurrences = found.Occurrences;

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
                : isType && occurrence.Scope is { } scope && RenameBinding.IsTheType(context, symbol, scope.Uri);
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
                        && RenameBinding.IsTheType(context, symbol, NameOccurrences.ScopeOf(context, source, range.Start).Uri))
                    {
                        sites.Add(new SymbolSite(source.Module, range, range));
                    }
                }
            }
        }

        // a name written as the symbol's whose meaning is not known, where the symbol could be what it means, as a rename refuses it.
        var unresolved = found.Unknown
            .Where(unknown => unknown.Kind == OccurrenceKind.Member || together.Any(defined => RenameBinding.ChainIndex(unknown.Scope, defined) < int.MaxValue))
            .Select(unknown => new SymbolSite(unknown.Module, unknown.Range, unknown.Range));
        return new SymbolNavigationTarget(origin, symbol, Ordered(sites)) { Unresolved = Ordered(unresolved) };
    }

    private static IReadOnlyList<SymbolSite> Ordered(IEnumerable<SymbolSite> sites)
        => [.. sites
            .DistinctBy(site => (site.Module.AbsoluteUri, site.Range))
            .OrderBy(site => site.Module.AbsoluteUri, StringComparer.Ordinal)
            .ThenBy(site => site.Range.Start)];

    // what calls a procedure of a property: a read calls its Get, a Let-assignment its Let, a Set-assignment its Set; null for anything else.
    private static NameAccess? AccessorOf(Symbol symbol) => symbol switch
    {
        VBPropertyGetMemberSymbol => NameAccess.Read,
        VBPropertyLetMemberSymbol => NameAccess.Let,
        VBPropertySetMemberSymbol => NameAccess.Set,
        _ => null,
    };

    public static SymbolNavigationTarget? TypeDefinitionAt(RenameContext context, Uri module, SourcePosition position)
    {
        if (RenameBinding.SymbolAt(context, module, position) is not { } named)
        {
            return null;
        }

        var (origin, symbol) = named;
        var type = symbol is VBModuleSymbol or VBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol ? symbol : TypeSymbolOf(symbol);
        IReadOnlyList<SymbolSite> sites = type is not null && NameOccurrences.IsDeclaredBy(context, type) ? Definitions(type) : [];
        return new SymbolNavigationTarget(origin, symbol, sites);
    }

    // MS-VBAL §5.2.4.2
    public static SymbolNavigationTarget? ImplementationsAt(RenameContext context, Uri module, SourcePosition position)
    {
        if (RenameBinding.SymbolAt(context, module, position) is not { } named)
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
                foreach (var member in RenameBinding.DefinedTogether(context, symbol).OfType<VBTypeMemberSymbol>())
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
