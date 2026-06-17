using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Semantics.Analysis;

/// <summary>
/// An abstraction that represents a set of registered analyzer plug-ins that can analyze a set of specific semantic operations.
/// </summary>
public interface IAnalysisProvider
{
    /// <summary>
    /// Instructs analyzer plug-ins to build a semantic context for the specified <em>let coercion operation</em> context.
    /// </summary>
    /// <typeparam name="TContext">The type of <em>semantic context</em> specific to the operator expression.</typeparam>
    /// <typeparam name="TFlags">The type of <em>semantic flags</em> specific to the <em>semantic context</em>.</typeparam>
    /// <param name="context">A <em>builder</em> for the semantic context of the let-coercion operation under scrutiny.</param>
    /// <param name="expression">The operator expression under scrutiny.</param>
    /// <param name="destinationDeclaredType">The <em>destination declared type</em> of the expression.</param>
    /// <param name="sourceValue">A <em>typed value</em> being let-coerced to the <em>destination declared type</em>.</param>
    /// <param name="resultValue">The <em>typed value</em> resulting from the let-coercion operation. <c>null</c> if the operation is a <em>type mismatch</em>, in which case a runtime error diagnostic has already been attached to the semantic context.</param>
    /// <returns>A <c>Task</c> that completes when all LSP notifications have been sent.</returns>
    void AnalyzeLetCoercionSemantics<TContext, TFlags>(SemanticContextBuilder<ConversionOperationSemanticContext, ConversionSemanticFlags> context, VBOperatorExpression<TContext, TFlags> expression, VBType destinationDeclaredType, VBTypedValue sourceValue, VBTypedValue? resultValue)
        where TContext : SemanticContext<TFlags>, new ()
        where TFlags : struct, Enum;

    /// <summary>
    /// Instructs analyzer plug-ins to build a semantic context for the specified <em>let coercion operation</em> context.
    /// </summary>
    /// <typeparam name="TContext">The type of <em>semantic context</em> specific to the operator expression.</typeparam>
    /// <typeparam name="TFlags">The type of <em>semantic flags</em> specific to the <em>semantic context</em>.</typeparam>
    /// <param name="expression">The operator expression under scrutiny.</param>
    /// <param name="destinationDeclaredType">The <em>destination declared type</em> of the expression.</param>
    /// <param name="sourceValue">A <em>typed value</em> being let-coerced to the <em>destination declared type</em>.</param>
    /// <param name="resultValue">The <em>typed value</em> resulting from the let-coercion operation. <c>null</c> if the operation is a <em>type mismatch</em>, in which case a runtime error diagnostic has already been attached to the semantic context.</param>
    /// <returns>A <c>Task</c> that completes when all LSP notifications have been sent.</returns>
    void AnalyzeOperatorSemantics<TContext, TFlags>(VBOperatorExpression<TContext, TFlags> expression, VBType destinationDeclaredType, VBTypedValue sourceValue, VBTypedValue? resultValue)
        where TContext : SemanticContext<TFlags>, new()
        where TFlags : struct, Enum;
}
