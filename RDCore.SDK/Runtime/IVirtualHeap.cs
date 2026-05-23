using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Unbound;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Runtime;

/// <summary>
/// A service that manages the run-time memory structure of an execution context.
/// </summary>
public interface IVirtualHeap
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
    /// Gets the <c>VBTypedValue</c> currently associated with the specified <c>Symbol</c>.
    /// </summary>
    /// <param name="symbol">The <c>Symbol</c> to retrieve the currently associated value for.</param>
    VBTypedValue GetValue(Symbol symbol);
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
