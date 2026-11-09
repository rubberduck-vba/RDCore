using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Flags;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using System.Globalization;
using System.Resources;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Services.Refactoring;

public enum RenameRefusalKind
{
    NoName,

    NotDeclaredByWorkspace,

    Type,

    // MS-VBAL §5.2.3.1.2, §5.3.1.1
    NamedByConvention,

    Event,

    WithEvents,

    Control,

    ExternalProcedure,

    InterfaceMember,

    Empty,

    NotAnIdentifier,

    Reserved,

    Conflict,

    Unresolved,
}

public sealed record class RenameRefusal(RenameRefusalKind Kind, string Name, string? Other = null, Uri? Module = null, SourcePosition Position = default)
{
    private static readonly ResourceManager Messages = new("RDCore.SDK.Semantics.Refactoring.RefactoringMessages", typeof(RefactoringTitles).Assembly);

    public string ToMessage(Func<Uri, SourcePosition, string>? locate = null, CultureInfo? culture = null)
    {
        var where = Module is null ? string.Empty : (locate ?? DefaultLocation)(Module, Position);
        var format = Messages.GetString($"Rename_{Kind}", culture ?? CultureInfo.CurrentUICulture) ?? Kind.ToString();
        return string.Format(culture ?? CultureInfo.CurrentUICulture, format, Name, Other, where);
    }

    private static string DefaultLocation(Uri module, SourcePosition position)
        => $"{SymbolUri.ModuleNameOf(module)} {position.Line + 1}:{position.Character + 1}";
}

public sealed record class RenameTarget(string Name, Uri Module, SourceRange Range, ImmutableArray<SemanticId> Symbols);

// MS-VBAL §6.1.2.8.1.1, §5.6.12
public readonly record struct RenameEdit(Uri Module, SourceRange Range, string NewText, bool IsInString = false, bool IsLateBound = false, bool IsBacked = false);

public sealed record class RenamePreparation(RenameTarget? Target, RenameRefusal? Refusal);

public sealed record class RenamePlan(ImmutableArray<RenameEdit> Edits, RenameRefusal? Refusal)
{
    public bool IsValid => Refusal is null;
}

