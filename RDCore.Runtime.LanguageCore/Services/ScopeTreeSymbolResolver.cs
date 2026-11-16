using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Services;

// MS-VBAL §5.6.10, §5.6.4, §4.1; RD-VBAL §2.3.1.2
public sealed class ScopeTreeSymbolResolver(ScopeTree scopeTree) : ISymbolResolver
{
    // what each name means as a value, where it was looked up from, kept for each tree, as long as the tree lives. The resolution of a name is a function of
    // the tree, which does not change once it is built: a symbol table that changes builds another tree, whose resolutions are its own, and begin empty.
    // Nothing is ever taken out, and nothing has to be: what a tree resolved is true of that tree, whichever resolver of it resolved it.
    private static readonly ConditionalWeakTable<ScopeTree, ConcurrentDictionary<ValueResolutionKey, SymbolResolutionResult>> Resolutions = new();

    private readonly ConcurrentDictionary<ValueResolutionKey, SymbolResolutionResult> _valueResolutions = ValueResolutionsOf(scopeTree);

    // the resolutions of a tree, made empty the first time a resolver of it is made.
    internal static ConcurrentDictionary<ValueResolutionKey, SymbolResolutionResult> ValueResolutionsOf(ScopeTree tree) => Resolutions.GetOrCreateValue(tree);

