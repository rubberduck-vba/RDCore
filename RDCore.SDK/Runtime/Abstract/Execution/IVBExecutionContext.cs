namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Represents and encapsulates the execution environment and memory space.
/// </summary>
public interface IVBExecutionContext
{
    /// <summary>
    /// <c>true</c> if the execution context describes a 64-bit environment; <c>false</c> otherwise.
    /// </summary>
    /// <remarks>
    /// This value determines the size of a <c>VBLongPtrValue</c> and is used anywhere needed, e.g. when evaluating precompiler directives.
    /// </remarks>
    bool Is64Bit { get; }

    /// <summary>
    /// Gets the memory space for this context.
    /// </summary>
    IVirtualHeap Memory { get; }
}
