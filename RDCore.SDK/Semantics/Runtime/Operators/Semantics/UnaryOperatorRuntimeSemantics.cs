using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract.Operators;
using RDCore.SDK.Semantics.Runtime.LetCoercion;
using RDCore.SDK.Semantics.Runtime.Operators.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.Operators.Semantics;

/// <summary>
/// Provides <c>virtual</c> overloads to simplify the implementation of <em>unary operators</em> runtime semantics.
/// </summary>
public abstract record class UnaryOperatorRuntimeSemantics<TContext, TFlags>(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider, 
    IVerboseMessageBuilder FormatterService) 
    : OperatorRuntimeSemantics<TContext, TFlags>(LetCoercionProvider, FormatterService)
where TContext : SemanticContext<TFlags>, new()
where TFlags : struct, Enum
{
    /// <summary>
    /// Analyzes the specified <c>VBOperatorExpression</c> node in the specified execution context, using the specified operands.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context..</param>
    /// <param name="coercionContext">The <em>let-coercion</em> semantic context of this operation.</param>
    /// <param name="builder">A <em>semantic flags builder</em> specifically for the operation defined by the <see cref="VBOperatorExpression{TContext,TFlags}"/> node under scrutiny.</param>
    /// <param name="analysisContext">The results of each step of the analysis/evaluation process.</param>
    /// <param name="expression">The <em>operator expression</em> node to be evaluated.</param>
    /// <param name="operands">The operands of the <em>operator expression</em>.</param>
    /// <remarks>
    /// <list type="bullet">
    /// <item>If present, the <c>ErrorInfo</c> of the <c>effectiveTypeResult</c> has already been added as an error diagnostic to the context.</item>
    /// <item>This method is templated and invoked by the <em>semantic analysis pipeline</em> to incrementally build the <em>semantic flags</em> into the context.</item>
    /// </list>
    /// 🧩 <strong>There is no mechanism to <em>remove</em> a flag from the <em>builder</em></strong>; semantic flags should only be added in <c>sealed</c> implementations under most circumstances.
    /// </remarks>
    /// <returns>The <c>builder</c> parameter, or a reference to it returned by one of its methods.</returns>
    protected sealed override ISemanticContextContributor<TContext, TFlags> Analyze(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext coercionContext,
        ISemanticContextContributor<TContext, TFlags> builder,
        VBOperatorExpression<TContext, TFlags> expression,
        OperatorAnalysisContext<TFlags> analysisContext,
        params VBTypedValue[] operands)
        => AnalyzeEffectiveType(builder, analysisContext.EffectiveTypeResult);

    /// <summary>
    /// Analyzes the <em>determine effective type</em> evaluation step.
    /// </summary>
    /// <param name="builder">A <em>builder</em> for the arithmetic operation semantic context.</param>
    /// <param name="context">The result of the <em>determine effective type</em> evaluation step.</param>
    /// <returns>The <c>builder</c>.</returns>
    protected virtual ISemanticContextContributor<TContext, TFlags> AnalyzeEffectiveType(
        ISemanticContextContributor<TContext, TFlags> builder,
        DetermineOperatorEffectiveTypeResult context) => builder;

    /// <summary>
    /// Determines the <em>effective type</em> of an operation based on the data type of its operands.
    /// </summary>
    /// <param name="resolver">A read-only interface over the current execution context..</param>
    /// <param name="context">The semantic context of this operation, built by <c>Analyze</c>.</param>
    /// <param name="expression">The <em>expression node</em> to analyze.</param>
    /// <param name="frame">The evaluation frame of the operator expression.</param>
    /// <returns><strong>Does not throw exceptions.</strong> Returns <c>DetermineOperatorEffectiveTypeResult.NotApplicable</c> if no type is statically valid.</returns>
    public sealed override DetermineOperatorEffectiveTypeResult DetermineOperatorEffectiveType(
        ISymbolResolver resolver, 
        SemanticContext<TFlags> context, 
        VBOperatorExpression<TContext, TFlags> expression, 
        OperatorEvaluationFrame frame)
    {
        var result = DetermineOperatorEffectiveType(resolver, expression, frame);
        if (result.IsApplicable)
        {
            // a more specialized implementation is in charge.
            return result;
        }

        // the base rules are the verbatim specifications for
        // unary arithmetic operators (5.6.9.3.1 runtime semantics):
        var effectiveType = frame[OperandIndex.UnaryOperand].TypeInfo switch
        {
            VBByteType => VBByteType.TypeInfo,
            VBBooleanType or VBIntegerType or VBEmptyType => VBIntegerType.TypeInfo,
            VBLongType => VBLongType.TypeInfo,
            VBLongLongType => VBLongLongType.TypeInfo,
            VBSingleType => VBSingleType.TypeInfo,
            VBDoubleType or VBStringType => VBDoubleType.TypeInfo,
            VBCurrencyType => VBCurrencyType.TypeInfo,
            VBDateType => VBDateType.TypeInfo, // NOTE: operand is then let-coerced to VBDoubleType at validation, for evaluation semantics
            VBDecimalType => VBDecimalType.TypeInfo,

            VBNullType => VBNullType.TypeInfo,

            _ => VBUnknownType.TypeInfo
        };

        return effectiveType is not VBUnknownType
            ? DetermineOperatorEffectiveTypeResult.Success(effectiveType)
            // if no effective type can be determined, it's a type mismatch error:
            : DetermineOperatorEffectiveTypeResult.Error(OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression,
                Exceptions.VBRuntimeTypeMismatch_OperationEffectiveType_Verbose.Replace("{$OPERANDS}", frame[OperandIndex.UnaryOperand].TypeInfo.Name)));
    }

    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (runtime semantics) 
    /// The operator has the declared type returned by this method, based on the declared type of its operands.
    /// </summary>
    /// <remarks>
    /// ⚠️ <strong>Must</strong> return <c>DetermineOperatorEffectiveTypeResult.NotApplicable()</c> in a fallback case,
    /// to allow the base logic to resolve an <em>effective type</em> when the more specialized logic does not yield a data type.
    /// </remarks>
    /// <param name="resolver">The execution context and memory space to operate in. ⚠️ in most situations this access is restrained to <c>ISymbolResolver</c>.</param>
    /// <param name="expression">The <c>VBUnaryOperatorExpression</c> to be evaluated.</param>
    /// <param name="frame">The current evaluation frame for this operation.</param>
    protected abstract DetermineOperatorEffectiveTypeResult DetermineOperatorEffectiveType(
        ISymbolResolver resolver,
        VBOperatorExpression<TContext, TFlags> expression,
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
    protected /*virtual*/ RuntimeSemanticsEvaluationResult EvaluateUnaryExpressionResult(IVBExecutionContext runtime, VBUnaryOperatorExpression<TContext, TFlags> expression, VBNullType effectiveType, VBNullValue operand) => 
        RuntimeSemanticsEvaluationResult.Success(VBNullValue.Null);
}
