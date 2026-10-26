using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;
using System.Collections.Immutable;
using System.Globalization;
using System.Resources;

namespace RDCore.SDK.Semantics.Refactoring;

/// <summary>
/// A module of the workspace, as a rename reads it: its syntax tree, and the text the tree was parsed from.
/// </summary>
/// <param name="Module">The address of the module (<c>workspace#Module</c>), as the scope tree has it.</param>
/// <param name="Syntax">The syntax tree of the module.</param>
/// <param name="Source">The text of the module, whose lines the positions of <paramref name="Syntax"/> are positions of.</param>
public sealed record class RenameModule(Uri Module, ModuleNode Syntax, string Source);

/// <summary>
/// What a rename is worked out in: the workspace, composed, and every module of it.
/// </summary>
/// <param name="Resolver">The resolver of the composed workspace, which says what each name means where it is written.</param>
/// <param name="ScopeTree">The scope tree of the composed workspace.</param>
/// <param name="Modules">Every module of the workspace: a name is renamed wherever it refers to the symbol, and nowhere else.</param>
/// <param name="Environment">What the environment the code is compiled for decides, when it is stated.</param>
public sealed record class RenameContext(ISymbolResolver Resolver, ScopeTree ScopeTree, IReadOnlyList<RenameModule> Modules, StaticEnvironment? Environment = null);

/// <summary>
/// Why a name is not renamed.
/// </summary>
public enum RenameRefusalKind
{
    /// <summary>There is no name where the rename was asked for.</summary>
    NoName,

    /// <summary>The name refers to something the workspace does not declare: a member of a library, or nothing at all.</summary>
    NotDeclaredByWorkspace,

    /// <summary>The name is that of a type - a module, a class, a <c>Type</c> or an <c>Enum</c> - which is not renamed yet.</summary>
    Type,

    /// <summary>
    /// The name of a procedure of a class module has an underscore: it is the handler of an event or the implementation of an interface member, named by the
    /// event or the interface (<strong>MS-VBAL §5.2.3.1.2</strong>, §5.3.1.1), and a rename would make it something else.
    /// </summary>
    NamedByConvention,

    /// <summary>The name is that of an event, whose handlers are named after it: an event is not renamed yet.</summary>
    Event,

    /// <summary>The name is that of a variable declared <c>WithEvents</c>, whose handlers are named after it: it is not renamed yet.</summary>
    WithEvents,

    /// <summary>The name is that of a control of a form, which the form's designer declares: a control is not renamed yet.</summary>
    Control,

    /// <summary>The name is that of an external procedure (<c>Declare</c>), which a library names: it is not renamed.</summary>
    ExternalProcedure,

    /// <summary>The name is that of a member of a class that a class implements as an interface: a member of an interface is not renamed yet.</summary>
    InterfaceMember,

    /// <summary>The new name is empty.</summary>
    Empty,

    /// <summary>The new name is not an identifier (<see cref="VBIdentifierValidity.NotAnIdentifier"/>).</summary>
    NotAnIdentifier,

    /// <summary>The new name is a keyword (<see cref="VBIdentifierValidity.Reserved"/>).</summary>
    Reserved,

    /// <summary>The new name already means something where the code would mean the renamed symbol by it, or the other way around.</summary>
    Conflict,

    /// <summary>What a name written as the renamed one refers to is not known: the rename could miss it, or change it when it should not.</summary>
    Unresolved,
}

/// <summary>
/// Why a name is not renamed, and what about.
/// </summary>
/// <param name="Kind">Why.</param>
/// <param name="Name">The name the refusal is about: the one renamed, or the new one.</param>
/// <param name="Other">What else the refusal names: the symbol the new name already means, the class that implements the interface.</param>
/// <param name="Module">The module the refusal points to, when it points to one.</param>
/// <param name="Position">Where in <paramref name="Module"/>.</param>
public sealed record class RenameRefusal(RenameRefusalKind Kind, string Name, string? Other = null, Uri? Module = null, SourcePosition Position = default)
{
    private static readonly ResourceManager Messages = new("RDCore.SDK.Semantics.Refactoring.RefactoringMessages", typeof(RenameRefusal).Assembly);

    /// <summary>
    /// What the refusal says, in the language of the reader.
    /// </summary>
    /// <param name="locate">
    /// How a position is named to the reader: a client knows how it numbers its lines, which need not be the module's - a program of the platform's BASIC
    /// numbers its own. By default, the name of the module and the line and the column, from 1.
    /// </param>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public string ToMessage(Func<Uri, SourcePosition, string>? locate = null, CultureInfo? culture = null)
    {
        var where = Module is null ? string.Empty : (locate ?? DefaultLocation)(Module, Position);
        var format = Messages.GetString($"Rename_{Kind}", culture ?? CultureInfo.CurrentUICulture) ?? Kind.ToString();
        return string.Format(culture ?? CultureInfo.CurrentUICulture, format, Name, Other, where);
    }

    private static string DefaultLocation(Uri module, SourcePosition position)
        => $"{module.Fragment.TrimStart('#')} {position.Line + 1}:{position.Character + 1}";
}

/// <summary>
/// A symbol that is renamed: the name where the rename was asked for, and every symbol the name is.
/// </summary>
/// <param name="Name">The name, as the symbol declares it.</param>
/// <param name="Module">The module the rename was asked for in.</param>
/// <param name="Range">Where the name is written there.</param>
/// <param name="Symbols">
/// The symbols renamed together: one, or each accessor of a property - <c>Get</c>, <c>Let</c> and <c>Set</c> are the one property, under one name.
/// </param>
public sealed record class RenameTarget(string Name, Uri Module, SourceRange Range, ImmutableArray<SemanticId> Symbols);

