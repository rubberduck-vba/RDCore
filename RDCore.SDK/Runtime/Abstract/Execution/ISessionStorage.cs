using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Holds the actual <see cref="IBindingHandle"/> bound at each address in a session's memory space.
/// </summary>
/// <remarks>
/// <see cref="ISessionMemoryAllocator"/> deliberately does not cover this: it only accounts for
/// allocation size and fragmentation, never values. An <see cref="ISessionStorage"/> depends on one
/// to reserve address space, then binds the caller's value at the resulting address.
/// </remarks>
/// <remarks>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </remarks>
public interface ISessionStorage
{
    /// <summary>
    /// Reserves <paramref name="size"/> bytes through the underlying <see cref="ISessionMemoryAllocator"/>
    /// and binds <paramref name="handle"/> at the resulting address.
    /// </summary>
    /// <returns><c>false</c> if the underlying memory space is exhausted.</returns>
    bool TryAllocate(int size, IBindingHandle handle, out MemoryAddress address);

    /// <summary>
    /// Gets the <see cref="IBindingHandle"/> bound at <paramref name="address"/>.
    /// </summary>
    bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? handle);

    /// <summary>
    /// Frees the binding and the underlying storage at <paramref name="address"/>.
    /// </summary>
    /// <returns><c>true</c> if a binding existed at <paramref name="address"/> and was released.</returns>
    bool TryDeallocate(MemoryAddress address);
}
