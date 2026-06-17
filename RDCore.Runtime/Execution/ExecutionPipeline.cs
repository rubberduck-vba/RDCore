using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// A base implementation that executes an expression and returns the evaluation result.
/// </summary>
internal class ExecutionPipeline : IExecutionPipeline<ExecutionResultInfo>
{
    /// <summary>
    /// Evaluates an <em>expression</em> given an <em>execution context</em>.
    /// </summary>
    /// <typeparam name="TNode">The type of <em>bound expression</em> node to evaluate.</typeparam>
    /// <param name="context">The <em>runtime context</em> to evaluate the expression with.</param>
    /// <param name="expression">The <em>bound expression</em> node to evaluate.</param>
    /// <returns>A record encapsulating the <see cref="RuntimeSemanticsEvaluationResult"/>.</returns>
    public ExecutionResultInfo Execute<TNode>(IVBExecutionContext context, TNode expression) where TNode : BoundExpression
    {
        // TODO get the appropriate runtime semantics for the specified node.
        return new ExecutionResultInfo(new(VBUnknownType.TypeInfo.DefaultValue, null));
    }
}
