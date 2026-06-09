using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
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

namespace RDCore.SDK.Semantics.Runtime.Operators.Semantics.Relational;

public abstract record class BinaryRelationalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IVerboseMessageBuilder FormatterService) 
    : BinaryOperatorRuntimeSemantics<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags>(LetCoercionSemanticsProvider, FormatterService)
{
    protected abstract bool ComparisonOp(string lhs, string rhs, StringComparison comparison);
    protected abstract bool ComparisonOp(double lhs, double rhs);

    protected override OperatorAnalysisContext<ComparisonOperatorSemanticFlags> CreateAnalysisContext(
        BoundNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        ComparisonOperatorSemanticFlags semanticFlags) 
        => new(node.SemanticId, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected override ISemanticContextContributor<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags> Analyze(
        ISymbolResolver resolver, 
        ConversionOperationSemanticContext coercionContext, 
        ISemanticContextContributor<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags> builder, 
        VBOperatorExpression<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags> expression, 
        OperatorAnalysisContext<ComparisonOperatorSemanticFlags> analysisContext, 
        params VBTypedValue[] operands)
    {
        if (analysisContext.EffectiveTypeResult.Result is VBErrorType 
            && analysisContext.EvaluationResult.Result!.BoxedValue is int errorCode
            && errorCode > 0 && errorCode < VBErrorType.MaximumStdErrorValue)
        {
            builder.AddFlags(ComparisonOperatorSemanticFlags.HasStandardErrorCodes);
        }

        if (operands.Any(operand => operand.TypeInfo is IFloatingPointNumericType && double.IsNaN((double)operand.BoxedValue)))
        {
            builder.AddFlags(ComparisonOperatorSemanticFlags.HasNaNOperand);
        }

        var variantOperands = operands.Select(operand => operand.TypeInfo).Cast<VBVariantType>().ToArray();
        if (operands.All(operand => operand is VBVariantValue) 
            && variantOperands.Any(operand => operand.SubType is VBStringType)
            && variantOperands.Any(operand => operand.SubType is VBNumericType))
        {
            builder.AddFlags(ComparisonOperatorSemanticFlags.IsVariantStringNumericException);
        }

        return builder.AddFlags(analysisContext.EffectiveTypeResult.Result switch
        {
            VBBooleanType => ComparisonOperatorSemanticFlags.BooleanEffectiveType,
            VBByteType => ComparisonOperatorSemanticFlags.ByteEffectiveType | ComparisonOperatorSemanticFlags.IntegralNumericEffectiveType,
            VBIntegerType => ComparisonOperatorSemanticFlags.IntegerEffectiveType | ComparisonOperatorSemanticFlags.IntegralNumericEffectiveType,
            VBLongType => ComparisonOperatorSemanticFlags.LongEffectiveType | ComparisonOperatorSemanticFlags.IntegralNumericEffectiveType,
            VBLongLongType => ComparisonOperatorSemanticFlags.LongLongEffectiveType | ComparisonOperatorSemanticFlags.IntegralNumericEffectiveType,
            VBSingleType => ComparisonOperatorSemanticFlags.SingleEffectiveType | ComparisonOperatorSemanticFlags.FloatingPointNumericEffectiveType,
            VBDoubleType => ComparisonOperatorSemanticFlags.DoubleEffectiveType | ComparisonOperatorSemanticFlags.FloatingPointNumericEffectiveType,
            VBStringType => ComparisonOperatorSemanticFlags.StringEffectiveType,
            VBCurrencyType => ComparisonOperatorSemanticFlags.CurrencyEffectiveType | ComparisonOperatorSemanticFlags.FixedPointNumericEffectiveType,
            VBDecimalType => ComparisonOperatorSemanticFlags.DecimalEffectiveType | ComparisonOperatorSemanticFlags.FixedPointNumericEffectiveType,
            VBNullType => ComparisonOperatorSemanticFlags.NullEffectiveType,
            VBErrorType => ComparisonOperatorSemanticFlags.ErrorEffectiveType,
            _ => 0
        });
    }

    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver, 
        SemanticContext<ComparisonOperatorSemanticFlags> context, 
        VBBinaryOperatorExpression<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags> expression, 
        OperatorEvaluationFrame frame)
    {
        var lhs = frame.Operands[(int)InputIndex.BinaryLeftOperand].GetTargetType();
        var rhs = frame.Operands[(int)InputIndex.BinaryRightOperand].GetTargetType();
        return lhs switch
        {
            VBByteType when rhs is VBByteType or VBStringType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBByteType.TypeInfo),
            VBByteType or VBStringType or VBEmptyType when rhs is VBByteType 
                => DetermineOperatorEffectiveTypeResult.Success(VBByteType.TypeInfo),

            VBBooleanType when rhs is VBBooleanType or VBStringType 
                => DetermineOperatorEffectiveTypeResult.Success(VBBooleanType.TypeInfo),
            VBBooleanType or VBStringType when rhs is VBBooleanType 
                => DetermineOperatorEffectiveTypeResult.Success(VBBooleanType.TypeInfo),

            VBIntegerType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBStringType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo),
            VBByteType or VBBooleanType or VBIntegerType or VBStringType or VBEmptyType when rhs is VBIntegerType 
                => DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo),
            VBBooleanType when rhs is VBByteType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo),
            VBByteType or VBEmptyType when rhs is VBBooleanType 
                => DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo),

            VBByteType or VBBooleanType or VBIntegerType or VBStringType or VBEmptyType when rhs is VBIntegerType 
                => DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo),

            VBBooleanType when rhs is VBByteType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo),
            VBByteType or VBEmptyType when rhs is VBBooleanType 
                => DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo),

            VBEmptyType when rhs is VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBIntegerType.TypeInfo),

            VBLongType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBStringType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBLongType.TypeInfo),
            VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBStringType or VBEmptyType when rhs is VBLongType 
                => DetermineOperatorEffectiveTypeResult.Success(VBLongType.TypeInfo),

            VBLongLongType when rhs is IIntegralNumericType or VBStringType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBLongLongType.TypeInfo),
            IIntegralNumericType or VBStringType or VBEmptyType when rhs is VBLongLongType 
                => DetermineOperatorEffectiveTypeResult.Success(VBLongLongType.TypeInfo),

            VBSingleType when rhs is VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBDoubleType or VBStringType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBSingleType.TypeInfo),
            VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBDoubleType or VBStringType or VBEmptyType when rhs is VBSingleType 
                => DetermineOperatorEffectiveTypeResult.Success(VBSingleType.TypeInfo),

            VBSingleType when rhs is VBLongType 
                => DetermineOperatorEffectiveTypeResult.Success(VBDoubleType.TypeInfo),
            VBLongType when rhs is VBSingleType 
                => DetermineOperatorEffectiveTypeResult.Success(VBDoubleType.TypeInfo),
            VBDoubleType when rhs is IIntegralNumericType or VBDoubleType or VBStringType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBDoubleType.TypeInfo),
            IIntegralNumericType or VBDoubleType or VBStringType or VBEmptyType when rhs is VBDoubleType 
                => DetermineOperatorEffectiveTypeResult.Success(VBDoubleType.TypeInfo),

            VBStringType when rhs is VBStringType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBStringType.TypeInfo),
            VBStringType or VBEmptyType when rhs is VBStringType 
                => DetermineOperatorEffectiveTypeResult.Success(VBStringType.TypeInfo),

            VBCurrencyType when rhs is IIntegralNumericType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBCurrencyType.TypeInfo),
            IIntegralNumericType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBEmptyType when rhs is VBCurrencyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBCurrencyType.TypeInfo),

            VBDateType when rhs is IIntegralNumericType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBDateType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBDateType.TypeInfo),

            IIntegralNumericType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBDateType or VBEmptyType when rhs is VBDateType 
                => DetermineOperatorEffectiveTypeResult.Success(VBDateType.TypeInfo),

            VBDecimalType when rhs is INumericType or VBStringType or VBDateType or VBEmptyType 
                => DetermineOperatorEffectiveTypeResult.Success(VBDecimalType.TypeInfo),
            INumericType or VBStringType or VBDateType or VBEmptyType when rhs is VBDecimalType 
                => DetermineOperatorEffectiveTypeResult.Success(VBDecimalType.TypeInfo),

            VBNullType when rhs is INumericType or VBStringType or VBDateType or VBEmptyType or VBNullType 
                => DetermineOperatorEffectiveTypeResult.Success(VBNullType.TypeInfo),
            INumericType or VBStringType or VBDateType or VBEmptyType or VBNullType when rhs is VBNullType 
                => DetermineOperatorEffectiveTypeResult.Success(VBNullType.TypeInfo),

            VBErrorType when rhs is VBErrorType 
                => DetermineOperatorEffectiveTypeResult.Success(VBErrorType.TypeInfo),

            VBErrorType when rhs is not VBErrorType => DetermineOperatorEffectiveTypeResult.NotApplicable(),
            not VBErrorType when rhs is VBErrorType => DetermineOperatorEffectiveTypeResult.NotApplicable(),

            _ => DetermineOperatorEffectiveTypeResult.NotApplicable()
        };
    }

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        IVBExecutionContext runtime, 
        SemanticContext<ComparisonOperatorSemanticFlags> context,
        VBBinaryOperatorExpression<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags> expression, 
        OperatorEvaluationFrame frame)
    {
        var lhs = frame.Operands[(int)InputIndex.BinaryLeftOperand];
        var rhs = frame.Operands[(int)InputIndex.BinaryRightOperand];
        if (frame.EffectiveType is VBByteType or VBIntegerType or VBLongType or VBLongLongType or VBCurrencyType or VBDecimalType)
        {
            var result = ComparisonOp(((VBNumericTypedValue)lhs).ManagedValue, ((VBNumericTypedValue)rhs).ManagedValue);
            return RuntimeSemanticsEvaluationResult.Success(VBTypedValueFactory.CreateBooleanValue(expression.ResultSymbol, result));
        }
        else if (frame.EffectiveType is VBSingleType or VBDoubleType)
        {
            if (double.IsNaN(((VBNumericTypedValue)lhs).ManagedValue))
            {
                return RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.Overflow, expression, 
                    Exceptions.LetCoercionRuntimeErrorExceptionOverflow_Verbose));
            }
            if (double.IsNaN(((VBNumericTypedValue)rhs).ManagedValue))
            {
                return RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.Overflow, expression,
                    Exceptions.LetCoercionRuntimeErrorExceptionOverflow_Verbose));
            }

        }

        else if (frame.EffectiveType is VBNullType)
        {
            return RuntimeSemanticsEvaluationResult.Success(VBNullValue.Null);
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }

    /// <summary>
    /// 💥 Creates and returns a new <see cref="RuntimeSemanticsEvaluationResult"/> with a <see cref="VBRuntimeErrorId.ObjectRequired"/> error.
    /// </summary>
    /// <param name="expression">The <em>binary arithmetic operator expression</em> whose <c>ResultSymbol</c> the error result will be attached to.</param>
    /// <param name="verbose">A detailed <c>Verbose</c> message about the error.</param>
    protected static RuntimeSemanticsEvaluationResult OnObjectRequired(BoundExpressionNode expression, string verbose)
        => RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.ObjectRequired, expression, verbose));
}