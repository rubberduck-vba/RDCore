using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.Execution;

public interface IExecutionPipeline<out TResult> where TResult : ExecutionResultInfo
{
    /// <summary>
    /// Evaluates an <em>expression</em> given the read face over the current execution context.
    /// </summary>
    /// <typeparam name="TNode">The type of <em>bound expression</em> node to evaluate.</typeparam>
    /// <param name="resolver">The read face over the current execution context.</param>
    /// <param name="expression">The <em>bound expression</em> node to evaluate.</param>
    /// <returns>A record encapsulating the <see cref="RuntimeSemanticsEvaluationResult"/>.</returns>
    // TODO a side-effecting statement pipeline will also need write access / the call stack — widen
    // beyond ISymbolResolver when statement execution lands (§R).
    TResult Execute<TNode>(ISymbolResolver resolver, TNode expression)
        where TNode : ExpressionNode;
}

/// <summary>
/// An <em>execution result value</em> that encapsulates a <see cref="RuntimeSemanticsEvaluationResult"/> result object.
/// </summary>
/// <param name="EvaluationResult">The result of the runtime semantic evaluation.</param>
public record class ExecutionResultInfo(RuntimeSemanticsEvaluationResult EvaluationResult);

