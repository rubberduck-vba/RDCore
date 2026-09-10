using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// A base implementation that executes an expression and returns the evaluation result.
/// </summary>
internal class ExecutionPipeline : IExecutionPipeline<ExecutionResultInfo>
{
    /// <inheritdoc/>
    public ExecutionResultInfo Execute<TNode>(ISymbolResolver resolver, TNode expression) where TNode : ExpressionNode
    {
        // TODO get the appropriate runtime semantics for the specified node.
        return new ExecutionResultInfo(new(VBUnknownType.TypeInfo.DefaultValue, null));
    }
}
