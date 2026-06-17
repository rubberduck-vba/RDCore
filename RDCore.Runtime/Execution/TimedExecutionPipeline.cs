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

    /// <summary>
    /// Evaluates an <em>expression</em> given an <em>execution context</em> using a <see cref="Stopwatch"/> to time its execution.
    /// </summary>
    /// <typeparam name="TNode">The type of <em>bound expression</em> node to evaluate.</typeparam>
    /// <param name="context">The <em>runtime context</em> to evaluate the expression with.</param>
    /// <param name="expression">The <em>bound expression</em> node to evaluate.</param>
    /// <returns>A record encapsulating the <see cref="RuntimeSemanticsEvaluationResult"/> and a <see cref="TimeSpan"/> representing the amoutn of time elapsed during evaluation.</returns>
    public TimedExecutionResultInfo Execute<TNode>(IVBExecutionContext context, TNode expression) where TNode : BoundExpression
    {
        var stopwatch = Stopwatch.StartNew();
        var result =_pipeline.Execute(context, expression);
        
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
