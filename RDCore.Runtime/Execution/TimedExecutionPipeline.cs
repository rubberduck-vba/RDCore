using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics;

namespace RDCore.Runtime.Execution;

/// <summary>
/// 🧩 Decorates an <see cref="IExecutionPipeline{TResult}"/> with a <see cref="Stopwatch"/> to return an 
/// enriched result that includes a <see cref="TimeSpan"/> representing the amount of time elapsed during evaluation.
/// </summary>
/// <param name="pipeline">The decorated execution pipeline.</param>
internal class TimedExecutionPipeline(IExecutionPipeline<ExecutionResultInfo> pipeline) : IExecutionPipeline<TimedExecutionResultInfo>
{
    private readonly IExecutionPipeline<ExecutionResultInfo> _pipeline = pipeline;

    /// <inheritdoc/>
    public TimedExecutionResultInfo Execute<TNode>(ISymbolResolver resolver, TNode expression) where TNode : ExpressionNode
    {
        var stopwatch = Stopwatch.StartNew();
        var result = _pipeline.Execute(resolver, expression);

        stopwatch.Stop();
        return new(result.EvaluationResult, stopwatch.Elapsed);
    }
}

/// <summary>
/// An <em>execution result value </em> that encapsulates a <see cref="TimeSpan"/> alongside the evaluation result object.
/// </summary>
/// <param name="EvaluationResult">The result of the runtime semantic evaluation.</param>
/// <param name="Elapsed">A <see cref="TimeSpan"/> representing the amount of time elapsed during the evaluation of the expression.</param>
public record class TimedExecutionResultInfo(RuntimeSemanticsEvaluationResult EvaluationResult, TimeSpan Elapsed)
    : ExecutionResultInfo(EvaluationResult);
