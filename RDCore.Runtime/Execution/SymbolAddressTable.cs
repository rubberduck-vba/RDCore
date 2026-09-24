using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
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

        if (!storage.TryAllocate(value.Size, FreshBinding(value), out address))
        {
            _addressBySymbol.Remove(symbol.SemanticId);
            return false;
        }

        _addressBySymbol[symbol.SemanticId] = address;
        return true;
    }

    /// <summary>
    /// A caller's <see cref="VBTypedValue.Handle"/> may be a cached, shared instance — a type's
    /// <c>DefaultValue</c> chiefly, reused by every caller that hasn't assigned that variable yet.
    /// Binding it here directly would let a write through THIS address mutate every other allocation
    /// that started from the same shared instance (both <see cref="ValueBindingHandle"/> and
    /// <see cref="ReferenceBindingHandle"/> mutate their value in place). A fresh handle of the same
    /// kind, wrapping the same (value-type) runtime value, gives each address its own independent,
    /// safely-mutable binding; a <see cref="ConstantBindingHandle"/> (or anything else that can never
    /// be mutated) is safe to share as-is.
    /// </summary>
    /// <remarks>
    /// A <see cref="VBArrayValue"/> is location-identified, not value-identified: its real storage is
    /// the element cells on the array object itself, which a scalar <c>IRuntimeValue</c> (an
    /// <c>int</c>, a <see cref="VBRuntimeReference"/>, …) has nowhere to hold. It gets its own fresh
    /// <see cref="ValueBindingHandle"/> boxing a <see cref="VBRuntimeArrayValue"/> around the array
    /// itself, so <see cref="RDCore.SDK.Model.Types.VBArrayType.CreateValue"/> can hand back the very
    /// same instance — cells intact — on every subsequent read.
    /// </remarks>
    private static IBindingHandle FreshBinding(VBTypedValue value) => value switch
    {
        VBArrayValue array => new ValueBindingHandle(new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array))),
        _ => value.Handle switch
        {
            ValueBindingHandle => new ValueBindingHandle(value.Handle.Value),
            ReferenceBindingHandle => new ReferenceBindingHandle((VBRuntimeReference)value.Handle.Value),
            _ => value.Handle,
        }
    };

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
