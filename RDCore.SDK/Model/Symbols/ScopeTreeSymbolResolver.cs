using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
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
/// design-time resolver that holds no run-time bindings. Ordering referenced projects and libraries
/// by their <c>.rdproj</c> priority within the global scope, and reporting an ambiguous name as a
/// coded compile-time error rather than an unbound result, are later work.
/// </para>
/// </remarks>
/// <param name="scopeTree">The tree to resolve against.</param>
public sealed class ScopeTreeSymbolResolver(ScopeTree scopeTree) : ISymbolResolver
{
    /// <summary>
    /// Resolves <paramref name="name"/> in the default binding context, as seen from the scope the
    /// symbol at <paramref name="handle"/> belongs to. The tiers, in order of precedence
    /// (<strong>MS-VBAL §5.6.10</strong>): the enclosing procedure; the enclosing module; the enclosing
    /// project itself, or a procedural module in it; an accessible member of another procedural module of
    /// the project; then whatever else the global scope declares, which includes a class module's
    /// predeclared instance. Neither a user-defined type nor a class module is a candidate in any tier.
    /// <paramref name="scope"/> is not consulted.
    /// </summary>
    public SymbolResolutionResult ResolveValue(string name, ScopeKind scope, Uri handle)
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
        var project = origin.FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Project);
        (LexicalScope? Tier, Func<Symbol, bool> IsCandidate)[] tiers =
        [
            (global, IsProjectOrProceduralModule),
            (project, IsValueDeclaration),
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
    /// (<strong>MS-VBAL §5.6.4</strong>): a user-defined type or Enum declared at the level of the
    /// enclosing module; then the enclosing project itself, or a procedural or class module in it; then
    /// an accessible user-defined type or Enum declared in another module of the project. The procedure
    /// scope is never consulted — no local is a type. <paramref name="scope"/> is not consulted.
    /// </summary>
    public SymbolResolutionResult ResolveType(string name, ScopeKind scope, Uri handle)
    {
        var origin = scopeTree.ScopeFor(handle).SelfAndAncestors().ToArray();
        (LexicalScope? Tier, Func<Symbol, bool> IsCandidate)[] tiers =
        [
            (origin.FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Module), IsTypeDeclaration),
            (origin.FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Global), IsProjectOrModule),
            (origin.FirstOrDefault(lexicalScope => lexicalScope.Kind == LexicalScopeKind.Project), IsTypeDeclaration),
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

    // the first tier with at least one candidate is the selected tier (MS-VBAL §5.6.10): null when this
    // tier has none, so the caller moves on to the next one.
    private static SymbolResolutionResult? SelectTier(LexicalScope lexicalScope, IEnumerable<Symbol> candidates)
    {
        var matches = candidates.ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length == 1)
        {
            return SymbolResolutionResult.Resolved(matches[0]);
        }

        if (TryResolvePropertyAccessors(matches) is { } propertyResult)
        {
            return propertyResult;
        }

        // a collision inside one module or procedure is a duplicate declaration; one at the
        // project or global tier — members promoted from different modules or references —
        // is an ambiguous name the reference must qualify (VBC09303 vs VBC09301).
        return lexicalScope.Kind is LexicalScopeKind.Project or LexicalScopeKind.Global
            ? SymbolResolutionResult.Ambiguous(matches)
            : SymbolResolutionResult.Duplicate(matches);
    }

    // MS-VBAL §5.6.10 lists no user-defined type and no class module among the default binding context's
    // candidates: a class is a name there only through its predeclared instance (5.2.4.1.2).
    private static bool IsValueDeclaration(Symbol symbol) => symbol is not (VBUserDefinedTypeMemberSymbol or VBClassModuleSymbol);

    private static bool IsProjectOrProceduralModule(Symbol symbol) => symbol is VBProjectSymbol or VBStandardModuleSymbol;

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

        return set is null || ValueTypeOf(set) is VBObjectType or VBVariantType or VBClassType;
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

    // Name is the symbolic name of a type as it appears in code - safe to compare this way (unlike a
    // class/UDT type's own record equality, which would walk its Members - see VBClassType.FromClassModule
    // for the same Uri.AbsoluteUri-over-Uri pitfall this sidesteps by comparing a stable string instead).
    private static bool SameDeclaredType(VBType a, VBType b) => string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

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
}
