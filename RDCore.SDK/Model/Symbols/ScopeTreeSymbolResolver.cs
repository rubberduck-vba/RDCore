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
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// The compile-time <see cref="ISymbolResolver"/>: binds an identifier by walking a
/// <see cref="ScopeTree"/> from the scope a lookup originates in — the first tier that declares the
/// name binds it (<strong>MS-VBAL §5.6.10</strong>, <strong>RD-VBAL §2.3.1.2</strong>). A name declared
/// more than once in a single module or procedure scope resolves as
/// <see cref="SymbolResolutionResult.Duplicate"/>; more than once at the project or global tier
/// resolves as <see cref="SymbolResolutionResult.Ambiguous"/>.
/// </summary>
/// <remarks>
/// <see cref="ResolveValue"/> and <see cref="ResolveType"/> walk the same tree under the two binding
/// contexts <strong>MS-VBAL §5.6.4</strong> distinguishes, each with its own candidates: a user-defined
/// type or a class module is only ever bound by <see cref="ResolveType"/> (or, for a class module and the
/// project, as the qualifier of a qualified type name, by <see cref="ResolveQualifier"/>), and a
/// local, parameter, constant, variable or procedure only ever by <see cref="ResolveValue"/>. A class module that has a
/// predeclared instance (<see cref="VBPredeclaredInstanceSymbol"/>) is also a name in the default binding
/// context — as that instance, a variable of the class's type.
/// <para>
/// A name-resolution service only — the value-binding members throw, matching the intent of a
/// design-time resolver that holds no run-time bindings. Where the candidates of one tier of the project or
/// global scope are of different projects, the workspace's own shadow every library's, and a library shadows
/// one whose reference ranks below it in the project (<see cref="SymbolProperties.LibraryPriority"/>);
/// candidates that still share the top rank are an ambiguous name, a result that carries
/// <see cref="Model.Errors.VBCompileErrorId.AmbiguousName"/> and the candidates rather than an unbound one.
/// </para>
/// </remarks>
/// <param name="scopeTree">The tree to resolve against.</param>
public sealed class ScopeTreeSymbolResolver(ScopeTree scopeTree) : ISymbolResolver
{
    /// <summary>
    /// Resolves <paramref name="name"/> in the default binding context, as seen from the scope the
    /// symbol at <paramref name="handle"/> belongs to. The tiers, in order of precedence
    /// (<strong>MS-VBAL §5.6.10</strong>): the enclosing procedure; the enclosing module; the enclosing
    /// project itself, a referenced project, or a procedural module of the enclosing project; an accessible
    /// member of another procedural module of the project; a procedural module of a referenced project (a
    /// library's); an accessible member of a library's procedural module; then whatever else the global scope
    /// declares, which includes a class module's predeclared instance. Neither a user-defined type nor a class
    /// module is a candidate in any tier. <paramref name="scope"/> is not consulted.
    /// </summary>
    /// <remarks>
    /// The project scope holds the members of the enclosing project's procedural modules and those of every library's
    /// alike, so it is two tiers, the enclosing project's (a symbol of no <see cref="SymbolProperties.Library"/>) and then the
    /// libraries': a library's procedural module ranks between them, after the project's own members and before its own.
    /// <para>
    /// A name is resolved once for each place it is looked up from, and the tree keeps the resolution (<see cref="ScopeTree.ValueResolutions"/>): code that
    /// reads a variable a million times resolves its name once.
    /// </para>
    /// </remarks>
    public SymbolResolutionResult ResolveValue(string name, ScopeKind scope, Uri handle)
        => scopeTree.ValueResolutions.GetOrAdd(new ScopeTree.ValueResolutionKey(name, scope, handle.AbsoluteUri),
            static (_, lookup) => lookup.Resolver.ResolveValueOnce(lookup.Name, lookup.Handle), (Resolver: this, Name: name, Handle: handle));

