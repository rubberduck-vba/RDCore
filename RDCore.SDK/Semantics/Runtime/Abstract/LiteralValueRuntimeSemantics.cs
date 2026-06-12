using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Runtime.Abstract;

/// <summary>
/// Encapsulates the runtime semantics of <em>literal value</em> expressions.
/// </summary>
/// <typeparam name="TContext">The specific type of <em>semantic context</em> associated with these semantics.</typeparam>
/// <typeparam name="TFlags">The specific type of <em>semantic flags</em> associated with the <em>semantic context</em>.</typeparam>
public abstract record class LiteralValueRuntimeSemantics<TContext, TFlags> : RuntimeSemantics<TContext, TFlags>
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{
    /// <summary>
    /// Evaluates the specified <c>BoundNode</c> in the specified execution context, using the specified inputs.
    /// </summary>
    /// <remarks>
    /// ⚠️ <strong>Does not throw</strong> any run-time errors; instead it packages the error metadata in the result.
    /// </remarks>
    /// <param name="runtime">The execution context and memory space to operate with.</param>
    /// <param name="context">The semantic context of this operation, built by <c>Analyze</c>.</param>
    /// <param name="node">The bound node to be evaluated.</param>
    /// <param name="inputs">The inputs of the bound node.</param>
    public override RuntimeSemanticsEvaluationResult Evaluate(IVBExecutionContext runtime, SemanticContext<TFlags> context, BoundNode node, params VBTypedValue[] inputs)
        => EvaluateSemanticResult((VBLiteralExpression)node);

    /// <summary>
    /// Evaluates the specified <c>expression</c> in the specified execution context, using the specified inputs 
    /// and returning a <em>semantic result</em> without implicating any side-effecting run-time calls.
    /// </summary>
    /// <param name="expression">The expression to be evaluated.</param>
    /// <returns>A successful <c>RuntimeSemanticsEvalutationResult</c> encapsulating the static expression value.</returns>
    protected virtual RuntimeSemanticsEvaluationResult EvaluateSemanticResult(VBLiteralExpression expression) => 
        RuntimeSemanticsEvaluationResult.Success(expression.StaticValue);
}
