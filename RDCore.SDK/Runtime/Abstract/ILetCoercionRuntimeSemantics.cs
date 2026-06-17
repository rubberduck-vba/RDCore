using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context.Abstract;

namespace RDCore.SDK.Runtime.Abstract;

/// <summary>
/// Formalizes the interface of let-coercion runtime semantics.
/// </summary>
public interface ILetCoercionRuntimeSemantics
{
    Type LetCoercionSpecification { get; }

    /// <summary>
    /// Evaluates the let-coerced <c>VBTypedValue</c> for the specified <c>effectiveType</c> in the context of the specified <c>expression</c>.
    /// </summary>
    /// <param name="resolver">A service that can resolve symbols and their values in the current context.</param>
    /// <param name="expression">The <c>VBOperatorExpression</c> that is being evaluated.</param>
    /// <param name="frame">The current stack frame of the coercion operation.</param>
    /// <returns>An object that encapsulates the result of the operation, including any run-time errors to be thrown.</returns>
    LetCoercionResult EvaluateLetCoercion<TContext, TFlags>(
        ISymbolResolver resolver, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum;

    /// <summary>
    /// Builds the semantic context of a let-coercion operation involving the semantics of a specific <c>VBType</c>.
    /// </summary>
    /// <typeparam name="TContext">The type of <em>semantic context</em> of the <c>expression</c> the let-coercion is occurring inside of.</typeparam>
    /// <typeparam name="TFlags">The type of semantic flags associated with the semantic context of the <c>expression</c>.</typeparam>
    /// <param name="builder">Builds the semantic context of the conversion operation.</param>
    /// <param name="resolver">A symbol lookup service.</param>
    /// <param name="expression">The <c>BoundExpression</c> that is being evaluated.</param>
    /// <param name="frame">The current stack frame of the coercion operation.</param>
    /// <param name="result">The result of the let-coercion operation for the current stack frame.</param>
    /// <remarks>
    /// 🧩 <em>Analyzers</em> (<c>RDCore.Diagnostics</c> and other <em>plug-ins</em>) perform the actual analysis of the semantic context.
    /// </remarks>
    /// <returns>An <em>analysis context</em> for this let-coercion operation.</returns>
    LetCoercionAnalysisContext Analyze<TContext, TFlags>(
        ILetCoercionSemanticContextBuilder builder, 
        ISymbolResolver resolver, 
        VBOperatorExpression<TContext, TFlags> expression, 
        LetCoercionStackFrame frame,
        LetCoercionResult result)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum;
}
