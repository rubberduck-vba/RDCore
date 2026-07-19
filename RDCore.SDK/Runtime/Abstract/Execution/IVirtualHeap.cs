using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Unbound;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// A service that resolves an <em>identifier name</em> to a <c>Symbol</c> given a <em>scope</em> <c>Uri</c> 
/// and that can lookup the value of a symbol in the current context.
/// </summary>
public interface ISymbolResolver
{
    /// <summary>
    /// Resolves the specified <em>identifier name</em> in the specified scope.
    /// </summary>
    /// <param name="name">The name of the <see cref="Symbol"/> to resolve.</param>
    /// <param name="scope">The memory scope to inspect.</param>
    /// <param name="handle">The <see cref="Uri"/> of the current scope (procedure) symbol.</param>
    /// <returns><c>null</c> if no symbol could be resolved from the specified <em>handle</em> in the specified <em>scope</em> with the specified <em>name</em>.</returns>
    Symbol? Resolve(string name, ScopeKind scope, Uri handle);
    /// <summary>
    /// Gets the <see cref="IBindingHandle"/> currently associated with the specified <see cref="Symbol"/>.
    /// </summary>
    /// <param name="symbol">The <see cref="Symbol"/> to retrieve the currently associated <see cref="VBTypedValue"/> for.</param>
    IBindingHandle GetValue(Symbol symbol);

    /// <summary>
    /// Gets the <see cref="IBindingHandle"/> at the specified address value in the runtime <em>memory map</em>.
    /// </summary>
    /// <param name="address">The memory address to read.</param>
    /// <param name="value">The retrieved value, if successful.</param>
    bool TryRead(long address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? value);
}

/// <summary>
/// A service that loads a <see cref="Symbol"/> into the semantic layer.
/// </summary>
/// <remarks>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </remarks>
public interface ISymbolProvider
{
    /// <summary>
    /// Defines the specified new <see cref="Symbol"/> in the semantic layer (static context), or in the symbol table (runtime context).
    /// </summary>
    /// <param name="symbol">The new <see cref="Symbol"/> to be semantically defined.</param>
    void Define(Symbol symbol);
}


/// <summary>
/// A service that manages the run-time memory structure of an execution context.
/// </summary>
/// <remarks>
/// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
/// </remarks>
public interface IVirtualHeap : ISymbolResolver, ISymbolProvider
{
    /// <summary>
    /// Creates a new <see cref="VBObjectValue"/> for the specified <see cref="Symbol"/>.
    /// </summary>
    /// <param name="symbol">The <see cref="VBClassModuleSymbol"/> to instantiate.</param>
    /// <remarks>
    /// The <strong>RDCore</strong> implementation is intended to be thread-safe.
    /// </remarks>
    VBObjectValue CreateObject(VBClassModuleSymbol symbol);
    /// <summary>
    /// Associates the specified <see cref="VBTypedValue"/> value to the specified <see cref="Symbol"/>.
    /// </summary>
    /// <param name="symbol">The <see cref="Symbol"/> receiving the assignment.</param>
    /// <param name="value">The <see cref="VBTypedValue"/> to be assigned.</param>
    /// <remarks>
    /// The <strong>RDCore</strong> implementation is intended to be thread-safe.
    /// </remarks>
    void SetValue(Symbol symbol, IBindingHandle handle);
    /// <summary>
    /// Allocates the specified number of bytes (<c>size</c>) under the specified <c>symbolUri</c> at the <em>current memory address</em> pointer.
    /// </summary>
    /// <param name="symbolUri">The <see cref="Uri"/> associated with this allocated memory space.</param>
    /// <param name="size">The size (bytes) of the allocated memory.</param>
    /// <returns></returns>
    /// <remarks>
    /// The <strong>RDCore</strong> implementation is intended to be thread-safe.
    /// </remarks>
    long Allocate(Uri symbolUri, int size);
    /// <summary>
    /// Allocates the specified <see cref="VBTypedValue"/> under the specified <c>symbolUri</c> at the <em>current memory address</em> pointer.
    /// </summary>
    /// <param name="symbolUri">The <see cref="Uri"/> associated with this allocated memory space.</param>
    /// <param name="value">The <see cref="VBTypedValue"/> to be allocated.</param>
    /// <remarks>
    /// The <strong>RDCore</strong> implementation is intended to be thread-safe.
    /// </remarks>
    long Allocate(Uri symbolUri, VBTypedValue value);
    /// <summary>
    /// Deallocates the memory space held at the specified <see cref="Uri"/>.
    /// </summary>
    /// <param name="symbolUri">The <em>semantic ID</em> of the symbol to deallocate.</param>
    void Deallocate(Uri symbolUri);
}
