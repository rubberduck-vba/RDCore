using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// Chains <see cref="ISymbolResolver"/>s: <see cref="Resolve"/> tries each in order and returns the
/// first result that is not <see cref="SymbolResolutionResult.IsUnbound"/> — a bound symbol, or an
/// error result (a duplicate or ambiguous name found by an earlier resolver is not masked by a later
/// fallback). The result is unbound only when every resolver is unbound.
/// </summary>
/// <remarks>
/// A compile-time composite — the value-binding members throw. Used to layer a workspace's
/// <see cref="ScopeTreeSymbolResolver"/> over an intrinsic type-name resolver, most specific first.
/// </remarks>
/// <param name="resolvers">The resolvers to try, in priority order.</param>
public sealed class CompositeSymbolResolver(params ISymbolResolver[] resolvers) : ISymbolResolver
{
    /// <inheritdoc/>
    public SymbolResolutionResult Resolve(string name, ScopeKind scope, Uri handle)
    {
        foreach (var resolver in resolvers)
        {
            var result = resolver.Resolve(name, scope, handle);
            if (!result.IsUnbound)
            {
                return result;
            }
        }

        return SymbolResolutionResult.Unbound;
    }

    /// <inheritdoc/>
    public IBindingHandle GetValue(Symbol symbol)
        => throw new NotSupportedException("The composite symbol resolver binds names only; it holds no run-time bindings.");

    /// <inheritdoc/>
    public bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
    {
        value = null;
        return false;
    }
}
