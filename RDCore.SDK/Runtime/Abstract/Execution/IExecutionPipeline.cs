using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.Execution;

public interface IExecutionPipeline<out TResult> where TResult : ExecutionResultInfo
{
    /// <summary>
    /// Evaluates an <em>expression</em> given an <em>execution context</em>.
    /// </summary>
    /// <typeparam name="TNode">The type of <em>bound expression</em> node to evaluate.</typeparam>
    /// <param name="context">The <em>runtime context</em> to evaluate the expression with.</param>
    /// <param name="expression">The <em>bound expression</em> node to evaluate.</param>
    /// <returns>A record encapsulating the <see cref="RuntimeSemanticsEvaluationResult"/>.</returns>
    TResult Execute<TNode>(IVBExecutionContext context, TNode expression)
        where TNode : BoundExpression;
}

/// <summary>
/// An <em>execution result value</em> that encapsulates a <see cref="RuntimeSemanticsEvaluationResult"/> result object.
/// </summary>
/// <param name="EvaluationResult">The result of the runtime semantic evaluation.</param>
public record class ExecutionResultInfo(RuntimeSemanticsEvaluationResult EvaluationResult);

