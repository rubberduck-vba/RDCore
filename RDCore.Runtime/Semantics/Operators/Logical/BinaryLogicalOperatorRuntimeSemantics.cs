using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Values.Meta;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Logical;

/// <summary>
/// <strong>MS-VBAL 5.6.9.8 Logical Operators</strong><br/>
/// 👉 Logical operators are <em>simple data operators</em> 
/// that perform <strong>bitwise computations</strong> on their operands.
/// </summary>
public abstract record class BinaryLogicalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider, 
    IVerboseMessageBuilder FormatterService)
    : BinaryOperatorRuntimeSemantics<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags>(LetCoercionSemanticsProvider, FormatterService)
{
    /// <summary>
    /// Evaluates the numeric result of a binary logical/bitwise operation.
    /// </summary>
    /// <param name="lhs">The underlying managed value of the left-hand side (LHS) numeric binary expression operand.</param>
    /// <param name="rhs">The underlying managed value of the right-hand side (RHS) numeric binary expression operand.</param>
    protected abstract double EvaluateBitwiseOp(int lhs, int rhs);
    protected virtual double EvaluateBitwiseOp(double lhs, double rhs) => EvaluateBitwiseOp(Convert.ToInt32(lhs), Convert.ToInt32(rhs));

    protected override OperatorAnalysisContext<LogicalOperatorSemanticFlags> CreateAnalysisContext(
        SyntaxNode node, 
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult, 
        LetCoercionAnalysisContext coercionResult, 
        RuntimeSemanticsEvaluationResult evaluationResult, 
        LogicalOperatorSemanticFlags semanticFlags) => new(node.Identity, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver,
        BinaryLogicalOperatorSemanticContext context, 
        VBBinaryOperatorExpressionNode expression, 
        OperatorEvaluationFrame frame)
        => frame[InputIndex.BinaryLeftOperand].TypeInfo switch
        {
            VBByteType or VBNullType when frame[InputIndex.BinaryLeftOperand].TypeInfo is VBByteType 
                => DetermineOperatorEffectiveTypeResult.Success(VBByteType.TypeInfo),

            _ => DetermineOperatorEffectiveTypeResult.NotApplicable()
        };

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        IVBExecutionContext runtime,
        BinaryLogicalOperatorSemanticContext context, 
        VBBinaryOperatorExpressionNode expression, 
        OperatorEvaluationFrame frame)
    {
        var lhs = frame[InputIndex.BinaryLeftOperand];
        var rhs = frame[InputIndex.BinaryRightOperand];

        if (lhs.TypeInfo is IIntegralNumericType && rhs.TypeInfo is IIntegralNumericType)
        {
            var lhsCoercion = LetCoercionSemanticsProvider.EvaluateLetCoercionSemantics(runtime.Memory, expression, new(
                NodeId: expression.Identity, 
                OperandIndex: InputIndex.BinaryLeftOperand, 
                SourceValue: lhs, 
                DestinationTypeDesc: new VBTypeDescValue(frame.EffectiveType)));
            var lhsValue = lhsCoercion.Result as VBNumericTypedValue;

            var rhsCoercion = LetCoercionSemanticsProvider.EvaluateLetCoercionSemantics(runtime.Memory, expression, new(
                NodeId: expression.Identity,
                OperandIndex: InputIndex.BinaryRightOperand,
                SourceValue: rhs,
                DestinationTypeDesc: new VBTypeDescValue(frame.EffectiveType)));
            var rhsValue = rhsCoercion.Result as VBNumericTypedValue;

            if (lhsCoercion.ErrorInfo is not null || rhsCoercion.ErrorInfo is not null)
            {
                return RuntimeSemanticsEvaluationResult.Error((lhsCoercion.ErrorInfo ?? rhsCoercion.ErrorInfo)!);
            }

            if (lhsValue is VBDoubleValue lhsDouble && rhsValue is VBDoubleValue rhsDouble)
            {
                return RuntimeSemanticsEvaluationResult.Success(
                    VBIntegerType.TypeInfo.CreateValue(
                        EvaluateBitwiseOp(Convert.ToInt32(lhsDouble.UnderlyingValue.RuntimeValue!.BoxedValue), Convert.ToInt32(rhsDouble.UnderlyingValue.RuntimeValue!.BoxedValue))));
            }
        }
        else if (lhs is VBNullValue && rhs is VBNullValue)
        {
            return EvaluateNullBinaryExpressionResult();
        }

        return EvaluateSemanticallly(runtime, expression, frame);
    }

    /// <summary>
    /// Evaluates the not-bitwise evaluation branches of the MS-VBAL specifications for a logical operator.
    /// </summary>
    /// <remarks>
    /// Operands are <strong>explicitly specified</strong> as being evaluated bitwise only given specific operand data types.
    /// Base implementation has already handled the case where both operands are <see cref="IIntegralNumericType"/>, and the case where they're both <see cref="VBNullValue"/>.
    /// </remarks>
    protected abstract RuntimeSemanticsEvaluationResult EvaluateSemanticallly(
        IVBExecutionContext context, 
        VBBinaryOperatorExpressionNode expression, 
        OperatorEvaluationFrame frame);

    /// <summary>
    /// Evaluates the runtime semantics of a binary logical operator and returns a value of the effective numeric data type.
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="lhs">The left-hand side (LHS) numeric binary expression operand.</param>
    /// <param name="rhs">The right-hand side (RHS) numeric binary expression operand.</param>
    /// <returns><c>null</c> if no return value can be evaluated, which would throw a <em>type mismatch</em> error.</returns>
    protected virtual VBTypedValue? EvaluateRuntimeSemantics(VBNumericType effectiveType, VBNumericTypedValue lhs, VBNumericTypedValue rhs) =>
        effectiveType.CreateValue(EvaluateBitwiseOp((int)lhs.UnderlyingValue.RuntimeValue!.BoxedValue, (int)rhs.UnderlyingValue.RuntimeValue!.BoxedValue));

    /// <summary>
    /// Evaluates the runtime semantics of a binary logical operator
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="lhs">The left-hand side (LHS) numeric binary expression operand.</param>
    /// <param name="rhs">The right-hand side (RHS) numeric binary expression operand.</param>
    /// <returns><c>null</c> if no return value can be evaluated, which would throw a <em>type mismatch</em> error.</returns>
    protected virtual VBTypedValue? EvaluateRuntimeSemantics(VBDateType effectiveType, VBNumericTypedValue lhs, VBNumericTypedValue rhs) =>
        new VBDateValue(EvaluateBitwiseOp((int)lhs.UnderlyingValue.RuntimeValue!.BoxedValue, (int)rhs.UnderlyingValue.RuntimeValue!.BoxedValue));

    protected override ISemanticContextContributor<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> Analyze(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext coercionContext,
        ISemanticContextContributor<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> builder,
        VBOperatorExpression expression,
        OperatorAnalysisContext<LogicalOperatorSemanticFlags> analysisContext,
        params VBTypedValue[] operands)
    {
        var lhs = operands[(int)InputIndex.BinaryLeftOperand];
        var rhs = operands[(int)InputIndex.BinaryRightOperand];
        if (lhs.TypeInfo is IIntegralNumericType && rhs.TypeInfo is IIntegralNumericType)
        {
            builder.AddFlags(LogicalOperatorSemanticFlags.IsBitwiseSemantics);
        }
        if (lhs is VBNullValue || rhs is VBNullValue)
        {
            builder.AddFlags(LogicalOperatorSemanticFlags.HasNullOperand);
        }

        return builder.AddFlags(analysisContext.EffectiveTypeResult.Result switch
        {
            VBBooleanType => LogicalOperatorSemanticFlags.BooleanEffectiveType,
            VBByteType => LogicalOperatorSemanticFlags.ByteEffectiveType,
            VBIntegerType => LogicalOperatorSemanticFlags.IntegerEffectiveType,
            VBLongType => LogicalOperatorSemanticFlags.LongEffectiveType,
            VBLongLongType => LogicalOperatorSemanticFlags.LongEffectiveType,
            VBNullType => LogicalOperatorSemanticFlags.NullEffectiveType,
            _ => 0
        });
    }
}
