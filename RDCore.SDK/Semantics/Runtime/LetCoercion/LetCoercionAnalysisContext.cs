using RDCore.SDK.Runtime.Abstract;

namespace RDCore.SDK.Semantics.Runtime.LetCoercion
{
    /// <summary>
    /// Encapsulates the context of an <c>Analyze</c> operation as the nullable results of successive operations.
    /// </summary>
    public readonly record struct LetCoercionAnalysisContext
    {
        /// <summary>
        /// Encapsulates the context of an <c>Analyze</c> operation as the nullable results of successive operations.
        /// </summary>
        /// <param name="nodeUri">The <c>SemanticId</c> of the associated expression node.</param>
        /// <param name="result">The result of the let-coercion operation.</param>
        /// <param name="flags">The semantic flags associated with this context.</param>
        public LetCoercionAnalysisContext(Uri nodeUri, LetCoercionResult result, ConversionSemanticFlags flags)
        {
            NodeUri = nodeUri;
            Result = result;
            Flags = flags;
        }

        /// <summary>
        /// The <c>SemanticId</c> of the associated expression node.
        /// </summary>
        public Uri NodeUri { get; }

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
        /// <param name="nodeUri">The <c>SemanticId</c> of the node being evaluated.</param>
        /// <param name="nopResult">The result of the no-op operation.</param>
        public LetCoercionAnalysisContext(Uri nodeUri, LetCoercionResult nopResult)
            : this(nodeUri, nopResult, 0) { }

        /// <summary>
        /// Returns a new <c>LetCoercionAnalysisContext</c> containing the specified evaluation frames and their respective result.
        /// </summary>
        /// <param name="context">The sub-coercion analysis context to merge into this one.</param>
        public LetCoercionAnalysisContext Merge(LetCoercionAnalysisContext context)
            => new(context.NodeUri, 
                context.Result with
                {
                    // NOTE: .Merge(context) is called within an aggregator stack-traversal enumeration;
                    // the aggregation root already contains the final result (or error) value from index 0,
                    // therefore we only need to append the aggregate frame here to keep the stack in order.
                    // 👉 Each iteration necessarily only contains a single frame.
                    Frames = [.. Result.Frames, context.Result.Frame],
                }, 
                // NOTE: we must Bitwise-Or the flags to combine them; any possible duplicate flags are not a concern.
                Flags | context.Flags);
    }
}
