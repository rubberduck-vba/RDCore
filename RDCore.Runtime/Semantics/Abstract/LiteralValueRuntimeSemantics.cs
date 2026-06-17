using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Context.Abstract;

namespace RDCore.Runtime.Semantics.Abstract;

public abstract record class LiteralValueRuntimeSemantics<TContext, TFlags> : RuntimeSemantics<TContext, TFlags>
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{
    public override RuntimeSemanticsEvaluationResult Evaluate(IVBExecutionContext runtime, SemanticContext<TFlags> context, BoundNode node, params VBTypedValue[] inputs)
        => EvaluateSemanticResult((VBLiteralExpression)node);

    /// <summary>
    /// Evaluates the specified <c>expression</c> in the specified execution context, using the specified inputs 
    /// and returning a <em>semantic result</em> without implicating any side-effecting runtime calls.
    /// </summary>
    /// <param name="expression">The expression to be evaluated.</param>
    /// <returns>A successful <c>RuntimeSemanticsEvalutationResult</c> encapsulating the static expression value.</returns>
    protected virtual RuntimeSemanticsEvaluationResult EvaluateSemanticResult(VBLiteralExpression expression) => 
        RuntimeSemanticsEvaluationResult.Success(expression.StaticValue);
}