/// <summary>
/// One name that a rename writes anew.
/// </summary>
/// <param name="Module">The module.</param>
/// <param name="Range">Where the name is written: the name only, never a type-declaration character after it.</param>
/// <param name="NewText">The new name.</param>
public readonly record struct RenameEdit(Uri Module, SourceRange Range, string NewText);

/// <summary>
/// What a rename would rename, where it was asked for (<see cref="SymbolRename.Prepare"/>): the target, or why there is none.
/// </summary>
/// <param name="Target">The symbol, when the name can be renamed.</param>
/// <param name="Refusal">Why it cannot, when it cannot.</param>
public sealed record class RenamePreparation(RenameTarget? Target, RenameRefusal? Refusal);

/// <summary>
/// What a rename writes (<see cref="SymbolRename.Plan"/>): every name it writes anew, or why it writes none.
/// </summary>
/// <param name="Edits">The names, in each module, in the order they are written; empty when the new name is the name.</param>
/// <param name="Refusal">Why nothing is renamed, when it is not.</param>
public sealed record class RenamePlan(ImmutableArray<RenameEdit> Edits, RenameRefusal? Refusal)
{
    /// <summary>Whether the rename can be made.</summary>
    public bool IsValid => Refusal is null;
}

/// <summary>
/// Renames a symbol: the names that refer to it, wherever they are written, and none that do not - which is what makes it a rename and not a search for a text.
/// </summary>
/// <remarks>
/// <para>
/// What a name refers to is what the static semantics resolve it to (<strong>MS-VBAL §5.6.10</strong>, §5.6.12): the names of a procedure's body as the static pass
/// binds them, a member of an object by the declared type of the object, a named argument by the procedure that is called, a member's attribute by the
/// member. A name the pass does not bind is resolved where it is written. A name whose meaning cannot be told is not guessed at: the rename is refused.
/// </para>
/// <para>
/// The new name must be one code can declare (<see cref="VBIdentifier"/>), and must not change what any code means: it must not be declared where the symbol is,
/// it must not mean something nearer than the symbol where the code refers to the symbol, and the symbol must not become nearer than what it means where the
/// code already writes it.
/// </para>
/// <para>
/// A member that code accesses late-bound - on an <c>Object</c> or a <c>Variant</c> - is not known to refer to the symbol, and is not renamed.
/// </para>
/// </remarks>
public static class SymbolRename
{
    /// <summary>
    /// What a rename at <paramref name="position"/> of <paramref name="module"/> would rename (<strong>LSP 3.17</strong> §Prepare Rename Request).
    /// </summary>
    /// <param name="context">The workspace.</param>
    /// <param name="module">The module.</param>
    /// <param name="position">A position in the name, or at its end.</param>
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

    /// <summary>
    /// What renaming <paramref name="target"/> to <paramref name="newName"/> writes (<strong>LSP 3.17</strong> §Rename Request).
    /// </summary>
    /// <param name="context">The workspace.</param>
    /// <param name="target">What is renamed (<see cref="Prepare"/>).</param>
    /// <param name="newName">The new name, without a type-declaration character: a name written with one keeps it.</param>
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
        foreach (var unknown in old.Unknown)
        {
            if (unknown.Kind == OccurrenceKind.Member || symbols.Any(symbol => ChainIndex(unknown.Scope, symbol) < int.MaxValue))
            {
                return Refused(new(RenameRefusalKind.Unresolved, target.Name, Module: unknown.Module, Position: unknown.Range.Start));
            }
        }

        var renamed = old.Occurrences.Where(occurrence => occurrence.Binding is { } binding && targets.Contains(binding)).ToList();
        if (ConflictOf(context, symbols, targets, renamed, newName) is { } conflict)
        {
            return Refused(conflict);
        }

        var edits = renamed
            .DistinctBy(occurrence => (occurrence.Module.AbsoluteUri, occurrence.Range))
            .OrderBy(occurrence => occurrence.Module.AbsoluteUri, StringComparer.Ordinal)
            .ThenBy(occurrence => occurrence.Range.Start)
            .Select(occurrence => new RenameEdit(occurrence.Module, occurrence.Range, newName));
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
                && ChainIndex(occurrence.Scope, meant) <= symbols.Min(symbol => ChainIndex(occurrence.Scope, symbol)))
            {
                return ConflictWith(newName, meant, occurrence);
            }
        }

        // where the code writes the new name already, the symbol must not become what it means: not nearer than what it means now, nor as near.
        var current = NameOccurrences.Find(context, newName, context.Modules);
        foreach (var occurrence in current.Occurrences.Where(occurrence => occurrence.Kind == OccurrenceKind.Name && !(occurrence.Binding is { } binding && targets.Contains(binding))).Concat(current.Unknown))
        {
            var nearest = symbols.Min(symbol => ChainIndex(occurrence.Scope, symbol));
            if (nearest == int.MaxValue)
            {
                continue;
            }

            var meant = occurrence.Binding is { } bound ? NameOccurrences.SymbolOf(context, bound) : null;
            if (meant is null || nearest <= ChainIndex(occurrence.Scope, meant))
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

    // how near a symbol is where a name is written: the index of the first scope, from there outward, that declares it; none when no scope there does.
    internal static int ChainIndex(LexicalScope scope, Symbol symbol)
    {
        var index = 0;
        foreach (var outer in scope.SelfAndAncestors())
        {
            if (outer.DeclaredAs(symbol.Name).Any(declared => declared.SemanticId == symbol.SemanticId))
            {
                return index;
            }

            index++;
        }

        return int.MaxValue;
    }

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start <= position && position <= range.End;
}
