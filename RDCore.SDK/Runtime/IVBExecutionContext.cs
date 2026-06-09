using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;

namespace RDCore.SDK.Runtime;

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

    /// <summary>
    /// Calls the specified <see cref="VBTypeMemberSymbol"/>.
    /// </summary>
    /// <param name="symbol">The <em>member symbol</em> to invoke.</param>
    /// <returns>
    /// A <c>Success</c> <see cref="RuntimeSemanticsEvaluationResult"/> if the call returns a <see cref="VBTypedValue"/>;
    /// an <c>Error</c> result otherwise.
    /// </returns>
    RuntimeSemanticsEvaluationResult Call(VBTypeMemberSymbol symbol);
}
