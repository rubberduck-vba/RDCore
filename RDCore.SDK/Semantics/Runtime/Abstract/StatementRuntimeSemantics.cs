using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;

namespace RDCore.SDK.Semantics.Runtime.Abstract;

public abstract record class StatementRuntimeSemantics<TContext, TFlags> : RuntimeSemantics<TContext, TFlags>
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{
    /// <summary>
    /// Evaluates the specified <c>expression</c> in the specified execution context, using the specified inputs 
    /// and returning a <em>semantic result</em> without implicating any side-effecting run-time calls.
    /// </summary>
    /// <param name="runtime">The execution context and memory space to operate with.</param>
    /// <param name="statement">The statement to be evaluated.</param>
    /// <returns>The result of the expression, or a default value of the expected data type. <c>null</c> if a result cannot be semantically determined (an exception should have been thrown then).</returns>
    protected virtual VBTypedValue? EvaluateSemanticNodeResult(IVBExecutionContext runtime, BoundStatementNode statement, VBTypedValue[] arguments)
    {
        // TODO
        return default;
    }        
}