// a name whose meaning cannot be told is refused, never guessed at; late-bound and in-string edits are flagged for the user to confirm, since only the running
// code knows what they name.
// MS-VBAL §5.6.10, §5.6.12
public static class SymbolRename
{
    public static RenamePreparation Prepare(RenameContext context, Uri module, SourcePosition position)
    {
        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } source
            || NameOccurrences.NameAt(source, position) is not { } name)
        {
            return new(null, new RenameRefusal(RenameRefusalKind.NoName, string.Empty));
        }

        var found = NameOccurrences.Find(context, name, [source]);
        if (found.Unknown.Any(occurrence => Contains(occurrence.Range, position)))
        {
            return new(null, new RenameRefusal(RenameRefusalKind.Unresolved, name, Module: module, Position: position));
        }

        var here = found.Occurrences.FirstOrDefault(occurrence => Contains(occurrence.Range, position));
        if (here.Binding is not { } binding || NameOccurrences.SymbolOf(context, binding) is not { } symbol)
        {
            // a name that means no value where it is written can be that of a type: a class that is created, a Type a variable is declared as.
            var isType = here.Scope is { } scope && context.Resolver.ResolveType(name, ScopeKind.Local, scope.Uri).Symbol is not null;
            return new(null, new RenameRefusal(isType ? RenameRefusalKind.Type : RenameRefusalKind.NotDeclaredByWorkspace, name));
        }

        if (RefusalOf(context, symbol) is { } refusal)
        {
            return new(null, refusal);
        }

        return new(new RenameTarget(symbol.Name, module, here.Range, [.. RenamedTogether(context, symbol).Select(renamed => renamed.SemanticId)]), null);
    }

    public static IReadOnlyList<RenameTarget> TargetsIn(RenameContext context, Uri module, SourceRange range)
    {
        if (range.IsEmpty)
        {
            return Prepare(context, module, range.Start).Target is { } here ? [here] : [];
        }

        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } source)
        {
            return [];
        }

        var targets = new List<RenameTarget>();
        foreach (var start in NameOccurrences.DeclarationsIn(source, range))
        {
            if (Prepare(context, module, start).Target is { } target && !targets.Any(offered => offered.Symbols.Intersect(target.Symbols).Any()))
            {
                targets.Add(target);
            }
        }

        return targets;
    }

    public static RenamePlan Plan(RenameContext context, RenameTarget target, string newName)
    {
        switch (VBIdentifier.Validate(newName))
        {
            case VBIdentifierValidity.Empty:
                return Refused(new(RenameRefusalKind.Empty, newName));
            case VBIdentifierValidity.NotAnIdentifier:
                return Refused(new(RenameRefusalKind.NotAnIdentifier, newName));
            case VBIdentifierValidity.Reserved:
                return Refused(new(RenameRefusalKind.Reserved, newName));
        }

        if (string.Equals(newName, target.Name, StringComparison.Ordinal))
        {
            return new([], null);
        }

        var targets = target.Symbols.ToHashSet();
        var symbols = target.Symbols.Select(id => NameOccurrences.SymbolOf(context, id)).OfType<Symbol>().ToList();
        var old = NameOccurrences.Find(context, target.Name, context.Modules);

        // a name written as the renamed one whose meaning is not known, where the symbol could be what it means.
        var inStrings = new List<NameOccurrence>();
        foreach (var unknown in old.Unknown)
        {
            if (unknown.Kind == OccurrenceKind.String)
            {
                if (RenameBinding.IsCalledBy(context, symbols, unknown))
                {
                    inStrings.Add(unknown);
                }

                continue;
            }

            if (unknown.Kind == OccurrenceKind.Member || symbols.Any(symbol => RenameBinding.ChainIndex(unknown.Scope, symbol) < int.MaxValue))
            {
                return Refused(new(RenameRefusalKind.Unresolved, target.Name, Module: unknown.Module, Position: unknown.Range.Start));
            }
        }

        var lateBound = old.Occurrences
            .Select(occurrence => (Occurrence: occurrence, Match: RenameBinding.LateBoundMatchOf(context, symbols, occurrence)))
            .Where(site => site.Match != RenameBinding.LateBoundMatch.None)
            .ToList();
        var renamed = old.Occurrences.Where(occurrence => occurrence.Binding is { } binding && targets.Contains(binding)).ToList();
        if (ConflictOf(context, symbols, targets, renamed, newName) is { } conflict)
        {
            return Refused(conflict);
        }

        var edits = renamed
            .DistinctBy(occurrence => (occurrence.Module.AbsoluteUri, occurrence.Range))
            .OrderBy(occurrence => occurrence.Module.AbsoluteUri, StringComparer.Ordinal)
            .ThenBy(occurrence => occurrence.Range.Start)
            .Select(occurrence => new RenameEdit(occurrence.Module, occurrence.Range, newName))
            .Concat(inStrings
                .DistinctBy(occurrence => (occurrence.Module.AbsoluteUri, occurrence.Range))
                .Select(occurrence => new RenameEdit(occurrence.Module, occurrence.Range, newName, IsInString: true)))
            .Concat(lateBound
                .DistinctBy(site => (site.Occurrence.Module.AbsoluteUri, site.Occurrence.Range))
                .Select(site => new RenameEdit(site.Occurrence.Module, site.Occurrence.Range, newName, IsLateBound: true, IsBacked: site.Match == RenameBinding.LateBoundMatch.Backed)))
            .OrderBy(edit => edit.Module.AbsoluteUri, StringComparer.Ordinal)
            .ThenBy(edit => edit.Range.Start);
        return new([.. edits], null);
    }

    private static RenamePlan Refused(RenameRefusal refusal) => new([], refusal);

    // what makes a symbol one that is not renamed: what the workspace does not declare, a type, and a name that something else is named after.
    private static RenameRefusal? RefusalOf(RenameContext context, Symbol symbol)
    {
        if (!NameOccurrences.IsDeclaredBy(context, symbol))
        {
            return new(RenameRefusalKind.NotDeclaredByWorkspace, symbol.Name);
        }

        var parent = NameOccurrences.SymbolOf(context, new SemanticId(symbol.ParentUri));
        switch (symbol)
        {
            case VBModuleSymbol or VBProjectSymbol or VBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol:
                return new(RenameRefusalKind.Type, symbol.Name);
            case VBEventMemberSymbol:
                return new(RenameRefusalKind.Event, symbol.Name);
            case VBExternalSubMemberSymbol or VBExternalFunctionMemberSymbol:
                return new(RenameRefusalKind.ExternalProcedure, symbol.Name);
        }

        if (symbol.GetProperty(SymbolProperties.WithEvents))
        {
            return new(RenameRefusalKind.WithEvents, symbol.Name);
        }

        if (symbol.GetProperty(SymbolProperties.DesignerControl))
        {
            return new(RenameRefusalKind.Control, symbol.Name);
        }

        if (parent is VBClassModuleSymbol owner)
        {
            if (IsProcedure(symbol) && symbol.Name.Contains('_'))
            {
                return new(RenameRefusalKind.NamedByConvention, symbol.Name);
            }

            if (NameOccurrences.ImplementerOf(context, owner) is { } implementer)
            {
                return new(RenameRefusalKind.InterfaceMember, symbol.Name, $"{owner.Name}, {implementer}");
            }
        }

        return null;
    }

    private static bool IsProcedure(Symbol symbol)
        => symbol is VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol;

    private static bool IsProperty(Symbol symbol) => symbol is VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol;

    // the accessors of a property are one name: renaming one renames them all.
    private static IEnumerable<Symbol> RenamedTogether(RenameContext context, Symbol symbol)
    {
        if (!IsProperty(symbol) || !context.ScopeTree.TryGetScope(symbol.ParentUri, out var module))
        {
            return [symbol];
        }

        return module.DeclaredAs(symbol.Name).Where(IsProperty).Prepend(symbol).DistinctBy(accessor => accessor.SemanticId);
    }

    private static RenameRefusal? ConflictOf(RenameContext context, IReadOnlyList<Symbol> symbols, HashSet<SemanticId> targets, IReadOnlyList<NameOccurrence> renamed, string newName)
    {
        // the new name is declared where the symbol is: a member of the module, a local of the procedure, a field of the type.
        foreach (var symbol in symbols)
        {
            if (NameOccurrences.SiblingNamed(context, symbol, newName) is { } sibling && !targets.Contains(sibling.SemanticId))
            {
                return ConflictWith(newName, sibling);
            }
        }

        // where the code refers to the symbol by its name, the new name must not mean something nearer, or as near.
        foreach (var occurrence in renamed.Where(occurrence => occurrence.Kind is OccurrenceKind.Name or OccurrenceKind.Declaration))
        {
            var meant = context.Resolver.ResolveValue(newName, ScopeKind.Local, occurrence.Scope.Uri).Symbol;
            if (meant is not null && !targets.Contains(meant.SemanticId)
                && RenameBinding.ChainIndex(occurrence.Scope, meant) <= symbols.Min(symbol => RenameBinding.ChainIndex(occurrence.Scope, symbol)))
            {
                return ConflictWith(newName, meant, occurrence);
            }
        }

        // where the code writes the new name already, the symbol must not become what it means: not nearer than what it means now, nor as near.
        var current = NameOccurrences.Find(context, newName, context.Modules);
        foreach (var occurrence in current.Occurrences.Where(occurrence => occurrence.Kind == OccurrenceKind.Name && !(occurrence.Binding is { } binding && targets.Contains(binding))).Concat(current.Unknown))
        {
            var nearest = symbols.Min(symbol => RenameBinding.ChainIndex(occurrence.Scope, symbol));
            if (nearest == int.MaxValue)
            {
                continue;
            }

            var meant = occurrence.Binding is { } bound ? NameOccurrences.SymbolOf(context, bound) : null;
            if (meant is null || nearest <= RenameBinding.ChainIndex(occurrence.Scope, meant))
            {
                return new(RenameRefusalKind.Conflict, newName, meant is null ? newName : NameOccurrences.Describe(meant), occurrence.Module, occurrence.Range.Start);
            }
        }

        return null;
    }

    private static RenameRefusal ConflictWith(string newName, Symbol other, NameOccurrence? where = null)
    {
        var at = where?.Module ?? (other is WorkspaceSymbol declared ? NameOccurrences.ModuleOf(other.Uri) : null);
        var position = where?.Range.Start ?? (other as WorkspaceSymbol)?.PrimarySelectionRange.Start ?? default;
        return new(RenameRefusalKind.Conflict, newName, NameOccurrences.Describe(other), at, position);
    }

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start <= position && position <= range.End;
}
