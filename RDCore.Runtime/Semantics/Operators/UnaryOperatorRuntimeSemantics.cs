using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;

namespace RDCore.Runtime.Semantics.Operators;

/// <summary>
/// Provides <c>virtual</c> overloads to simplify the implementation of <em>unary operators</em> runtime semantics.
/// </summary>
public abstract record class UnaryOperatorRuntimeSemantics<TContext, TFlags>(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider, 
    IErrorDetailsRecorder ErrorDetails) 
    : OperatorRuntimeSemantics<TContext, TFlags>(LetCoercionProvider, ErrorDetails)
where TContext : SemanticContext<TFlags>, new()
where TFlags : struct, Enum
{
    protected sealed override ISemanticContextContributor<TContext, TFlags> Analyze(
        IRuntimeSymbolResolver resolver,
        ConversionOperationSemanticContext coercionContext,
        ISemanticContextContributor<TContext, TFlags> builder,
        ExpressionNode expression,
        OperatorAnalysisContext<TFlags> analysisContext,
        params VBTypedValue[] operands)
        => AnalyzeOperands(builder, analysisContext.EffectiveTypeResult, operands);

    /// <summary>
    /// Analyzes the operand of the operation, and the effective type it is evaluated in. Analyzes the effective type
    /// alone, by default.
    /// </summary>
    protected virtual ISemanticContextContributor<TContext, TFlags> AnalyzeOperands(
        ISemanticContextContributor<TContext, TFlags> builder,
        DetermineOperatorEffectiveTypeResult effectiveType,
        params VBTypedValue[] operands) => AnalyzeEffectiveType(builder, effectiveType);

    protected virtual ISemanticContextContributor<TContext, TFlags> AnalyzeEffectiveType(
        ISemanticContextContributor<TContext, TFlags> builder,
        DetermineOperatorEffectiveTypeResult context) => builder;

    public sealed override DetermineOperatorEffectiveTypeResult DetermineOperatorEffectiveType(
        IRuntimeSymbolResolver resolver, 
        TContext context, 
        ExpressionNode expression, 
        OperatorEvaluationFrame frame)
        => DetermineOperatorEffectiveType(resolver, expression, frame);

    /// <summary>
    /// The effective type of the operation, based on the type of its operand.
    /// </summary>
    /// <remarks>
    /// 👉 The answer is the whole answer, a type mismatch included: an operator that has a table of effective types has it in the
    /// <see cref="RDCore.Runtime.LanguageCore.Plans.OperationPlanner"/>, and there is no other table to fall back on.
    /// </remarks>
    /// <param name="resolver">The execution context and memory space to operate in. ⚠️ in most situations this access is restrained to <c>ISymbolResolver</c>.</param>
    /// <param name="expression">The <c>VBUnaryOperatorExpression</c> to be evaluated.</param>
    /// <param name="frame">The current evaluation frame for this operation.</param>
    protected abstract DetermineOperatorEffectiveTypeResult DetermineOperatorEffectiveType(
        IRuntimeSymbolResolver resolver,
        ExpressionNode expression,
        OperatorEvaluationFrame frame);

    /// <summary>
    /// Evaluates the run-time semantics of any unary operator involving a <c>VBNullValue</c> operand.
    /// </summary>
    /// <param name="runtime">The execution context and memory space to operate in.</param>
    /// <param name="expression">The <c>VBUnaryOperatorExpression</c> to be evaluated.</param>
    /// <param name="effectiveType">The semantically determined <em>effective type</em> of the operation.</param>
    /// <param name="operand">A valid <c>VBTypedValue</c> let-coerced to the <em>effective type</em> containing the operand of the unary operation.</param>
    /// <remarks>
    /// This implementation satifies the specifiations of every defined unary operator with regards to <c>VBNullValue</c> operands.
    /// </remarks>
    /// <returns>An evaluation result containing the value <c>VBNullValue.Null</c>.</returns>
    protected /*virtual*/ RuntimeSemanticsEvaluationResult EvaluateUnaryExpressionResult(IRuntimeSymbolResolver resolver, ExpressionNode expression, VBNullType effectiveType, VBNullValue operand) => 
        RuntimeSemanticsEvaluationResult.Success(VBNullValue.Null);
}
