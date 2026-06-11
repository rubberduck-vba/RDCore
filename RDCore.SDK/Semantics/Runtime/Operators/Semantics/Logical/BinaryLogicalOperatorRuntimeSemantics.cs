using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract.Operators;
using RDCore.SDK.Semantics.Runtime.LetCoercion;
using RDCore.SDK.Semantics.Runtime.Operators.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.Operators.Semantics.Logical;

/// <summary>
/// <strong>MS-VBAL 5.6.9.8 Logical Operators</strong><br/>
/// 👉 Logical operators are <em>simple data operators</em> that perform <strong>bitwise computations</strong> on their operands.
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
        BoundNode node, 
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult, 
        LetCoercionAnalysisContext coercionResult, 
        RuntimeSemanticsEvaluationResult evaluationResult, 
        LogicalOperatorSemanticFlags semanticFlags) => new(node.SemanticId, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver, 
        SemanticContext<LogicalOperatorSemanticFlags> context, 
        VBBinaryOperatorExpression<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> expression, 
        OperatorEvaluationFrame frame)
        => frame[InputIndex.BinaryLeftOperand].TypeInfo switch
        {
            VBByteType or VBNullType when frame[InputIndex.BinaryLeftOperand].TypeInfo is VBByteType 
                => DetermineOperatorEffectiveTypeResult.Success(VBByteType.TypeInfo),

            _ => DetermineOperatorEffectiveTypeResult.NotApplicable()
        };

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        IVBExecutionContext runtime, 
        SemanticContext<LogicalOperatorSemanticFlags> context, 
        VBBinaryOperatorExpression<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> expression, 
        OperatorEvaluationFrame frame)
    {
        var lhs = frame[InputIndex.BinaryLeftOperand];
        var rhs = frame[InputIndex.BinaryRightOperand];

        if (lhs.TypeInfo is IIntegralNumericType && rhs.TypeInfo is IIntegralNumericType)
        {
            var lhsCoercion = LetCoercionSemanticsProvider.EvaluateLetCoercionSemantics(runtime.Memory, expression, new(
                NodeUri: expression.SemanticId, 
                StaticSymbol: expression.Symbol, 
                InputIndex: InputIndex.BinaryLeftOperand, 
                SourceValue: lhs, 
                DestinationTypeDesc: VBTypedValueFactory.DescribeType(frame.EffectiveType, expression.ResultSymbol)));
            var lhsValue = lhsCoercion.Result as VBNumericTypedValue;

            var rhsCoercion = LetCoercionSemanticsProvider.EvaluateLetCoercionSemantics(runtime.Memory, expression, new(
                NodeUri: expression.SemanticId,
                StaticSymbol: expression.Symbol,
                InputIndex: InputIndex.BinaryRightOperand,
                SourceValue: rhs,
                DestinationTypeDesc: VBTypedValueFactory.DescribeType(frame.EffectiveType, expression.ResultSymbol)));
            var rhsValue = rhsCoercion.Result as VBNumericTypedValue;

            if (lhsCoercion.ErrorInfo is not null || rhsCoercion.ErrorInfo is not null)
            {
                return RuntimeSemanticsEvaluationResult.Error((lhsCoercion.ErrorInfo ?? rhsCoercion.ErrorInfo)!);
            }

            if (lhsValue?.ManagedValue is double lhsDouble && rhsValue?.ManagedValue is double rhsDouble)
            {
                return RuntimeSemanticsEvaluationResult.Success(
                    VBTypedValueFactory.CreateValue(VBIntegerType.TypeInfo, expression.Symbol,
                        EvaluateBitwiseOp(Convert.ToInt32(lhsDouble), Convert.ToInt32(rhsDouble))));
            }
        }
        else if (lhs is VBNullValue && rhs is VBNullValue)
        {
            return EvaluateNullBinaryExpressionResult(expression.ResultSymbol);
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
        VBBinaryOperatorExpression<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> expression, 
        OperatorEvaluationFrame frame);

    /// <summary>
    /// Evaluates the runtime semantics of a binary logical operator and returns a value of the effective numeric data type.
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="symbol">The unary operator expression symbol.</param>
    /// <param name="lhs">The left-hand side (LHS) numeric binary expression operand.</param>
    /// <param name="rhs">The right-hand side (RHS) numeric binary expression operand.</param>
    /// <returns><c>null</c> if no return value can be evaluated, which would throw a <em>type mismatch</em> error.</returns>
    protected virtual VBTypedValue? EvaluateRuntimeSemantics(VBNumericType effectiveType, Symbol symbol, VBNumericTypedValue lhs, VBNumericTypedValue rhs) =>
        VBTypedValueFactory.CreateValue(effectiveType, symbol, EvaluateBitwiseOp(lhs.ManagedValue, rhs.ManagedValue));

    /// <summary>
    /// Evaluates the runtime semantics of a binary logical operator
    /// </summary>
    /// <param name="effectiveType">The <em>effective data type</em> of the operation.</param>
    /// <param name="symbol">The binary operator expression symbol.</param>
    /// <param name="operand">The binary operand being evaluated.</param>
    /// <param name="lhs">The left-hand side (LHS) numeric binary expression operand.</param>
    /// <param name="rhs">The right-hand side (RHS) numeric binary expression operand.</param>
    /// <returns><c>null</c> if no return value can be evaluated, which would throw a <em>type mismatch</em> error.</returns>
    protected virtual VBTypedValue? EvaluateRuntimeSemantics(VBDateType effectiveType, Symbol symbol, VBNumericTypedValue lhs, VBNumericTypedValue rhs) =>
        VBTypedValueFactory.CreateValue(effectiveType, symbol, EvaluateBitwiseOp(lhs.ManagedValue, rhs.ManagedValue));

    protected override ISemanticContextContributor<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> Analyze(
        ISymbolResolver resolver,
        ConversionOperationSemanticContext coercionContext,
        ISemanticContextContributor<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> builder,
        VBOperatorExpression<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> expression,
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
