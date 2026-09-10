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
    protected sealed override RuntimeSemanticsEvaluationResult EvaluateSemanticResult(
        ISymbolResolver resolver,
        TContext context,
        SyntaxNode node,
        VBType effectiveType,
        params VBTypedValue[] inputs) => EvaluateSemanticResult(resolver, (StatementNode)node, inputs);

    /// <summary>
    /// Evaluates the specified <c>expression</c> in the specified execution context, using the specified inputs 
    /// and returning a <em>semantic result</em> without implicating any side-effecting run-time calls.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context.</param>
    /// <param name="statement">The statement to be evaluated.</param>
    /// <param name="inputs">The inputs of the statement.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> encapsulating the result this statement (including any runtime error diagnostics).</returns>
    protected virtual RuntimeSemanticsEvaluationResult EvaluateSemanticResult(ISymbolResolver resolver, StatementNode statement, VBTypedValue[] inputs)
        => RuntimeSemanticsEvaluationResult.InternalError(); // TODO
}