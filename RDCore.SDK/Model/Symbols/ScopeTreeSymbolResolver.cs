using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// The compile-time <see cref="ISymbolResolver"/>: binds an identifier by walking a
/// <see cref="ScopeTree"/> outward from the scope a lookup originates in — the first scope that
/// declares the name binds it (<strong>MS-VBAL §5.2</strong> name binding,
/// <strong>RD-VBAL §2.3.1.2</strong>). A name declared more than once in a single scope is
/// ambiguous and stays unbound.
/// </summary>
/// <remarks>
/// A name-resolution service only — the value-binding members throw, matching the intent of a
/// design-time resolver that holds no run-time bindings. Ordering referenced projects and libraries
/// by their <c>.rdproj</c> priority within the global scope, and reporting an ambiguous name as a
/// coded compile-time error rather than an unbound result, are later work.
/// </remarks>
/// <param name="scopeTree">The tree to resolve against.</param>
public sealed class ScopeTreeSymbolResolver(ScopeTree scopeTree) : ISymbolResolver
{
    /// <summary>
    /// Resolves <paramref name="name"/> as seen from the scope the symbol at <paramref name="handle"/>
    /// belongs to. <paramref name="scope"/> is not consulted — the lookup order is the tree's.
    /// </summary>
    public SymbolResolutionResult Resolve(string name, ScopeKind scope, Uri handle)
    {
        foreach (var lexicalScope in scopeTree.ScopeFor(handle).SelfAndAncestors())
        {
            var matches = lexicalScope.DeclaredAs(name).ToArray();
            if (matches.Length == 1)
            {
                return SymbolResolutionResult.Resolved(matches[0]);
            }

            if (matches.Length > 1)
            {
                if (TryResolvePropertyAccessors(matches, out var property))
                {
                    return SymbolResolutionResult.Resolved(property);
                }

                // a collision inside one module or procedure is a duplicate declaration; one at the
                // project or global tier — members promoted from different modules or references —
                // is an ambiguous name the reference must qualify (VBC09303 vs VBC09301).
                return lexicalScope.Kind is LexicalScopeKind.Project or LexicalScopeKind.Global
                    ? SymbolResolutionResult.Ambiguous(matches)
                    : SymbolResolutionResult.Duplicate(matches);
            }
        }

        return SymbolResolutionResult.Unbound;
    }

    /// <summary>
    /// A property's Get/Let/Set accessors share one declared name by design (MS-VBAL §5.3.1) and are
    /// not a duplicate declaration. Resolves to a single representative accessor — Get when present
    /// (the common read-context lookup), else Let, else Set — when every match is a distinct accessor
    /// kind of the same property. A second accessor of the same kind is still a genuine duplicate.
    /// </summary>
    /// <remarks>
    /// Collapsing to one representative, rather than exposing all matched accessors, is an interim
    /// simplification: <c>Symbol.CreateUri</c> keys purely on name, so Get/Let/Set currently share one
    /// <c>Symbol.Uri</c> and can only be told apart by concrete type — giving each accessor its own
    /// uri suffix is separately-tracked follow-up work.
    /// </remarks>
    private static bool TryResolvePropertyAccessors(Symbol[] matches, [NotNullWhen(true)] out Symbol? property)
    {
        property = null;
        if (matches.Any(symbol => symbol is not IVBPropertyMemberSymbol))
        {
            return false;
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
                    return false;
            }
        }

        property = get as Symbol ?? let as Symbol ?? set as Symbol;
        return property is not null;
    }

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
