using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Maps a <see cref="Symbol"/> to the <see cref="MemoryAddress"/> an <see cref="ISessionStorage"/>
/// reserved for it, freeing any previous allocation before rebinding so re-allocating the same symbol
/// never leaks its old block. Shared by <see cref="RuntimeSymbolResolver"/> (one instance, session
/// lifetime — module/global symbols) and <see cref="Frames.CallStackFrame"/> (one instance per
/// activation — a procedure's locals and parameters): the address bookkeeping is identical between
/// them, only the owning table's lifetime differs.
/// </summary>
internal sealed class SymbolAddressTable(ISessionStorage storage)
{
    private readonly Dictionary<SemanticId, MemoryAddress> _addressBySymbol = [];

    /// <summary>
    /// The addresses currently tracked by this table.
    /// </summary>
    public IReadOnlyCollection<MemoryAddress> Addresses => _addressBySymbol.Values;

    /// <summary>
    /// Gets the address reserved for <paramref name="symbol"/>, if any.
    /// </summary>
    public bool TryGetAddress(Symbol symbol, out MemoryAddress address)
        => _addressBySymbol.TryGetValue(symbol.SemanticId, out address);

    /// <summary>
    /// Reserves storage sized for <paramref name="value"/> and binds it to <paramref name="symbol"/>.
    /// A symbol already tracked here has its previous storage freed first, so re-allocating never
    /// leaks the old block or leaves it readable with a stale handle.
    /// </summary>
    /// <returns><c>false</c> if the underlying storage is exhausted.</returns>
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
    public bool TryDeallocate(Symbol symbol)
        => _addressBySymbol.Remove(symbol.SemanticId, out var address) && storage.TryDeallocate(address);

    /// <summary>
    /// Gets the <see cref="IBindingHandle"/> currently bound to <paramref name="symbol"/>.
    /// </summary>
    public IBindingHandle GetValue(Symbol symbol)
        => TryGetAddress(symbol, out var address) && storage.TryRead(address, out var handle)
            ? handle
            : throw new KeyNotFoundException($"No runtime binding exists yet for '{symbol.Uri}'.");

    /// <summary>
    /// Gets the <see cref="IBindingHandle"/> currently bound to <paramref name="symbol"/>, if any.
    /// </summary>
    public bool TryRead(Symbol symbol, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
    {
        if (TryGetAddress(symbol, out var address))
        {
            return storage.TryRead(address, out value);
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Frees every address this table tracks and clears the mapping.
    /// </summary>
    public void ReleaseAll()
    {
        foreach (var address in _addressBySymbol.Values)
        {
            storage.TryDeallocate(address);
        }

        _addressBySymbol.Clear();
    }
}