    private SymbolResolutionResult ResolveValueOnce(string name, Uri handle)
    {
        var origin = scopeTree.ScopeFor(handle).SelfAndAncestors().ToArray();

        // the procedure and the enclosing module: the tree's own order, innermost first.
        foreach (var lexicalScope in origin.TakeWhile(lexicalScope => lexicalScope.Kind is not (LexicalScopeKind.Project or LexicalScopeKind.Global)))
        {
            if (SelectTier(lexicalScope, lexicalScope.DeclaredAs(name).Where(IsValueDeclaration)) is { } result)
            {
                return result;
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
            if (tier is not null && SelectTier(tier, tier.DeclaredAs(name).Where(isCandidate)) is { } result)
            {
                return result;
            }
        }

        return SymbolResolutionResult.Unbound;
    }

    /// <summary>
    /// Resolves <paramref name="name"/> in the type binding context, as seen from the scope the symbol
    /// at <paramref name="handle"/> belongs to. The tiers, in order of precedence
    /// (<strong>MS-VBAL §5.6.4</strong>, <strong>§5.6.10</strong>): a user-defined type or Enum declared at the level of the
    /// enclosing module; then the enclosing project itself, a referenced project, a procedural or class module of the
    /// enclosing project, or a library's class; then an accessible user-defined type or Enum declared in another module of
    /// the project; then a procedural module of a referenced project (a library's); then a user-defined type or Enum of a
    /// library. The procedure scope is never consulted — no local is a type. <paramref name="scope"/> is not consulted.
    /// </summary>
    /// <remarks>
    /// The project scope holds the types of the enclosing project's modules and those of every library's alike, so it is two
    /// tiers, as it is for <see cref="ResolveValue"/>: a library's procedural module ranks between them, after the project's own
    /// types and before the libraries'. A library's class keeps its place among the enclosing project's modules, ahead of the
    /// project's own types, where §5.6.10 ranks it with the library's procedural modules.
    /// </remarks>
    public SymbolResolutionResult ResolveType(string name, ScopeKind scope, Uri handle) => ResolveType(name, handle, _ => true);

    /// <summary>
    /// Resolves the type <paramref name="name"/> that <paramref name="project"/> declares (<strong>MS-VBAL §5.6.12</strong>): the candidates are the project's
    /// own before the name is decided to be ambiguous, so the types of the other projects that share the tier are not in the way.
    /// </summary>
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

    /// <summary>
    /// Resolves <paramref name="name"/> as the qualifier of a qualified type name (the <c>A</c> in <c>A.B</c>), as
    /// seen from the scope the symbol at <paramref name="handle"/> belongs to. A qualifier is a namespace, and neither
    /// a user-defined type nor an Enum type can contain a type: the enclosing module's own types and the types of
    /// the project's other modules are not tiers here, and what is left is the enclosing project itself, or a
    /// procedural or class module in it. <paramref name="scope"/> is not consulted.
    /// </summary>
    public SymbolResolutionResult ResolveQualifier(string name, ScopeKind scope, Uri handle)
    {
        var global = scopeTree.ScopeFor(handle).SelfAndAncestors().FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Global);

        return global is not null && SelectTier(global, global.DeclaredAs(name).Where(IsProjectOrModule)) is { } result
            ? result
            : SymbolResolutionResult.Unbound;
    }

