using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution;

/// <summary>
/// The runtime <see cref="ISymbolResolver"/>: layers real, live value bindings over an inner
/// (compile-time) resolver's name resolution, backed by a session's <see cref="ISessionStorage"/>.
/// </summary>
/// <remarks>
/// 👉 This class owns only the declaration-site mapping from a symbol to the address reserved for
/// it — the address's actual <c>IBindingHandle</c> is <see cref="ISessionStorage"/>'s concern, not
/// this resolver's. <see cref="GetValue"/> and <see cref="TryRead"/> both resolve through it.
/// </remarks>
/// <param name="names">The compile-time resolver <see cref="Resolve"/> delegates to.</param>
/// <param name="storage">The session's value storage.</param>
public sealed class RuntimeSymbolResolver(ISymbolResolver names, ISessionStorage storage) : ISymbolResolver
{
    private readonly Dictionary<SemanticId, MemoryAddress> _addressBySymbol = [];

    /// <inheritdoc/>
    public SymbolResolutionResult Resolve(string name, ScopeKind scope, Uri handle) => names.Resolve(name, scope, handle);

    /// <inheritdoc/>
    public IBindingHandle GetValue(Symbol symbol)
        => _addressBySymbol.TryGetValue(symbol.SemanticId, out var address) && storage.TryRead(address, out var handle)
            ? handle
            : throw new KeyNotFoundException($"No runtime binding exists yet for '{symbol.Uri}'.");

    /// <inheritdoc/>
    public bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
        => storage.TryRead(address, out value);

    /// <summary>
    /// Reserves storage sized for <paramref name="value"/> and binds it to <paramref name="symbol"/>,
    /// reachable afterwards through both <see cref="GetValue"/> (by symbol) and <see cref="TryRead"/>
    /// (by <paramref name="address"/>). A symbol already allocated has its previous storage freed
    /// first, so re-allocating never leaks the old block or leaves it readable with a stale handle.
    /// </summary>
    /// <returns>
    /// <c>false</c> if the session's memory space is exhausted; the caller is responsible for reporting
    /// this as a coded <c>VBRuntimeErrorId.OutOfMemory</c> runtime error once it has a source location
    /// to attach to it.
    /// </returns>
    public bool TryAllocate(Symbol symbol, VBTypedValue value, out MemoryAddress address)
    {
        if (_addressBySymbol.TryGetValue(symbol.SemanticId, out var previous))
        {
            storage.TryDeallocate(previous);
        }

        if (!storage.TryAllocate(value.Size, value.Handle, out address))
        {
            _addressBySymbol.Remove(symbol.SemanticId);
            return false;
        }

        _addressBySymbol[symbol.SemanticId] = address;

        return true;
    }

    /// <summary>
    /// Frees the storage bound to <paramref name="symbol"/> and removes the address mapping.
    /// </summary>
    /// <returns><c>true</c> if a binding for <paramref name="symbol"/> existed and was released.</returns>
    public bool TryDeallocate(Symbol symbol)
        => _addressBySymbol.Remove(symbol.SemanticId, out var address) && storage.TryDeallocate(address);
}
