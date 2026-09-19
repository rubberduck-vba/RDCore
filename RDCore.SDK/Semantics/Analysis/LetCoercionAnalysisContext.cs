using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Semantics.Analysis;

/// <summary>
/// Encapsulates the context of an <c>Analyze</c> operation as the nullable results of successive operations.
/// </summary>
public readonly record struct LetCoercionAnalysisContext
{
    /// <summary>
    /// Encapsulates the context of an <c>Analyze</c> operation as the nullable results of successive operations.
    /// </summary>
    /// <param name="nodeId">The <c>Identity</c> of the associated expression node.</param>
    /// <param name="result">The result of the let-coercion operation.</param>
    /// <param name="flags">The semantic flags associated with this context.</param>
    public LetCoercionAnalysisContext(SyntaxNodeId nodeId, LetCoercionResult result, ConversionSemanticFlags flags)
    {
        NodeId = nodeId;
        Result = result;
        Flags = flags;
    }

    /// <summary>
    /// The <c>Identity</c> of the associated expression node.
    /// </summary>
    public SyntaxNodeId NodeId { get; }

    /// <summary>
    /// The semantic flags associated with this context.
    /// </summary>
    public ConversionSemanticFlags Flags { get; }

    /// <summary>
    /// The result of the let-coercion operation.
    /// </summary>
    public LetCoercionResult Result { get; }

    /// <summary>
    /// Creates a new <em>analysis context</em> for a no-op coercion operation that does not involve a coercion stack frame.
    /// </summary>
    /// <param name="nodeId">The <c>Identity</c> of the node being evaluated.</param>
    /// <param name="nopResult">The result of the no-op operation.</param>
    public LetCoercionAnalysisContext(SyntaxNodeId nodeId, LetCoercionResult nopResult)
        : this(nodeId, nopResult, 0) { }

    /// <summary>
    /// Returns a new <c>LetCoercionAnalysisContext</c> containing the specified evaluation frames and their respective result.
    /// </summary>
    /// <param name="context">The sub-coercion analysis context to merge into this one.</param>
    public LetCoercionAnalysisContext Merge(LetCoercionAnalysisContext context)
        // the merged result is the failure, if either coercion failed - the earlier one first, as it was found first - and
        // otherwise the later one; the frames of both are kept, in order. An operand that is not coerced at all (a Null
        // operand, an operand that already has the effective type) has no frame, and so contributes none.
        => new(context.NodeId,
            (Result.ErrorInfo is not null ? Result : context.Result) with
            {
                // NOTE: .Merge(context) is called within an aggregator stack-traversal enumeration, so the frames
                // accumulated so far are the aggregation root's, and the merged context's are appended after them.
                Frames = [.. Result.Frames, .. context.Result.Frames],
            },
            // NOTE: we must Bitwise-Or the flags to combine them; any possible duplicate flags are not a concern.
            Flags | context.Flags);
}
