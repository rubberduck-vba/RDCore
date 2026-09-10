using RDCore.SDK.Model.Symbols.Abstract;
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
    public Symbol? Resolve(string name, ScopeKind scope, Uri handle)
    {
        foreach (var lexicalScope in scopeTree.ScopeFor(handle).SelfAndAncestors())
        {
            var matches = lexicalScope.DeclaredAs(name).Take(2).ToArray();
            if (matches.Length == 1)
            {
                return matches[0];
            }

            if (matches.Length > 1)
            {
                // ambiguous in this scope (MS-VBAL "ambiguous name"). A coded VBCompileErrorInfo
                // needs a richer result than Symbol?, so for now the name stays unbound.
                return null;
            }
        }

        return null;
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
