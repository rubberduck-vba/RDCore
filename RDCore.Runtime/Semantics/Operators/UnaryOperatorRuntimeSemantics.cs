using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.Abstract;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
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
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators;

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
    protected sealed override ISemanticContextContributor<TContext, TFlags> Analyze(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext coercionContext,
        ISemanticContextContributor<TContext, TFlags> builder,
        VBOperatorExpression<TContext, TFlags> expression,
        OperatorAnalysisContext<TFlags> analysisContext,
        params VBTypedValue[] operands)
        => AnalyzeEffectiveType(builder, analysisContext.EffectiveTypeResult);

    protected virtual ISemanticContextContributor<TContext, TFlags> AnalyzeEffectiveType(
        ISemanticContextContributor<TContext, TFlags> builder,
        DetermineOperatorEffectiveTypeResult context) => builder;

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
        var effectiveType = frame[InputIndex.UnaryOperand].TypeInfo switch
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
                Exceptions.VBRuntimeTypeMismatch_OperationEffectiveType_Verbose.Replace("{$OPERANDS}", frame[InputIndex.UnaryOperand].TypeInfo.Name)));
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
