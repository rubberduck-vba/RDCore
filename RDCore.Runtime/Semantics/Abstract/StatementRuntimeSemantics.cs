using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Context.Abstract;

namespace RDCore.Runtime.Semantics.Abstract;

public abstract record class StatementRuntimeSemantics<TContext, TFlags> : RuntimeSemantics<TContext, TFlags>
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{
    public override RuntimeSemanticsEvaluationResult Evaluate(IVBExecutionContext runtime, SemanticContext<TFlags> context, BoundNode node, params VBTypedValue[] inputs)
    {
        throw new NotImplementedException();
    }

    protected sealed override RuntimeSemanticsEvaluationResult EvaluateSemanticResult(
        ISymbolResolver resolver,
        SemanticContext<TFlags> context,
        BoundNode node,
        VBType effectiveType,
        params VBTypedValue[] inputs) => EvaluateSemanticResult((IVBExecutionContext)resolver, (BoundStatement)node, inputs);

    /// <summary>
    /// Evaluates the specified <c>expression</c> in the specified execution context, using the specified inputs 
    /// and returning a <em>semantic result</em> without implicating any side-effecting run-time calls.
    /// </summary>
    /// <param name="runtime">The execution context and memory space to operate with.</param>
    /// <param name="statement">The statement to be evaluated.</param>
    /// <param name="inputs">The inputs of the statement.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> encapsulating the result this statement (including any runtime error diagnostics).</returns>
    protected virtual RuntimeSemanticsEvaluationResult EvaluateSemanticResult(IVBExecutionContext runtime, BoundStatement statement, VBTypedValue[] inputs) 
        => RuntimeSemanticsEvaluationResult.InternalError(); // TODO
}