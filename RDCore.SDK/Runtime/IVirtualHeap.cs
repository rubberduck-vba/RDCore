using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Unbound;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.SDK.Runtime
{
    /// <summary>
    /// A service that resolves an <em>identifier name</em> to a <c>Symbol</c> given a <em>scope</em> <c>Uri</c> 
    /// and that can lookup the value of a symbol in the current context.
    /// </summary>
    public interface ISymbolResolver
    {
        /// <summary>
        /// Resolves the specified <em>identifier name</em> in the specified scope.
        /// </summary>
        /// <param name="name">The name of the symbol to resolve.</param>
        /// <param name="scope">The memory scope to inspect.</param>
        /// <param name="handle">The <c>Uri</c> of the current scope (procedure) symbol.</param>
        /// <returns><c>null</c> if no symbol could be resolved from the specified <em>handle</em> in the specified <em>scope</em> with the specified <em>name</em>.</returns>
        Symbol? Resolve(string name, ScopeKind scope, Uri handle);
        /// <summary>
        /// Gets the <c>VBTypedValue</c> currently associated with the specified <c>Symbol</c>.
        /// </summary>
        /// <param name="symbol">The <c>Symbol</c> to retrieve the currently associated value for.</param>
        VBTypedValue GetValue(Symbol symbol);

        /// <summary>
        /// Gets the <see cref="VBTypedValue"/> at the specified address value in the runtime <em>memory map</em>.
        /// </summary>
        /// <param name="address">The memory address to read.</param>
        /// <param name="value">The retrieved value, if successful.</param>
        bool TryRead(long address, [NotNullWhen(true)][MaybeNullWhen(false)] out VBTypedValue? value);
    }

    /// <summary>
    /// A service that loads a <c>Symbol</c> into the semantic layer.
    /// </summary>
    /// <remarks>
    /// ⚖️<strong>RDCore</strong> provides an implementation of this interface <strong>licensed under GPLv3</strong>.
    /// </remarks>
    public interface ISymbolProvider
    {
        /// <summary>
        /// Defines the specified new <c>Symbol</c> in the semantic layer (static context), or in the symbol table (runtime context).
        /// </summary>
        /// <param name="symbol">The new <c>Symbol</c> to be semantically defined.</param>
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
        /// Creates a new <c>VBObjectValue</c> for the specified <c>Symbol</c>.
        /// </summary>
        /// <param name="symbol">The <c>ClassModuleSymbol</c> to instantiate.</param>
        /// <remarks>
        /// The <strong>RDCore</strong> implementation is intended to be thread-safe.
        /// </remarks>
        VBObjectValue CreateObject(VBClassModuleSymbol symbol);
        /// <summary>
        /// Associates the specified <c>VBTypedValue</c> value to the specified <c>Symbol</c>.
        /// </summary>
        /// <param name="symbol">The <c>Symbol</c> receiving the assignment.</param>
        /// <param name="value">The <c>VBTypedValue</c> to be assigned.</param>
        /// <remarks>
        /// The <strong>RDCore</strong> implementation is intended to be thread-safe.
        /// </remarks>
        void SetValue(Symbol symbol, VBTypedValue value);
        /// <summary>
        /// Allocates the specified number of bytes (<c>size</c>) under the specified <c>symbolUri</c> at the current memory address pointer.
        /// </summary>
        /// <param name="symbolUri">The <c>Uri</c> associated with this allocated memory space.</param>
        /// <param name="size">The size (bytes) of the allocated memory.</param>
        /// <returns></returns>
        /// <remarks>
        /// The <strong>RDCore</strong> implementation is intended to be thread-safe.
        /// </remarks>
        long Allocate(Uri symbolUri, int size);
        /// <summary>
        /// Allocates the specified <c>VBTypedValue</c> under the specified <c>symbolUri</c> at the current memory address pointer.
        /// </summary>
        /// <param name="symbolUri">The <c>Uri</c> associated with this allocated memory space.</param>
        /// <param name="value">The <c>VBTypedValue</c> to be allocated.</param>
        /// <remarks>
        /// The <strong>RDCore</strong> implementation is intended to be thread-safe.
        /// </remarks>
        long Allocate(Uri symbolUri, VBTypedValue value);
        /// <summary>
        /// Deallocates the memory space held at the specified <c>Uri</c>.
        /// </summary>
        /// <param name="symbolUri">The <c>Uri</c> of the symbol to deallocate.</param>
        void Deallocate(Uri symbolUri);
    }
}
