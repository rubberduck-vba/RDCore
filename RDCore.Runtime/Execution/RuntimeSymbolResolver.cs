using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution;

/// <summary>
/// The runtime <see cref="ISymbolResolver"/>: layers real, live value bindings over an inner
/// (compile-time) resolver's name resolution, backed by a session's <see cref="ISessionMemoryAllocator"/>.
/// </summary>
/// <remarks>
/// 👉 <see cref="ISessionMemoryAllocator"/> only tracks allocation size and fragmentation — it does
/// not itself hold values. This class is the missing link: it reserves address space through the
/// allocator, then indexes the caller's own already-bound <see cref="VBTypedValue.Handle"/> by both
/// the declaring symbol's <see cref="SemanticId"/> and the resulting <see cref="MemoryAddress"/>,
/// so <see cref="GetValue"/> and <see cref="TryRead"/> can find it again.
/// </remarks>
/// <param name="names">The compile-time resolver <see cref="Resolve"/> delegates to.</param>
/// <param name="memory">The session's memory allocator.</param>
public sealed class RuntimeSymbolResolver(ISymbolResolver names, ISessionMemoryAllocator memory) : ISymbolResolver
{
    private readonly Dictionary<SemanticId, MemoryAddress> _addressBySymbol = [];
    private readonly Dictionary<MemoryAddress, IBindingHandle> _handleByAddress = [];

    /// <inheritdoc/>
    public SymbolResolutionResult Resolve(string name, ScopeKind scope, Uri handle) => names.Resolve(name, scope, handle);

    /// <inheritdoc/>
    public IBindingHandle GetValue(Symbol symbol)
        => _addressBySymbol.TryGetValue(symbol.SemanticId, out var address) && _handleByAddress.TryGetValue(address, out var handle)
            ? handle
            : throw new KeyNotFoundException($"No runtime binding exists yet for '{symbol.Uri}'.");

    /// <inheritdoc/>
    public bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value)
        => _handleByAddress.TryGetValue(address, out value);

    /// <summary>
    /// Reserves storage sized for <paramref name="value"/> and binds it to <paramref name="symbol"/>,
    /// reachable afterwards through both <see cref="GetValue"/> (by symbol) and <see cref="TryRead"/>
    /// (by <paramref name="address"/>).
    /// </summary>
    /// <returns>
    /// <c>false</c> if the session's memory space is exhausted; the caller is responsible for reporting
    /// this as a coded <c>VBRuntimeErrorId.OutOfMemory</c> runtime error once it has a source location
    /// to attach to it.
    /// </returns>
    public bool TryAllocate(Symbol symbol, VBTypedValue value, out MemoryAddress address)
    {
        if (!memory.TryAllocate(value.Size, out address))
        {
            return false;
        }

        _addressBySymbol[symbol.SemanticId] = address;
        _handleByAddress[address] = value.Handle;

        return true;
    }

    /// <summary>
    /// Frees the storage bound to <paramref name="symbol"/> and removes both bindings.
    /// </summary>
    /// <returns><c>true</c> if a binding for <paramref name="symbol"/> existed and was released.</returns>
    public bool TryDeallocate(Symbol symbol)
    {
        if (_addressBySymbol.Remove(symbol.SemanticId, out var address))
        {
            _handleByAddress.Remove(address);
            return memory.TryDeallocate(address, out _);
        }

        return false;
    }
}
