using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using System.Collections.Immutable;
using System.Globalization;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;

namespace RDCore.LanguageServer.Services.Refactoring;

public sealed record class RenameRefusal(RenameRefusalKind Kind, string Name, string? Other = null, Uri? Module = null, SourcePosition Position = default)
{
    public string ToMessage(Func<Uri, SourcePosition, string>? locate = null, CultureInfo? culture = null)
        => RefactoringTitles.RenameRefusal(Kind, Name, Other, Module is null ? null : (locate ?? DefaultLocation)(Module, Position), culture);

    private static string DefaultLocation(Uri module, SourcePosition position)
        => $"{SymbolUri.ModuleNameOf(module)} {position.Line + 1}:{position.Character + 1}";
}

// a line label is no symbol: Symbols is empty, and the label is renamed in its procedure.
public sealed record class RenameTarget(string Name, Uri Module, SourceRange Range, ImmutableArray<SemanticId> Symbols, bool IsLabel = false);

// MS-VBAL §6.1.2.8.1.1, §5.6.12
public readonly record struct RenameEdit(Uri Module, SourceRange Range, string NewText, bool IsInString = false, bool IsLateBound = false, bool IsBacked = false);

public sealed record class RenamePreparation(RenameTarget? Target, RenameRefusal? Refusal);

public sealed record class RenamePlan(ImmutableArray<RenameEdit> Edits, RenameRefusal? Refusal)
{
    public bool IsValid => Refusal is null;

    // a module that is renamed, whose file is renamed with it when the file is named after it.
    public Uri? Module { get; init; }

    // the name of the workspace's project that is renamed, which its project file writes.
    public string? Project { get; init; }
}

