using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.Execution;

public interface IExecutionPipeline<out TResult> where TResult : ExecutionResultInfo
{
    /// <summary>
    /// Evaluates an <em>expression</em> given the current execution session.
    /// </summary>
    /// <typeparam name="TNode">The type of <em>bound expression</em> node to evaluate.</typeparam>
    /// <param name="session">The current execution session.</param>
    /// <param name="expression">The <em>bound expression</em> node to evaluate.</param>
    /// <returns>A record encapsulating the <see cref="RuntimeSemanticsEvaluationResult"/>.</returns>
    TResult Execute<TNode>(IRuntimeSession session, TNode expression)
        where TNode : ExpressionNode;
}

/// <summary>
/// An <em>execution result value</em> that encapsulates a <see cref="RuntimeSemanticsEvaluationResult"/> result object.
/// </summary>
/// <param name="EvaluationResult">The result of the runtime semantic evaluation.</param>
public record class ExecutionResultInfo(RuntimeSemanticsEvaluationResult EvaluationResult);