    /// <summary>
    /// Resolves <paramref name="name"/> as a member of <paramref name="owner"/>, a project or a procedural module
    /// (<strong>MS-VBAL §5.6.12</strong>), as seen from the scope the symbol at <paramref name="handle"/> belongs to.
    /// </summary>
    /// <remarks>
    /// A module's members are what its own scope declares, less any that are <c>Private</c> to a lookup that does not
    /// originate inside the module. A project's are, in the specification's order: a project, when the owner is the
    /// enclosing project; a procedural module of the project; then the project tier's one accessible member of that
    /// name — which is a tier of exactly the members "exactly one of the procedural modules" may have, so two modules
    /// declaring the name collide there as an ambiguous name, as they do unqualified. The project tier is shared by
    /// every module of the tree, a library's included, so which project a candidate is in is read off its
    /// <see cref="SymbolProperties.Library"/>.
    /// </remarks>
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
            moduleScope.DeclaredAs(name).Where(candidate => IsValueDeclaration(candidate) && (fromWithin || ScopeTreeBuilder.IsProjectVisible(candidate))))
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
            && SelectTier(projectScope, projectScope.DeclaredAs(name).Where(candidate => IsValueDeclaration(candidate) && InProject(candidate))) is { } member)
        {
            return member;
        }

        return global is not null
            && SelectTier(global, global.DeclaredAs(name).Where(candidate => IsValueDeclaration(candidate) && !IsProjectOrProceduralModule(candidate) && InProject(candidate)))
                is { } instance
                ? instance
                : SymbolResolutionResult.Unbound;
    }

    /// <summary>
    /// Resolves <paramref name="name"/> as a conditional compilation constant. The tiers are §3.4.1's own
    /// shadowing rule: the enclosing module's own <c>#Const</c> declarations, then the project-level ones,
    /// which the global scope carries. <paramref name="scope"/> is not consulted.
    /// </summary>
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

        var module = global.DeclaredAs(moduleScope.Uri.Fragment.TrimStart('#')).OfType<VBClassModuleSymbol>()
            .FirstOrDefault(candidate => candidate.HostClass is not null && candidate.Uri.AbsoluteUri == moduleScope.Uri.AbsoluteUri);
        if (module is null || VBProjectSymbol.ResolveHostClass(this, module) is not { } host)
        {
            return null;
        }

        return SelectTier(moduleScope, host.DefaultInterfaceMembers.Where(member => member is not VBEventMemberSymbol
            && string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)));
    }

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
        // declare means the one that shadows the others: the workspace's declaration shadows every library's, and a library that is referenced later shadows
        // one that is referenced earlier (RD-VBAL §2.3.1.2). Candidates of one rank are still a collision.
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

    // what shadows: a symbol of no library is the workspace's own and outranks any library's; a library's rank is the precedence of its reference.
    private static Symbol[] Shadowing(Symbol[] candidates)
    {
        static int Rank(Symbol symbol) => symbol.GetProperty(SymbolProperties.Library) is null ? int.MaxValue : symbol.GetProperty(SymbolProperties.LibraryPriority);

        var highest = candidates.Max(Rank);
        return [.. candidates.Where(candidate => Rank(candidate) == highest)];
    }

    // MS-VBAL §5.6.10 lists no user-defined type and no class module among the default binding context's
    // candidates: a class is a name there only through its predeclared instance (5.2.4.1.2).
    // MS-VBAL §3.4.1: a conditional compilation constant is accessible to cc-expressions only, so it is
    // never a candidate here however the name ranks - `x = Win64` in ordinary source binds nothing.
    private static bool IsValueDeclaration(Symbol symbol)
        => symbol is not (VBUserDefinedTypeMemberSymbol or VBClassModuleSymbol or PrecompilerConstantSymbol);

    private static bool IsConditionalConstant(Symbol symbol) => symbol is PrecompilerConstantSymbol;

    private static bool IsProjectOrProceduralModule(Symbol symbol) => symbol is VBProjectSymbol or VBStandardModuleSymbol;

    // a library's symbol says which library it is of; the enclosing project's say none, as Shadowing reads them.
    private static bool IsOfALibrary(Symbol symbol) => symbol.GetProperty(SymbolProperties.Library) is not null;

    private static bool IsALibrarysProceduralModule(Symbol symbol) => symbol is VBStandardModuleSymbol && IsOfALibrary(symbol);

    private static bool IsTypeDeclaration(Symbol symbol) => symbol is VBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol;

    private static bool IsProjectOrModule(Symbol symbol) => symbol is VBProjectSymbol or VBModuleSymbol || symbol.Kind is SymbolKindExt.TypeDescriptor;

    /// <summary>
    /// A property's Get/Let/Set accessors share one declared name by design (MS-VBAL §5.3.1) and are
    /// not a duplicate declaration. Resolves to a single representative accessor — Get when present
    /// (the common read-context lookup), else Let, else Set — when every match is a distinct accessor
    /// kind of the same property and their declarations form a valid property
    /// (<see cref="IsConsistentProperty"/>). A second accessor of the same kind is still a genuine
    /// duplicate (<see langword="null"/>, falling through to the caller's own Duplicate/Ambiguous
    /// handling).
    /// </summary>
    /// <remarks>
    /// Collapsing to one representative, rather than exposing all matched accessors, is an interim
    /// simplification: <c>Symbol.CreateUri</c> keys purely on name, so Get/Let/Set currently share one
    /// <c>Symbol.Uri</c> and can only be told apart by concrete type — giving each accessor its own
    /// uri suffix is separately-tracked follow-up work.
    /// </remarks>
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

    /// <inheritdoc/>
    public IBindingHandle GetValue(Symbol symbol)
        => throw new NotSupportedException("The scope-tree resolver binds names only; it holds no run-time bindings.");

    /// <inheritdoc/>
    public bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
    {
        value = null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetAddress(Symbol symbol, out MemoryAddress address)
    {
        address = default;
        return false;
    }

    /// <inheritdoc/>
    public bool TryAllocate(Symbol symbol, VBTypedValue value, out MemoryAddress address)
    {
        address = default;
        return false;
    }
}