// a name whose meaning cannot be told is refused, never guessed at; late-bound and in-string edits are flagged for the user to confirm, since only the running
// code knows what they name.
// MS-VBAL §5.6.10, §5.6.12
public static class SymbolRename
{
    public static RenamePreparation Prepare(RenameContext context, Uri module, SourcePosition position)
    {
        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is { } labelled
            && LabelRename.At(labelled, position) is { } label)
        {
            return new(new RenameTarget(label.Name, module, label.Here, [], IsLabel: true), null);
        }

        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } source)
        {
            return new(null, new RenameRefusal(RenameRefusalKind.NoName, string.Empty));
        }

        // the module's own name, in its VB_Name attribute.
        if (NameOccurrences.SymbolOf(context, new SemanticId(module)) is VBModuleSymbol self
            && VbNameRange(context, self) is { } attribute && Contains(attribute.Range, position))
        {
            return PrepareType(context, module, attribute.Range, self);
        }

        // the event a RaiseEvent raises, which it names (MS-VBAL §5.4.2.20).
        if (RaisedEventAt(context, source, position) is { } raised)
        {
            return RefusalOf(context, raised.Event) is { } refused
                ? new(null, refused)
                : new(new RenameTarget(raised.Event.Name, module, raised.Range, [raised.Event.SemanticId]), null);
        }

        if (NameOccurrences.NameAt(source, position) is not { } name)
        {
            // the name of a type in an As clause.
            return NameOccurrences.TypeNameAt(source, position) is { } typeName
                && context.Resolver.ResolveType(typeName.Name, ScopeKind.Local, NameOccurrences.ScopeOf(context, source, position).Uri).Symbol is { } named
                ? PrepareType(context, module, typeName.Range, named)
                : new(null, new RenameRefusal(RenameRefusalKind.NoName, string.Empty));
        }

        var found = NameOccurrences.Find(context, name, [source]);
        if (found.Unknown.Any(occurrence => Contains(occurrence.Range, position)))
        {
            return new(null, new RenameRefusal(RenameRefusalKind.Unresolved, name, Module: module, Position: position));
        }

        var here = found.Occurrences.FirstOrDefault(occurrence => Contains(occurrence.Range, position));
        if (here.Binding is not { } binding || NameOccurrences.SymbolOf(context, binding) is not { } symbol)
        {
            // a name that means no value where it is written can be that of a type: a class that is created, an Enum whose member is qualified by it.
            return here.Scope is { } scope && context.Resolver.ResolveType(name, ScopeKind.Local, scope.Uri).Symbol is { } type
                ? PrepareType(context, module, here.Range, type)
                : new(null, new RenameRefusal(RenameRefusalKind.NotDeclaredByWorkspace, name));
        }

        // the default instance of a class (VB_PredeclaredId, MS-VBAL §5.2.4.1.2) is named by its class: renaming it renames the class.
        if (symbol is VBPredeclaredInstanceSymbol { ClassModule: var predeclaring })
        {
            symbol = NameOccurrences.SymbolOf(context, predeclaring.SemanticId) ?? predeclaring;
        }

        if (IsType(symbol))
        {
            return PrepareType(context, module, here.Range, symbol);
        }

        // a procedure bound to what it implements or handles: the part of its name the position is in names what is renamed.
        if (BoundPartAt(context, module, symbol, here.Range, position) is { } part)
        {
            return part;
        }

        if (RefusalOf(context, symbol) is { } refusal)
        {
            return new(null, refusal);
        }

        return new(new RenameTarget(symbol.Name, module, here.Range, [.. RenamedTogether(context, symbol).Select(renamed => renamed.SemanticId)]), null);
    }

    private static RenamePreparation PrepareType(RenameContext context, Uri module, SourceRange range, Symbol type)
        => RefusalOf(context, type) is { } refusal
            ? new(null, refusal)
            : new(new RenameTarget(type.Name, module, range, [type.SemanticId]), null);

    // MS-VBAL §5.3.1.8, §5.3.1.9: a handler is named for its source and the event it handles, an implementation for its interface and the member it
    // implements. Before the underscore, the name is the source's or the interface's; after it, the event's or the member's.
    private static RenamePreparation? BoundPartAt(RenameContext context, Uri module, Symbol procedure, SourceRange range, SourcePosition position)
    {
        if (!IsProcedure(procedure) || range.Start.Line != range.End.Line
            || NameOccurrences.SymbolOf(context, new SemanticId(procedure.ParentUri)) is not VBClassModuleSymbol owner
            || BindingOf(context, owner, procedure) is not { } binding)
        {
            return null;
        }

        // a source the workspace does not declare (the class of Class_Initialize, a form's own, a document's host, a library's interface), or that its class
        // names rather than its own name: neither part is renamed.
        if (NameOccurrences.SymbolOf(context, binding.Source) is not { } source || !NameOccurrences.IsDeclaredBy(context, source)
            || !IsNamedFor(procedure, source))
        {
            return null;
        }

        var split = source.Name.Length;
        var start = range.Start;

        // after the underscore, the name is the event's or the member's, which is renamed when the workspace declares it. When it does not (a library's
        // event, a control's), the source is the one part of the name the workspace declares, and the rename at either part renames it.
        if (position.Character - start.Character > split && binding.Bound is { } bound && NameOccurrences.SymbolOf(context, bound) is { } member
            && NameOccurrences.IsDeclaredBy(context, member))
        {
            var suffix = new SourceRange(new SourcePosition(start.Line, start.Character + split + 1), range.End);
            return RefusalOf(context, member) is { } refusal
                ? new(null, refusal)
                : new(new RenameTarget(member.Name, module, suffix, [.. RenamedTogether(context, member).Select(renamed => renamed.SemanticId)]), null);
        }

        var prefix = new SourceRange(start, new SourcePosition(start.Line, start.Character + split));
        return source is VBClassModuleSymbol implemented
            ? PrepareType(context, module, prefix, implemented)
            : RefusalOf(context, source) is { } refused ? new(null, refused) : new(new RenameTarget(source.Name, module, prefix, [source.SemanticId]), null);
    }

    // what the members of a class are bound to (MS-VBAL §5.3.1.8, §5.3.1.9, §5.3.1.10): the events of its form, as its designer has them, and of its host,
    // for a document module, included.
    internal static IReadOnlyDictionary<SemanticId, DeclarationBinding> BindingsOf(RenameContext context, VBClassModuleSymbol owner)
    {
        var at = NameOccurrences.ModuleOf(owner.Uri).AbsoluteUri;
        var designer = context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == at)?.Designer;
        return MemberRoles.BindingsOf(owner, context.Resolver, designer, DocumentModuleFact.Of(owner, context.Resolver));
    }

    internal static DeclarationBinding? BindingOf(RenameContext context, VBClassModuleSymbol owner, Symbol procedure)
        => BindingsOf(context, owner).GetValueOrDefault(procedure.SemanticId);

    // the part of a bound procedure's name before the underscore is its source's own name, unless its class names it (Class_Initialize, UserForm_Click).
    private static bool IsNamedFor(Symbol procedure, Symbol source)
        => procedure.Name.Length > source.Name.Length + 1 && procedure.Name[source.Name.Length] == '_'
            && procedure.Name.StartsWith(source.Name, StringComparison.OrdinalIgnoreCase);

    // where a RaiseEvent names the event it raises: the event of its class by that name.
    private static (VBEventMemberSymbol Event, SourceRange Range)? RaisedEventAt(RenameContext context, RenameModule source, SourcePosition position)
    {
        if (NameOccurrences.SymbolOf(context, new SemanticId(source.Module)) is not VBClassModuleSymbol owner)
        {
            return null;
        }

        foreach (var (name, range) in RaisedNames(source))
        {
            if (Contains(range, position) && owner.FindEvent(name) is { } raised)
            {
                return (raised, range);
            }
        }

        return null;
    }

    // the event names the RaiseEvent statements of a module write.
    private static IEnumerable<(string Name, SourceRange Range)> RaisedNames(RenameModule source)
    {
        foreach (var (node, _) in NameOccurrences.Walk(source.Syntax))
        {
            if (node is KeywordStatementNode { Token: Tokens.RaiseEvent, Inputs: [SimpleNameExpressionNode name, ..] })
            {
                var start = name.Location.Range.Start;
                yield return (name.IdentifierName, new SourceRange(start, new SourcePosition(start.Line, start.Character + name.IdentifierName.Length)));
            }
        }
    }

    // a Type and an Enum are module members; a module is the type of its class (MS-VBAL §5.2.3.3, §5.2.3.4, §5.2.4.1).
    private static bool IsType(Symbol symbol) => symbol is VBModuleSymbol or VBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol;

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

        foreach (var start in LabelRename.DefinitionsIn(source, range))
        {
            if (Prepare(context, module, start).Target is { IsLabel: true } label)
            {
                targets.Add(label);
            }
        }

        return [.. targets.OrderBy(target => target.Range.Start)];
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

        if (target.IsLabel)
        {
            return PlanLabel(context, target, newName);
        }

        var targets = target.Symbols.ToHashSet();
        var symbols = target.Symbols.Select(id => NameOccurrences.SymbolOf(context, id)).OfType<Symbol>().ToList();
        if (symbols is [VBProjectSymbol project] && IsOwnProject(project))
        {
            return PlanProject(context, project, newName);
        }

        if (symbols is [VBModuleSymbol module])
        {
            return PlanModule(context, module, newName);
        }

        if (symbols is [var type] && IsType(type))
        {
            return PlanType(context, type, newName);
        }

        var old = NameOccurrences.Find(context, target.Name, context.Modules);
        var raisedNames = RaisedNamesOf(context, symbols).ToList();

        // a name written as the renamed one whose meaning is not known, where the symbol could be what it means.
        var inStrings = new List<NameOccurrence>();
        foreach (var unknown in old.Unknown)
        {
            if (raisedNames.Any(raised => raised.Module.AbsoluteUri == unknown.Module?.AbsoluteUri && raised.Range.Start == unknown.Range.Start))
            {
                continue;
            }

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

        var named = raisedNames.Select(raised => new RenameEdit(raised.Module, raised.Range, newName)).ToList();
        if (BindingEdits(context, symbols, newName, named) is { } taken)
        {
            return Refused(taken);
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
            .Concat(named)
            .DistinctBy(edit => (edit.Module.AbsoluteUri, edit.Range))
            .OrderBy(edit => edit.Module.AbsoluteUri, StringComparer.Ordinal)
            .ThenBy(edit => edit.Range.Start);
        return new([.. edits], null);
    }

    private static RenamePlan Refused(RenameRefusal refusal) => new([], refusal);

    // a type is named where it is declared, in an As clause, and where it qualifies a member of an Enum (MS-VBAL §5.6.16.7): its name is not a value's,
    // and is renamed where it means the type.
    private static RenamePlan PlanType(RenameContext context, Symbol type, string newName)
        => TypeConflictOf(context, type, newName) is { } taken ? Refused(taken) : PlanTypeNames(context, type, newName);

    private static RenamePlan PlanTypeNames(RenameContext context, Symbol type, string newName)
    {
        // a name written as the type's whose meaning is not known, where it could be the type: the rename could miss it.
        if (NameOccurrences.Find(context, type.Name, context.Modules).Unknown
            .FirstOrDefault(unknown => unknown.Kind is OccurrenceKind.Name && unknown.Scope is { } scope && RenameBinding.IsTheType(context, type, scope.Uri))
            is { Module: not null } unresolved)
        {
            return Refused(new(RenameRefusalKind.Unresolved, type.Name, Module: unresolved.Module, Position: unresolved.Range.Start));
        }

        var sites = TypeSites(context, type, type.Name).ToList();
        foreach (var (module, range, scope, isValue) in sites)
        {
            // where the type is named, the new name must mean nothing else: no other type, as near or nearer, and no value before a member of an Enum.
            var meant = context.Resolver.ResolveType(newName, ScopeKind.Local, scope.Uri).Symbol;
            if (meant is not null && meant.SemanticId != type.SemanticId && RenameBinding.ChainIndex(scope, meant) <= RenameBinding.ChainIndex(scope, type))
            {
                return Refused(new(RenameRefusalKind.Conflict, newName, NameOccurrences.Describe(meant), module, range.Start));
            }

            if (isValue && context.Resolver.ResolveValue(newName, ScopeKind.Local, scope.Uri).Symbol is { } value)
            {
                return Refused(new(RenameRefusalKind.Conflict, newName, NameOccurrences.Describe(value), module, range.Start));
            }
        }

        // where the code names a type by the new name already, it must not mean this one once it is renamed.
        foreach (var (module, range, scope, _) in TypeSites(context, type: null, newName))
        {
            var meant = context.Resolver.ResolveType(newName, ScopeKind.Local, scope.Uri).Symbol;
            var nearest = RenameBinding.ChainIndex(scope, type);
            if (nearest < int.MaxValue && (meant is null || nearest <= RenameBinding.ChainIndex(scope, meant)))
            {
                return Refused(new(RenameRefusalKind.Conflict, newName, meant is null ? newName : NameOccurrences.Describe(meant), module, range.Start));
            }
        }

        return new([.. sites
            .DistinctBy(site => (site.Module.AbsoluteUri, site.Range))
            .OrderBy(site => site.Module.AbsoluteUri, StringComparer.Ordinal)
            .ThenBy(site => site.Range.Start)
            .Select(site => new RenameEdit(site.Module, site.Range, newName))], null);
    }

    // where a name is written as a type's: its declaration, an As clause, the qualifier of an Enum's member (a value's place). With no type, every place the
    // name is written as one.
    // a name bound to the default instance of the class, which the class names.
    private static bool IsDefaultInstanceOf(RenameContext context, SemanticId binding, Symbol type)
        => NameOccurrences.SymbolOf(context, binding) is VBPredeclaredInstanceSymbol { ClassModule: var predeclaring } && predeclaring.SemanticId == type.SemanticId;

    private static IEnumerable<(Uri Module, SourceRange Range, LexicalScope Scope, bool IsValue)> TypeSites(RenameContext context, Symbol? type, string name)
    {
        bool Means(LexicalScope scope)
            => type is null
                ? context.Resolver.ResolveType(name, ScopeKind.Local, scope.Uri).Symbol is not null
                : RenameBinding.IsTheType(context, type, scope.Uri);

        foreach (var occurrence in NameOccurrences.Find(context, name, context.Modules).Occurrences)
        {
            if (occurrence.Binding is { } binding)
            {
                if (type is not null && (binding == type.SemanticId || IsDefaultInstanceOf(context, binding, type)))
                {
                    yield return (occurrence.Module, occurrence.Range, occurrence.Scope, occurrence.Kind is OccurrenceKind.Name);
                }
            }
            else if (occurrence.Kind is OccurrenceKind.Name && occurrence.Scope is { } scope && Means(scope))
            {
                yield return (occurrence.Module, occurrence.Range, scope, true);
            }
        }

        foreach (var source in context.Modules.Where(candidate => candidate.Source.Contains(name, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var (written, range) in NameOccurrences.TypeNamesIn(source))
            {
                var scope = NameOccurrences.ScopeOf(context, source, range.Start);
                if (string.Equals(written, name, StringComparison.OrdinalIgnoreCase) && Means(scope))
                {
                    yield return (source.Module, range, scope, false);
                }
            }
        }
    }

    // MS-VBAL §5.2.3.3, §5.2.3.4: a Private Type or Enum is named as no other Type or Enum of its module; a Public one as no Public Type or Enum of its
    // project, no module of it, no project and no library.
    private static RenameRefusal? TypeConflictOf(RenameContext context, Symbol type, string newName)
    {
        var declaring = NameOccurrences.ModuleOf(type.Uri);
        if (context.ScopeTree.TryGetScope(declaring, out var own)
            && own.DeclaredAs(newName).FirstOrDefault(other => IsUserType(other) && other.SemanticId != type.SemanticId) is { } sibling)
        {
            return ConflictWith(newName, sibling);
        }

        if (!IsPublic(type))
        {
            return null;
        }

        var project = ProjectOf(declaring);
        foreach (var module in context.Modules.Where(candidate => ProjectOf(candidate.Module) == project))
        {
            if (string.Equals(SymbolUri.ModuleNameOf(module.Module), newName, StringComparison.OrdinalIgnoreCase))
            {
                return new(RenameRefusalKind.Conflict, newName, newName, module.Module);
            }

            if (context.ScopeTree.TryGetScope(module.Module, out var scope)
                && scope.DeclaredAs(newName).FirstOrDefault(other => IsUserType(other) && IsPublic(other) && other.SemanticId != type.SemanticId) is { } other)
            {
                return ConflictWith(newName, other);
            }
        }

        // the project, or a library it references: each library is a project symbol too, named after the library.
        return own is not null && context.Resolver.ResolveValue(newName, ScopeKind.Local, own.Uri).Symbol is VBProjectSymbol named
            ? ConflictWith(newName, named)
            : null;
    }

    private static bool IsUserType(Symbol symbol) => symbol is VBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol;

    private static bool IsPublic(Symbol symbol) => symbol is VBTypeMemberSymbol { AccessModifier: not AccessModifier.Private };

    private static string ProjectOf(Uri module) => new UriBuilder(module) { Fragment = string.Empty }.Uri.AbsoluteUri;

    // MS-VBAL §4.2
    private const int MaxModuleName = 31;

    // a module is named by its VB_Name attribute (MS-VBAL §4.2), where its name means it, in the Implements directives of the classes that implement it, and
    // in the procedures that implement its members, which are named after it (MS-VBAL §5.3.1.9): only the part before the underscore is renamed.
    private static RenamePlan PlanModule(RenameContext context, VBModuleSymbol module, string newName)
    {
        if (newName.Length > MaxModuleName)
        {
            return Refused(new(RenameRefusalKind.ModuleNameTooLong, newName, MaxModuleName.ToString(CultureInfo.InvariantCulture)));
        }

        if (ModuleConflictOf(context, module, newName) is { } taken)
        {
            return Refused(taken);
        }

        var named = PlanTypeNames(context, module, newName);
        if (!named.IsValid)
        {
            return named;
        }

        // where the code writes the new name as a value already (a variable, a procedure), the module must not become what it means.
        foreach (var occurrence in NameOccurrences.Find(context, newName, context.Modules).Occurrences
            .Where(occurrence => occurrence.Kind is OccurrenceKind.Name && occurrence.Binding is not null))
        {
            var nearest = RenameBinding.ChainIndex(occurrence.Scope, module);
            if (nearest < int.MaxValue && NameOccurrences.SymbolOf(context, occurrence.Binding!.Value) is { } meant
                && nearest <= RenameBinding.ChainIndex(occurrence.Scope, meant))
            {
                return Refused(new(RenameRefusalKind.Conflict, newName, NameOccurrences.Describe(meant), occurrence.Module, occurrence.Range.Start));
            }
        }

        var edits = named.Edits.ToList();
        if (VbNameRange(context, module) is { } attribute)
        {
            edits.Add(new RenameEdit(attribute.Module, attribute.Range, newName));
        }
        else if (VbNameInsertion(context, module, newName) is { } inserted)
        {
            edits.Add(inserted);
        }

        if (DesignerNameRange(context, module) is { } designed)
        {
            edits.Add(new RenameEdit(designed.Module, designed.Range, newName));
        }

        foreach (var (at, range) in ImplementsNames(context, module))
        {
            edits.Add(new RenameEdit(at, range, newName));
        }

        if (module is VBClassModuleSymbol && BindingEdits(context, [module], newName, edits) is { } refusal)
        {
            return Refused(refusal);
        }

        return new([.. edits
            .DistinctBy(edit => (edit.Module.AbsoluteUri, edit.Range))
            .OrderBy(edit => edit.Module.AbsoluteUri, StringComparer.Ordinal)
            .ThenBy(edit => edit.Range.Start)], null) { Module = module.Uri };
    }

    // a procedure bound to what it implements or handles, renamed in part where it is declared and where it is called: the part at offset, of the given
    // length, becomes part. Refused when the class declares the new name already.
    private static RenameRefusal? RenamePart(RenameContext context, VBClassModuleSymbol owner, Symbol procedure, int offset, int length, string part, List<RenameEdit> edits)
    {
        if (!context.ScopeTree.TryGetScope(NameOccurrences.ModuleOf(owner.Uri), out var members))
        {
            return null;
        }

        var accessors = RenameBinding.DefinedTogether(context, procedure).Select(accessor => accessor.SemanticId).ToHashSet();
        var renamed = string.Concat(procedure.Name.AsSpan(0, offset), part, procedure.Name.AsSpan(offset + length));
        if (members.DeclaredAs(renamed).FirstOrDefault(declared => !accessors.Contains(declared.SemanticId)) is { } taken)
        {
            return ConflictWith(renamed, taken);
        }

        foreach (var occurrence in NameOccurrences.Find(context, procedure.Name, context.Modules).Occurrences
            .Where(occurrence => occurrence.Binding is { } binding && accessors.Contains(binding)))
        {
            var start = occurrence.Range.Start;
            edits.Add(new RenameEdit(occurrence.Module, new SourceRange(start.Line, start.Character + offset, start.Line, start.Character + offset + length), part));
        }

        return null;
    }

    // MS-VBAL §5.3.1.8, §5.3.1.9: the procedures bound to a renamed symbol are renamed with it, in the part of their name that is its: after the underscore
    // for the event they handle or the interface member they implement, before it for the WithEvents variable whose event they handle or the interface they
    // implement. A handler that does not match its event is bound to none, and a procedure that is only named like one is bound to nothing: neither is
    // renamed.
    private static RenameRefusal? BindingEdits(RenameContext context, IReadOnlyList<Symbol> symbols, string newName, List<RenameEdit> edits)
    {
        var renamed = symbols.DistinctBy(symbol => symbol.SemanticId).ToDictionary(symbol => symbol.SemanticId);
        foreach (var handling in ClassesOf(context))
        {
            // the accessors of a property are bound each, and renamed together.
            var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in BindingsOf(context, handling).Values.Where(binding => binding.Role is not DeclarationRole.UnmatchedEventHandler))
            {
                if (NameOccurrences.SymbolOf(context, binding.Member) is not { } member || !parts.Add(member.Name))
                {
                    continue;
                }

                if (binding.Bound is { } bound && renamed.TryGetValue(bound, out var handled) && member.Name.EndsWith($"_{handled.Name}", StringComparison.OrdinalIgnoreCase))
                {
                    if (RenamePart(context, handling, member, member.Name.Length - handled.Name.Length, handled.Name.Length, newName, edits) is { } refusal)
                    {
                        return refusal;
                    }
                }
                // a class is the source of its own Initialize and Terminate, which the language names Class_.
                else if (binding.Source != handling.SemanticId && renamed.TryGetValue(binding.Source, out var source) && IsNamedFor(member, source))
                {
                    if (RenamePart(context, handling, member, 0, source.Name.Length, newName, edits) is { } refusal)
                    {
                        return refusal;
                    }
                }
                else
                {
                    parts.Remove(member.Name);
                }
            }
        }

        return null;
    }

    private static IEnumerable<VBClassModuleSymbol> ClassesOf(RenameContext context)
        => context.Modules.Select(candidate => NameOccurrences.SymbolOf(context, new SemanticId(candidate.Module))).OfType<VBClassModuleSymbol>();

    // the names the RaiseEvent statements of its class write, which mean a renamed event (MS-VBAL §5.4.2.20).
    private static IEnumerable<(Uri Module, SourceRange Range)> RaisedNamesOf(RenameContext context, IReadOnlyList<Symbol> symbols)
    {
        foreach (var raised in symbols.OfType<VBEventMemberSymbol>())
        {
            var at = NameOccurrences.ModuleOf(raised.Uri);
            if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == at.AbsoluteUri) is not { } source
                || NameOccurrences.SymbolOf(context, new SemanticId(raised.ParentUri)) is not VBClassModuleSymbol owner)
            {
                continue;
            }

            foreach (var (name, range) in RaisedNames(source))
            {
                if (owner.FindEvent(name)?.SemanticId == raised.SemanticId)
                {
                    yield return (source.Module, range);
                }
            }
        }
    }

    // MS-VBAL §4.2, §5.2.3.3, §5.2.3.4: a module is named as no other module of its project, no project it is in or references, and no Public Type or Enum
    // of its project.
    private static RenameRefusal? ModuleConflictOf(RenameContext context, VBModuleSymbol module, string newName)
    {
        var declaring = NameOccurrences.ModuleOf(module.Uri);
        var project = ProjectOf(declaring);
        foreach (var other in context.Modules.Where(candidate => ProjectOf(candidate.Module) == project && candidate.Module.AbsoluteUri != declaring.AbsoluteUri))
        {
            if (string.Equals(SymbolUri.ModuleNameOf(other.Module), newName, StringComparison.OrdinalIgnoreCase))
            {
                return new(RenameRefusalKind.Conflict, newName, newName, other.Module);
            }

            if (context.ScopeTree.TryGetScope(other.Module, out var scope)
                && scope.DeclaredAs(newName).FirstOrDefault(declared => declared is VBTypeMemberSymbol { AccessModifier: not AccessModifier.Private }
                    and (VBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol)) is { } type)
            {
                return ConflictWith(newName, type);
            }
        }

        // the project, or a library it references: each library is a project symbol too, named after the library.
        var meant = context.ScopeTree.TryGetScope(declaring, out var own) ? context.Resolver.ResolveValue(newName, ScopeKind.Local, own.Uri).Symbol : null;
        if (meant is VBProjectSymbol
            || NameOccurrences.SymbolOf(context, new SemanticId(module.ParentUri)) is VBProjectSymbol { Name: var name } && string.Equals(name, newName, StringComparison.OrdinalIgnoreCase))
        {
            return new(RenameRefusalKind.Conflict, newName, newName, declaring);
        }

        return null;
    }

    // the text of the module a symbol is of, as the rename reads it.
    private static RenameModule? SourceOf(RenameContext context, Symbol module)
    {
        var at = NameOccurrences.ModuleOf(module.Uri);
        return context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == at.AbsoluteUri);
    }

    // where a module's VB_Name attribute writes its name, between the quotes.
    private static (Uri Module, SourceRange Range)? VbNameRange(RenameContext context, Symbol module)
        => SourceOf(context, module) is { } source && ModuleHeader.NameRangeOf(source.Source) is { } range ? (source.Module, range) : null;

    // MS-VBAL §4.2: a module whose source does not name it is named by its file. Its rename writes its VB_Name where its first attribute goes, after
    // its header.
    private static RenameEdit? VbNameInsertion(RenameContext context, Symbol module, string newName)
    {
        if (SourceOf(context, module) is not { } source)
        {
            return null;
        }

        var line = ModuleHeader.AttributesLineOf(source.Source);
        var newLine = source.Source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return new RenameEdit(source.Module, new SourceRange(line, 0, line, 0), $"Attribute VB_Name = \"{newName}\"{newLine}");
    }

    // a form, a user control, a designer: the designer's text names the module too, on its first Begin line (Begin VB.Form Form1,
    // Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} Dialog), before its attributes.
    private static (Uri Module, SourceRange Range)? DesignerNameRange(RenameContext context, Symbol module)
    {
        if (SourceOf(context, module) is not { } source || FormNameRangeOf(source.Source) is not { } range)
        {
            return null;
        }

        var name = source.Source.Split('\n')[range.Start.Line][range.Start.Character..range.End.Character];
        return string.Equals(name, module.Name, StringComparison.OrdinalIgnoreCase) ? (source.Module, range) : null;
    }

    // a designer block that is not terminated names no form a rename can change.
    private static SourceRange? FormNameRangeOf(string source)
    {
        try
        {
            return FormDesigner.FormNameRangeOf(source);
        }
        catch (FormDesignerFormatException)
        {
            return null;
        }
    }

    // the name an Implements directive writes, where it is the module's.
    private static IEnumerable<(Uri Module, SourceRange Range)> ImplementsNames(RenameContext context, Symbol module)
    {
        foreach (var source in context.Modules)
        {
            foreach (var directive in source.Syntax.Children.OfType<ImplementsDirectiveNode>())
            {
                var name = directive.NameExpression switch
                {
                    SimpleNameExpressionNode simple => simple,
                    MemberAccessExpressionNode access => access.Member,
                    _ => null,
                };

                if (name is not null && string.Equals(name.IdentifierName, module.Name, StringComparison.OrdinalIgnoreCase)
                    && RenameBinding.IsTheType(context, module, NameOccurrences.ScopeOf(context, source, directive.Location.Range.Start).Uri))
                {
                    var start = name.Location.Range.Start;
                    yield return (source.Module, new SourceRange(start, new SourcePosition(start.Line, start.Character + name.IdentifierName.Length)));
                }
            }
        }
    }

    // MS-VBAL §5.4.1.1: a label is unique in its procedure, and is never named Randomize.
    private static RenamePlan PlanLabel(RenameContext context, RenameTarget target, string newName)
    {
        if (string.Equals(newName, "Randomize", StringComparison.OrdinalIgnoreCase))
        {
            return Refused(new(RenameRefusalKind.Reserved, newName));
        }

        if (context.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == target.Module.AbsoluteUri) is not { } source
            || LabelRename.At(source, target.Range.Start) is not { } label)
        {
            return Refused(new(RenameRefusalKind.NoName, string.Empty));
        }

        if (label.Others.FirstOrDefault(other => string.Equals(other.Name, newName, StringComparison.OrdinalIgnoreCase)) is { Name: { } taken } conflict)
        {
            return Refused(new(RenameRefusalKind.Conflict, newName, $"{taken}:", target.Module, conflict.At));
        }

        // a name written in brackets stays in brackets.
        return new([.. label.Occurrences.Select(name => new RenameEdit(target.Module, name.Range, name.IsBracketed ? $"[{newName}]" : newName))], null);
    }

    private static bool IsOwnProject(Symbol symbol) => symbol is VBProjectSymbol && symbol.GetProperty(SymbolProperties.Library) is null;

    // MS-VBAL §5.6.12: the workspace's project is renamed where its name qualifies a module or a type (Billing.Tools.Go), and in its project file. The new
    // name is not that of a module of the project, nor of a library it references, which the same qualifier would mean. MS-VBAL §5.2.3.3, §5.2.3.4:
    // nor is it that of a Public Type or Enum of the project or of a library it references.
    private static RenamePlan PlanProject(RenameContext context, Symbol project, string newName)
    {
        if (context.Modules.FirstOrDefault(module => string.Equals(SymbolUri.ModuleNameOf(module.Module), newName, StringComparison.OrdinalIgnoreCase)) is { } named)
        {
            return Refused(new(RenameRefusalKind.Conflict, newName, SymbolUri.ModuleNameOf(named.Module), named.Module, default));
        }

        foreach (var module in context.Modules)
        {
            if (context.ScopeTree.TryGetScope(module.Module, out var scope)
                && scope.DeclaredAs(newName).FirstOrDefault(other => IsUserType(other) && IsPublic(other)) is { } type)
            {
                return Refused(ConflictWith(newName, type));
            }
        }

        if (context.Modules.FirstOrDefault() is { } any)
        {
            if (context.Resolver.ResolveValue(newName, ScopeKind.Local, any.Module).Symbol is VBProjectSymbol library)
            {
                return Refused(new(RenameRefusalKind.Conflict, newName, NameOccurrences.Describe(library), any.Module, default));
            }

            // the type a module of the project means by the name: one of a library it references, the project's own being found above.
            if (context.Resolver.ResolveType(newName, ScopeKind.Local, any.Module).Symbol is { } referenced && IsUserType(referenced) && IsPublic(referenced))
            {
                return Refused(new(RenameRefusalKind.Conflict, newName, NameOccurrences.Describe(referenced), any.Module, default));
            }
        }

        var qualifiers = NameOccurrences.Find(context, project.Name, context.Modules).Occurrences
            .Where(occurrence => occurrence.Binding == project.SemanticId && occurrence.Kind is not OccurrenceKind.Declaration)
            .Select(occurrence => new RenameEdit(occurrence.Module, occurrence.Range, newName));
        return new([.. qualifiers.OrderBy(edit => edit.Module.AbsoluteUri, StringComparer.Ordinal).ThenBy(edit => edit.Range.Start)], null) { Project = project.Name };
    }

    // what makes a symbol one that is not renamed: what the workspace does not declare, a type, and a name that something else is named after.
    private static RenameRefusal? RefusalOf(RenameContext context, Symbol symbol)
    {
        // the workspace's own project is of no library: its project file names it.
        if (IsOwnProject(symbol))
        {
            return null;
        }

        if (!NameOccurrences.IsDeclaredBy(context, symbol))
        {
            return new(RenameRefusalKind.NotDeclaredByWorkspace, symbol.Name);
        }

        var parent = NameOccurrences.SymbolOf(context, new SemanticId(symbol.ParentUri));
        switch (symbol)
        {
            case VBClassModuleSymbol { IsDocument: true }:
                return new(RenameRefusalKind.Type, symbol.Name);
            case VBExternalSubMemberSymbol or VBExternalFunctionMemberSymbol:
                return new(RenameRefusalKind.ExternalProcedure, symbol.Name);
        }

        if (symbol.GetProperty(SymbolProperties.DesignerControl))
        {
            return new(RenameRefusalKind.Control, symbol.Name);
        }

        if (parent is not VBClassModuleSymbol owner || !IsProcedure(symbol))
        {
            return null;
        }

        if (HasUnknownHost(context, owner))
        {
            return new(RenameRefusalKind.UnknownDocumentHost, symbol.Name);
        }

        return BindingOf(context, owner, procedure: symbol) is not null ? new(RenameRefusalKind.BoundHandlerOrImplementation, symbol.Name) : null;
    }

    // what a document module's host calls its procedures for is not known when the host is not, or its class is not resolved: whatever their names, none of
    // its procedures can be told to be bound to nothing.
    private static bool HasUnknownHost(RenameContext context, VBClassModuleSymbol owner)
        => owner.IsDocument && VBProjectSymbol.ResolveHostClass(context.Resolver, owner) is null;

    internal static bool HasUnknownHost(RenameContext context, Symbol procedure)
        => NameOccurrences.SymbolOf(context, new SemanticId(procedure.ParentUri)) is VBClassModuleSymbol owner && HasUnknownHost(context, owner);

    // MS-VBAL §5.3.1.8, §5.3.1.9, §5.3.1.10: a procedure bound to the event it handles or the interface member it implements is called through that
    // binding: the handler of an event of the class itself (Class_Initialize), of its form (UserForm_Click, Form_Load), of its document's host, of a
    // WithEvents variable or a control, and the implementation of a member of an interface. A procedure of a document module whose host is not known may be
    // one. A procedure that is only named like one is a procedure like any other.
    internal static bool IsBound(RenameContext context, VBClassModuleSymbol owner, Symbol procedure)
        => HasUnknownHost(context, owner) || BindingOf(context, owner, procedure) is not null;

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

    internal static bool Contains(SourceRange range, SourcePosition position) => range.Start <= position && position <= range.End;
}