    // a name, and where it was looked up from: a name is the same name in any case (MS-VBAL §3.3.5.1), as the tree declares it.
    internal readonly record struct ValueResolutionKey(string Name, ScopeKind Scope, string Handle)
    {
        public bool Equals(ValueResolutionKey other)
            => Scope == other.Scope && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(Handle, other.Handle, StringComparison.Ordinal);

        public override int GetHashCode() => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Name), Scope, StringComparer.Ordinal.GetHashCode(Handle));
    }

    // MS-VBAL §5.6.10
    // each resolution is the tree's: a variable read a million times is resolved once.
    public SymbolResolutionResult ResolveValue(string name, ScopeKind scope, Uri handle)
        => _valueResolutions.GetOrAdd(new ValueResolutionKey(name, scope, handle.AbsoluteUri),
            static (_, lookup) => lookup.Resolver.ResolveValueOnce(lookup.Name, lookup.Handle), (Resolver: this, Name: name, Handle: handle));

    private SymbolResolutionResult ResolveValueOnce(string name, Uri handle)
    {
        var origin = scopeTree.ScopeFor(handle).SelfAndAncestors().ToArray();

        // the procedure and the enclosing module: the tree's own order, innermost first.
        foreach (var lexicalScope in origin.TakeWhile(lexicalScope => lexicalScope.Kind is not (LexicalScopeKind.Project or LexicalScopeKind.Global)))
        {
            if (SelectTier(lexicalScope, ValuesOverEnums(lexicalScope.DeclaredAs(name).Where(IsValueDeclaration))) is { } result)
            {
                return result;
            }

            if (ResultVariableOf(lexicalScope, name) is { } function)
            {
                return SymbolResolutionResult.Resolved(function);
            }
        }

        var global = origin.FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Global);

        // a document module's code names the members of its host's class as its own (MS-VBAL §4.2.1), after them: an unqualified Range in a worksheet's.
        if (HostMembersOf(origin, global, name) is { } hostMember)
        {
            return hostMember;
        }

        var project = origin.FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Project);
        (LexicalScope? Tier, Func<Symbol, bool> IsCandidate)[] tiers =
        [
            (global, symbol => symbol is VBProjectSymbol || symbol is VBStandardModuleSymbol && !IsOfALibrary(symbol)),
            (project, symbol => IsValueDeclaration(symbol) && !IsOfALibrary(symbol)),
            (global, symbol => symbol is VBStandardModuleSymbol && IsOfALibrary(symbol)),
            (project, symbol => IsValueDeclaration(symbol) && IsOfALibrary(symbol)),
            (global, symbol => IsValueDeclaration(symbol) && !IsProjectOrProceduralModule(symbol)),
        ];

        foreach (var (tier, isCandidate) in tiers)
        {
            if (tier is not null && SelectTier(tier, ValuesOverEnumsOfEachModule(tier.DeclaredAs(name).Where(isCandidate))) is { } result)
            {
                return result;
            }
        }

        return SymbolResolutionResult.Unbound;
    }

    // MS-VBAL §5.6.4, §5.6.10
    public SymbolResolutionResult ResolveType(string name, ScopeKind scope, Uri handle) => ResolveType(name, handle, _ => true);

    // MS-VBAL §5.6.12
    public SymbolResolutionResult ResolveProjectType(VBProjectSymbol project, string name)
    {
        var library = project.GetProperty(SymbolProperties.Library) ?? string.Empty;
        return ResolveType(name, project.WorkspaceRoot, symbol => string.Equals(symbol.GetProperty(SymbolProperties.Library) ?? string.Empty, library, StringComparison.Ordinal));
    }

    private SymbolResolutionResult ResolveType(string name, Uri handle, Func<Symbol, bool> isOfProject)
    {
        var origin = scopeTree.ScopeFor(handle).SelfAndAncestors().ToArray();
        var global = origin.FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Global);
        var project = origin.FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Project);
        (LexicalScope? Tier, Func<Symbol, bool> IsCandidate)[] tiers =
        [
            (origin.FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Module), IsTypeDeclaration),
            (global, symbol => IsProjectOrModule(symbol) && !IsALibrarysProceduralModule(symbol)),
            (project, symbol => IsTypeDeclaration(symbol) && !IsOfALibrary(symbol)),
            (global, IsALibrarysProceduralModule),
            (project, symbol => IsTypeDeclaration(symbol) && IsOfALibrary(symbol)),
        ];

        foreach (var (tier, isCandidate) in tiers)
        {
            if (tier is not null && SelectTier(tier, tier.DeclaredAs(name).Where(isCandidate).Where(isOfProject)) is { } result)
            {
                return result;
            }
        }

        return SymbolResolutionResult.Unbound;
    }

    public SymbolResolutionResult ResolveQualifier(string name, ScopeKind scope, Uri handle)
    {
        var global = scopeTree.ScopeFor(handle).SelfAndAncestors().FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Global);

        return global is not null && SelectTier(global, global.DeclaredAs(name).Where(IsProjectOrModule)) is { } result
            ? result
            : SymbolResolutionResult.Unbound;
    }

    // MS-VBAL §5.6.12
    public SymbolResolutionResult ResolveMember(Symbol owner, string name, Uri handle)
    {
        var origin = scopeTree.ScopeFor(handle).SelfAndAncestors().ToArray();
        return owner switch
        {
            VBProjectSymbol project => ResolveProjectMember(project, name, origin),
            VBStandardModuleSymbol module => ResolveModuleMember(module, name, origin),
            _ => SymbolResolutionResult.Unbound,
        };
    }

    private SymbolResolutionResult ResolveModuleMember(VBStandardModuleSymbol module, string name, LexicalScope[] origin)
    {
        var moduleScope = scopeTree.ScopeFor(module.Uri);
        if (moduleScope.Kind != LexicalScopeKind.Module)
        {
            return SymbolResolutionResult.Unbound;
        }

        // a lookup from inside the module sees what is private to it, as an unqualified one does.
        var fromWithin = origin.Any(scope => scope.Kind == LexicalScopeKind.Module && scope.Uri.AbsoluteUri == module.Uri.AbsoluteUri);

        return SelectTier(
            moduleScope,
            ValuesOverEnums(moduleScope.DeclaredAs(name).Where(candidate => IsValueDeclaration(candidate) && (fromWithin || ScopeTreeBuilder.IsProjectVisible(candidate)))))
            ?? SymbolResolutionResult.Unbound;
    }

    private static SymbolResolutionResult ResolveProjectMember(VBProjectSymbol project, string name, LexicalScope[] origin)
    {
        var global = origin.FirstOrDefault(scope => scope.Kind == LexicalScopeKind.Global);
        var projectScope = origin.FirstOrDefault(scope => scope.Kind == LexicalScopeKind.Project);

        // the tier shares its modules between the enclosing project and the library, so a candidate is the project's
        // own only when it names the same library - or, for the enclosing project, none.
        var library = project.GetProperty(SymbolProperties.Library);
        bool InProject(Symbol symbol)
            => string.Equals(symbol.GetProperty(SymbolProperties.Library) ?? string.Empty, library ?? string.Empty, StringComparison.Ordinal);

        // 1. the enclosing project names the projects it can see.
        if (library is null && global is not null
            && SelectTier(global, global.DeclaredAs(name).Where(candidate => candidate is VBProjectSymbol)) is { } referenced)
        {
            return referenced;
        }

        // 2. a procedural module of the project.
        if (global is not null
            && SelectTier(global, global.DeclaredAs(name).Where(candidate => candidate is VBStandardModuleSymbol && InProject(candidate))) is { } module)
        {
            return module;
        }

        // 3. otherwise the member exactly one of its procedural modules has - a class's default instance variable among them, which §5.2.4.1.2 declares
        // as if at module level, under the class's name (MVVM.AppContext.Create), and which the global scope holds as it does for the name unqualified.
        if (projectScope is not null
            && SelectTier(projectScope, ValuesOverEnumsOfEachModule(projectScope.DeclaredAs(name).Where(candidate => IsValueDeclaration(candidate) && InProject(candidate))))
                is { } member)
        {
            return member;
        }

        return global is not null
            && SelectTier(global, global.DeclaredAs(name).Where(candidate => IsValueDeclaration(candidate) && !IsProjectOrProceduralModule(candidate) && InProject(candidate)))
                is { } instance
                ? instance
                : SymbolResolutionResult.Unbound;
    }

    // MS-VBAL §3.4.1
    public SymbolResolutionResult ResolveConditionalConstant(string name, ScopeKind scope, Uri handle)
    {
        var origin = scopeTree.ScopeFor(handle).SelfAndAncestors().ToArray();
        (LexicalScope? Tier, Func<Symbol, bool> IsCandidate)[] tiers =
        [
            (origin.FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Module), IsConditionalConstant),
            (origin.FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Global), IsConditionalConstant),
        ];

        foreach (var (tier, isCandidate) in tiers)
        {
            if (tier is not null && SelectTier(tier, tier.DeclaredAs(name).Where(isCandidate)) is { } result)
            {
                return result;
            }
        }

        return SymbolResolutionResult.Unbound;
    }

    // the members of the host's class named so, when the lookup is from a document module (VBClassModuleSymbol.HostClass): its library's class is found by
    // its names, and its members are not the module's - no scope maps them.
    private SymbolResolutionResult? HostMembersOf(LexicalScope[] origin, LexicalScope? global, string name)
    {
        if (global is null || origin.FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Module) is not { } moduleScope)
        {
            return null;
        }

        var module = global.DeclaredAs(SymbolUri.ModuleNameOf(moduleScope.Uri)).OfType<VBClassModuleSymbol>()
            .FirstOrDefault(candidate => candidate.HostClass is not null && candidate.Uri.AbsoluteUri == moduleScope.Uri.AbsoluteUri);
        if (module is null || VBProjectSymbol.ResolveHostClass(this, module) is not { } host)
        {
            return null;
        }

        return SelectTier(moduleScope, host.DefaultInterfaceMembers.Where(member => member is not VBEventMemberSymbol
            && string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

    // MS-VBAL §5.3.1: a Function declares a local of its own name, its result variable, and a Property Get returns its value the same way, so in the
    // body the name is bound in the procedure's tier, and a namesake of the module (an Enum of the name: `Property Get PlayerType() As PlayerType`) is
    // no candidate. The procedure stands for its result variable, as it does when the name is bound in the module's tier.
    private static Symbol? ResultVariableOf(LexicalScope lexicalScope, string name)
        => lexicalScope is { Kind: LexicalScopeKind.Procedure, Parent: { } module }
            ? module.DeclaredAs(name).FirstOrDefault(declared => declared is VBFunctionMemberSymbol or VBPropertyGetMemberSymbol
                && declared.Uri.AbsoluteUri == lexicalScope.Uri.AbsoluteUri)
            : null;

    // the first tier with at least one candidate is the selected tier (MS-VBAL §5.6.10): null when this
    // tier has none, so the caller moves on to the next one.
    private static SymbolResolutionResult? SelectTier(LexicalScope lexicalScope, IEnumerable<Symbol> candidates)
    {
        var matches = candidates.ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        // at the project and global tiers the candidates are of different projects - the workspace's own and each library's - and a name that several of them
        // declare means the one that shadows the others: the workspace's declaration shadows every library's, the standard library's every referenced library's,
        // and a library that is referenced earlier one that is referenced later (MS-VBAL §4.1, RD-VBAL §2.3.1.3). Candidates of one rank are still a collision.
        if (matches.Length > 1 && lexicalScope.Kind is LexicalScopeKind.Project or LexicalScopeKind.Global)
        {
            matches = Shadowing(matches);
        }

        // checked ahead of the single-match fast path too: a lone Property Let/Set with no parameters
        // at all is invalid on its own (VBC09321), not only when it collides with other accessors.
        if (TryResolvePropertyAccessors(matches) is { } propertyResult)
        {
            return propertyResult;
        }

        if (matches.Length == 1)
        {
            return SymbolResolutionResult.Resolved(matches[0]);
        }

        // a collision inside one module or procedure is a duplicate declaration; one at the
        // project or global tier — members promoted from different modules or references —
        // is an ambiguous name the reference must qualify (VBC09303 vs VBC09301).
        return lexicalScope.Kind is LexicalScopeKind.Project or LexicalScopeKind.Global
            ? SymbolResolutionResult.Ambiguous(matches)
            : SymbolResolutionResult.Duplicate(matches);
    }

    // what shadows, the lowest rank first: a symbol of no library is the workspace's own and outranks any library's; then the standard library's, which comes
    // before every reference (MS-VBAL §4.1: the earlier reference has the higher precedence); then each library by the precedence of its reference; and a symbol
    // of a library that carries no precedence comes after every one that does, so that it never ties with the standard library or the workspace.
    private static Symbol[] Shadowing(Symbol[] candidates)
    {
        static long Rank(Symbol symbol) => symbol.GetProperty(SymbolProperties.Library) switch
        {
            null => -1,
            StandardLibrary.LibraryName => 0,
            _ => symbol.TryGetProperty(SymbolProperties.LibraryPriority, out var priority) ? priority : long.MaxValue,
        };

        var highest = candidates.Min(Rank);
        return [.. candidates.Where(candidate => Rank(candidate) == highest)];
    }

    // the project tier holds the names every module declares: the namesakes of one module are decided among themselves as they are inside it, and two
    // modules' declarations of the name are still an ambiguous name. Seen from another module, an Enum and a Function of one module is the Function, an Enum
    // and a variable the Enum; an Enum and a Function of two modules, ambiguous (observed in Excel). A module is told by its
    // address, and not by a candidate's parent: the parent of an Enum's constant is the Enum.
    private static IEnumerable<Symbol> ValuesOverEnumsOfEachModule(IEnumerable<Symbol> candidates)
        => candidates.GroupBy(candidate => (Root: candidate.Uri.GetLeftPart(UriPartial.Query), Module: SymbolUri.ModuleNameOf(candidate.Uri))).SelectMany(ValuesOverEnums);

    // an Enum and a procedure of one name in one module: in a value the name is the procedure, even where it qualifies a member of the Enum
    // (`Other = PlayerType.ComputerControlled` beside `Property Get PlayerType() As PlayerType` is an invalid qualifier), and the Enum is a type only. An Enum
    // and a variable or a constant of one name: the name is the Enum, whichever is declared first and whatever their access (`V = 5` is "Expected variable or
    // procedure, not enum type"). Observed in Excel.
    private static IEnumerable<Symbol> ValuesOverEnums(IEnumerable<Symbol> candidates)
    {
        var matches = candidates.ToArray();
        if (!matches.Any(candidate => candidate is VBEnumMemberSymbol))
        {
            return matches;
        }

        return matches.Any(IsProcedure)
            ? matches.Where(candidate => candidate is not VBEnumMemberSymbol)
            : matches.Where(candidate => candidate is VBEnumMemberSymbol);
    }

    internal static bool IsProcedure(Symbol symbol) => symbol is VBProcedureMemberSymbol or VBFunctionMemberSymbol or VBPropertyGetMemberSymbol;

    // MS-VBAL §5.6.10 lists no user-defined type, no class module and no event among the default binding context's
    // candidates: a class is a name there only through its predeclared instance (5.2.4.1.2), and an event is only ever raised
    // (RaiseEvent finds it in its class), so an Event and a Sub of one name are no collision (observed in Excel).
    // MS-VBAL §3.4.1: a conditional compilation constant is accessible to cc-expressions only, so it is
    // never a candidate here however the name ranks - `x = Win64` in ordinary source binds nothing.
    private static bool IsValueDeclaration(Symbol symbol)
        => symbol is not (VBUserDefinedTypeMemberSymbol or VBClassModuleSymbol or VBEventMemberSymbol or PrecompilerConstantSymbol);

    private static bool IsConditionalConstant(Symbol symbol) => symbol is PrecompilerConstantSymbol;

    private static bool IsProjectOrProceduralModule(Symbol symbol) => symbol is VBProjectSymbol or VBStandardModuleSymbol;

    // a library's symbol says which library it is of; the enclosing project's say none, as Shadowing reads them.
    private static bool IsOfALibrary(Symbol symbol) => symbol.GetProperty(SymbolProperties.Library) is not null;

    private static bool IsALibrarysProceduralModule(Symbol symbol) => symbol is VBStandardModuleSymbol && IsOfALibrary(symbol);

    private static bool IsTypeDeclaration(Symbol symbol) => symbol is VBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol;

    private static bool IsProjectOrModule(Symbol symbol) => symbol is VBProjectSymbol or VBModuleSymbol || symbol.Kind is SymbolKindExt.TypeDescriptor;

    // MS-VBAL §5.3.1
    // one representative accessor, Get first: Symbol.CreateUri keys on the name, so Get, Let and Set share one Uri.
    private static SymbolResolutionResult? TryResolvePropertyAccessors(Symbol[] matches)
    {
        if (matches.Any(symbol => symbol is not IVBPropertyMemberSymbol))
        {
            return null;
        }

        VBPropertyGetMemberSymbol? get = null;
        VBPropertyLetMemberSymbol? let = null;
        VBPropertySetMemberSymbol? set = null;
        foreach (var symbol in matches)
        {
            switch (symbol)
            {
                case VBPropertyGetMemberSymbol getSymbol when get is null:
                    get = getSymbol;
                    break;
                case VBPropertyLetMemberSymbol letSymbol when let is null:
                    let = letSymbol;
                    break;
                case VBPropertySetMemberSymbol setSymbol when set is null:
                    set = setSymbol;
                    break;
                default:
                    // a second accessor of the same kind (or an unrecognized IVBPropertyMemberSymbol
                    // implementation) is a genuine duplicate declaration, not a multi-accessor property.
                    return null;
            }
        }

        var property = get as Symbol ?? let as Symbol ?? set as Symbol;
        if (property is null)
        {
            return null;
        }

        // MS-VBAL §5.3.1.5: value-param is never bracketed in property-parameters, so it is always
        // mandatory - a Property Let/Set with no parameters at all has none to receive the assigned
        // value. Checked on its own, ahead of IsConsistentProperty, so it fires for a lone accessor too.
        if (let is { Parameters.Length: 0 } || set is { Parameters.Length: 0 })
        {
            return SymbolResolutionResult.ArgumentRequiredForPropertyLetOrSet(matches);
        }

        return IsConsistentProperty(get, let, set)
            ? SymbolResolutionResult.Resolved(property)
            : SymbolResolutionResult.InconsistentPropertyAccessors(matches);
    }

    // MS-VBAL §5.3.1.7: property declarations sharing a name must have equivalent parameter lists -
    // the same number of index parameters, each with the same name, declared type, and parameter
    // mechanism (implicit vs explicit ByRef is not a difference); a property let declaration and a
    // property-get-declaration sharing a name must have the same declared type; a property set
    // declaration's value parameter must be typed Object, Variant, or a named class. The real compiler
    // also rejects an Optional or ParamArray index parameter the moment a property has more than one
    // accessor - only a Get-only property may declare one. That consequence is not spelled out verbatim
    // in the spec text above, but is confirmed by the compiler's own error wording.
    private static bool IsConsistentProperty(VBPropertyGetMemberSymbol? get, VBPropertyLetMemberSymbol? let, VBPropertySetMemberSymbol? set)
    {
        IVBPropertyMemberSymbol?[] accessors = [get, let, set];
        var present = accessors.Where(accessor => accessor is not null).Select(accessor => accessor!).ToArray();
        if (present.Length < 2)
        {
            return true; // a single accessor has nothing to be inconsistent with.
        }

        var indexParameterLists = present.Select(IndexParametersOf).ToArray();
        if (indexParameterLists.Any(parameters => parameters.Any(parameter => parameter.IsOptional || parameter is ParamArrayParameterSymbol)))
        {
            return false;
        }

        for (var i = 1; i < indexParameterLists.Length; i++)
        {
            if (!HaveEquivalentParameters(indexParameterLists[0], indexParameterLists[i]))
            {
                return false;
            }
        }

        if (get is not null && let is not null && !SameDeclaredType(get.ResolvedType, ValueTypeOf(let)))
        {
            return false;
        }

        // VBUnknownType (not yet resolved/modeled) can't be shown to violate this rule either, so it's
        // deferred rather than flagged - same convention as SetCoercionStaticSemantics.IsSetCoercionInvalid.
        return set is null || ValueTypeOf(set) is VBUnknownType or VBObjectType or VBVariantType or VBClassType;
    }

    private static IReadOnlyList<VBParameterSymbol> IndexParametersOf(IVBPropertyMemberSymbol accessor) => accessor switch
    {
        VBPropertyGetMemberSymbol getAccessor => getAccessor.Parameters,
        VBPropertyLetMemberSymbol letAccessor => letAccessor.Parameters.Take(Math.Max(0, letAccessor.Parameters.Length - 1)).ToArray(),
        VBPropertySetMemberSymbol setAccessor => setAccessor.Parameters.Take(Math.Max(0, setAccessor.Parameters.Length - 1)).ToArray(),
        _ => [],
    };

    private static VBType ValueTypeOf(VBPropertyLetMemberSymbol let) => let.Parameters.Length > 0 ? let.Parameters[^1].ResolvedType : VBUnknownType.TypeInfo;

    private static VBType ValueTypeOf(VBPropertySetMemberSymbol set) => set.Parameters.Length > 0 ? set.Parameters[^1].ResolvedType : VBUnknownType.TypeInfo;

    private static bool HaveEquivalentParameters(IReadOnlyList<VBParameterSymbol> a, IReadOnlyList<VBParameterSymbol> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Name, b[i].Name, StringComparison.OrdinalIgnoreCase)
                || !SameDeclaredType(a[i].ResolvedType, b[i].ResolvedType)
                || IsByRef(a[i].ParameterKind) != IsByRef(b[i].ParameterKind))
            {
                return false;
            }
        }

        return true;
    }

    // A class/UDT/Enum type is identified by the symbol that declares it, not its bare Name: two
    // distinct types can share a simple name - different modules of the same project, or two entirely
    // different referenced projects/libraries (Excel.Range vs Word.Range - not workspace types, but the
    // same shape applies to VBProject1.Class1 vs VBProject2.Class1). SemanticId is the safe, already-
    // established identity accessor for this (Uri.AbsoluteUri, ordinal - Uri's own Equals/GetHashCode
    // ignore Fragment, which is where a Symbol's real identity lives). An intrinsic type owns no
    // symbol at all, so there is no such ambiguity to guard against - its Name is canonical there.
    // VBUnknownType (not yet resolved/modeled) on either side can't be shown to be a mismatch, so it's
    // deferred (treated as matching) rather than flagged - same convention as
    // SetCoercionStaticSemantics.IsSetCoercionInvalid.
    private static bool SameDeclaredType(VBType a, VBType b) => (a, b) switch
    {
        (VBUnknownType, _) or (_, VBUnknownType) => true,
        _ => (OwningSymbolOf(a), OwningSymbolOf(b)) switch
        {
            ({ } symbolA, { } symbolB) => symbolA.SemanticId == symbolB.SemanticId,
            (null, null) => string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            _ => false,
        },
    };

    private static Symbol? OwningSymbolOf(VBType type) => type switch
    {
        VBClassType classType => classType.Symbol,
        VBUserDefinedType userDefinedType => userDefinedType.Symbol,
        VBEnumType enumType => enumType.Symbol,
        _ => null,
    };

    private static bool IsByRef(ParameterKind kind) => kind is ParameterKind.ImplicitByRef or ParameterKind.ExplicitByRef;
}
