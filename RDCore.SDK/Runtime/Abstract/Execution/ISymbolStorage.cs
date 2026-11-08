using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The storage a running session binds its symbols to: the binding currently associated with a symbol, or held at an address of the session's
/// <em>memory map</em>.
/// </summary>
/// <remarks>
/// Storage only: what a name binds to is <see cref="ISymbolResolver"/>'s, and a resolver of a running session is both
/// (<see cref="IRuntimeSymbolResolver"/>).
/// </remarks>
public interface ISymbolStorage
{
    /// <summary>
    /// Gets the <see cref="IBindingHandle"/> currently associated with the specified <see cref="Symbol"/>.
    /// </summary>
    /// <param name="symbol">The <see cref="Symbol"/> to retrieve the currently associated binding for.</param>
    IBindingHandle GetValue(Symbol symbol);

    /// <summary>
    /// Gets the <see cref="IBindingHandle"/> at the specified address in the runtime <em>memory map</em>.
    /// </summary>
    /// <param name="address">The memory address to read.</param>
    /// <param name="value">The retrieved binding, if successful.</param>
    bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value);

    /// <summary>
    /// Gets the <see cref="MemoryAddress"/> currently reserved for <paramref name="symbol"/>, if any:
    /// what a <c>ByRef</c> argument binds to (<strong>MS-VBAL §5.3.1.11</strong>: "a reference parameter
    /// binding... referring to the variable referenced by the argument's expression").
    /// </summary>
    /// <param name="symbol">The <see cref="Symbol"/> whose address to look up.</param>
    /// <param name="address">The reserved address, if <paramref name="symbol"/> is currently allocated.</param>
    bool TryGetAddress(Symbol symbol, out MemoryAddress address);

    /// <summary>
    /// Reserves storage sized for <paramref name="value"/> and binds it to <paramref name="symbol"/>:
    /// a <c>Static</c> local's own first-call allocation (<strong>MS-VBAL §5.4.3.1</strong>: module
    /// extent, so it must persist past its own frame popping) chiefly. This is a storage-only
    /// operation: it never affects name resolution, so it is never how a symbol's name itself becomes
    /// resolvable; that rides on its own declaring symbol instead (a procedure's own
    /// <c>Locals</c>/<c>Parameters</c>), the same way calling this twice for an already-allocated
    /// symbol is safe (a fresh allocation, the previous one freed first) but never necessary.
    /// </summary>
    /// <param name="symbol">The symbol to reserve storage for.</param>
    /// <param name="value">The value to seed the new storage with.</param>
    /// <param name="address">The reserved address, on success.</param>
    /// <returns><c>false</c> if the underlying memory space is exhausted.</returns>
    bool TryAllocate(Symbol symbol, VBTypedValue value, out MemoryAddress address);
}
