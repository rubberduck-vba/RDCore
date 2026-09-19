using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;
using System.Numerics;

namespace RDCore.Runtime.Semantics.Operators;

public abstract record class BinaryRelationalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryOperatorRuntimeSemantics<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags>(LetCoercionSemanticsProvider, FormatterService)
{
    protected abstract bool ComparisonOp(string lhs, string rhs, StringComparisonRules rules);

    /// <summary>
    /// Compares two operands already let-coerced to the same numeric effective type, in that type's
    /// own CLR representation — generic math (<see cref="INumber{TSelf}"/>), no widening or boxing.
    /// </summary>
    protected abstract bool ComparisonOp<T>(T lhs, T rhs) where T : INumber<T>;

    protected override OperatorAnalysisContext<ComparisonOperatorSemanticFlags> CreateAnalysisContext(
        SyntaxNode node,
        DetermineOperatorEffectiveTypeResult determineOperatorEffectiveTypeResult,
        LetCoercionAnalysisContext coercionResult,
        RuntimeSemanticsEvaluationResult evaluationResult,
        ComparisonOperatorSemanticFlags semanticFlags) 
        => new(node.Identity, determineOperatorEffectiveTypeResult, coercionResult, evaluationResult, semanticFlags);

    protected override ISemanticContextContributor<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags> Analyze(
        ISymbolResolver resolver, 
        ConversionOperationSemanticContext coercionContext, 
        ISemanticContextContributor<BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags>, ComparisonOperatorSemanticFlags> builder, 
        VBOperatorExpression expression, 
        OperatorAnalysisContext<ComparisonOperatorSemanticFlags> analysisContext, params VBTypedValue[] operands)
    {
        // MS-VBAL 5.6.9.5: comparing two Error values is only defined when both hold a standard error code.
        if (analysisContext.EffectiveTypeResult.Result is VBErrorType
            && operands.OfType<VBErrorValue>().ToArray() is { Length: > 1 } errorOperands
            && errorOperands.All(error => error.Value >= VBErrorType.MinimumStdErrorValue && error.Value <= VBErrorType.MaximumStdErrorValue))
        {
            builder.AddFlags(ComparisonOperatorSemanticFlags.HasStandardErrorCodes);
        }

        if (operands.Any(operand => operand.TypeInfo is IFloatingPointNumericType
            && double.IsNaN(Convert.ToDouble(operand.RuntimeValue.BoxedValue))))
        {
            builder.AddFlags(ComparisonOperatorSemanticFlags.HasNaNOperand);
        }

        // only Variant operands have a subtype to look at.
        var variantSubTypes = operands.Select(operand => operand.TypeInfo).OfType<VBVariantType>().Select(variant => variant.SubType).ToArray();
        if (operands.All(operand => operand is VBVariantValue)
            && variantSubTypes.Any(subType => subType is VBStringType)
            && variantSubTypes.Any(subType => subType is VBNumericType))
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
            VBStringType => ComparisonOperatorSemanticFlags.StringEffectiveType
                | (analysisContext.Comparison.IgnoresCase
                    ? ComparisonOperatorSemanticFlags.StringComparisonText
                    : ComparisonOperatorSemanticFlags.StringComparisonBinary),
            VBCurrencyType => ComparisonOperatorSemanticFlags.CurrencyEffectiveType | ComparisonOperatorSemanticFlags.FixedPointNumericEffectiveType,
            VBDecimalType => ComparisonOperatorSemanticFlags.DecimalEffectiveType | ComparisonOperatorSemanticFlags.FixedPointNumericEffectiveType,
            VBNullType => ComparisonOperatorSemanticFlags.NullEffectiveType,
            VBErrorType => ComparisonOperatorSemanticFlags.ErrorEffectiveType,
            _ => 0
        });
    }

    protected override DetermineOperatorEffectiveTypeResult DetermineBinaryOperatorEffectiveType(
        ISymbolResolver resolver,
        BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags> context, 
        VBBinaryOperatorExpressionNode expression, 
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

            // MS-VBAL 5.6.9.5: no effective type is defined for any other pair of operands - a type mismatch.
            _ => DetermineOperatorEffectiveTypeResult.Error(OnRuntimeError(VBRuntimeErrorId.TypeMismatch, expression,
                Exceptions.VBRuntimeTypeMismatch_OperationEffectiveType_Verbose.Replace("{$OPERANDS}", string.Join(", ", [lhs.Name, rhs.Name]))))
        };
    }

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        ISymbolResolver resolver,
        BinaryOperatorSemanticContext<ComparisonOperatorSemanticFlags> context,
        VBBinaryOperatorExpressionNode expression, 
        OperatorEvaluationFrame frame)
    {
        var lhs = frame.Operands[(int)InputIndex.BinaryLeftOperand];
        var rhs = frame.Operands[(int)InputIndex.BinaryRightOperand];

        // operands have been let-coerced to the effective type by the pipeline; each numeric
        // effective type compares in its own CLR representation via generic math (INumber<T>) —
        // no widening, no boxing through BoxedValue.
        if (frame.EffectiveType is VBByteType)
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(((VBByteValue)lhs).Value, ((VBByteValue)rhs).Value)));
        }
        else if (frame.EffectiveType is VBIntegerType)
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(((VBIntegerValue)lhs).Value, ((VBIntegerValue)rhs).Value)));
        }
        else if (frame.EffectiveType is VBLongType)
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(((VBLongValue)lhs).Value, ((VBLongValue)rhs).Value)));
        }
        else if (frame.EffectiveType is VBLongLongType)
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(((VBLongLongValue)lhs).Value, ((VBLongLongValue)rhs).Value)));
        }
        else if (frame.EffectiveType is VBBooleanType)
        {
            // Boolean compares over its -1/0 representation (RD-VBAL §5.0.2.1, same convention the
            // logical operators use); bool is not itself an INumber<T>, so it widens to long.
            var result = ComparisonOp(
                Convert.ToInt64(lhs.RuntimeValue.BoxedValue),
                Convert.ToInt64(rhs.RuntimeValue.BoxedValue));
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(result));
        }
        else if (frame.EffectiveType is VBStringType)
        {
            // MS-VBAL 5.6.9.5: in a module compared in text mode the strings are compared regardless of case, according to the
            // regional settings of the environment; otherwise, by the code of each character.
            var result = ComparisonOp(((VBStringValue)lhs).Value!, ((VBStringValue)rhs).Value!, frame.Comparison);
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(result));
        }
        else if (frame.EffectiveType is VBCurrencyType)
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(((VBCurrencyValue)lhs).Value.Value, ((VBCurrencyValue)rhs).Value.Value)));
        }
        else if (frame.EffectiveType is VBDecimalType)
        {
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(((VBDecimalValue)lhs).Value, ((VBDecimalValue)rhs).Value)));
        }
        else if (frame.EffectiveType is VBSingleType)
        {
            var lhsValue = ((VBSingleValue)lhs).Value;
            var rhsValue = ((VBSingleValue)rhs).Value;
            if (float.IsNaN(lhsValue) || float.IsNaN(rhsValue))
            {
                return RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.Overflow, expression,
                    Exceptions.LetCoercionRuntimeErrorExceptionOverflow_Verbose));
            }
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(lhsValue, rhsValue)));
        }
        else if (frame.EffectiveType is VBDoubleType)
        {
            var lhsValue = ((VBDoubleValue)lhs).Value;
            var rhsValue = ((VBDoubleValue)rhs).Value;
            if (double.IsNaN(lhsValue) || double.IsNaN(rhsValue))
            {
                return RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.Overflow, expression,
                    Exceptions.LetCoercionRuntimeErrorExceptionOverflow_Verbose));
            }
            return RuntimeSemanticsEvaluationResult.Success(new VBBooleanValue(ComparisonOp(lhsValue, rhsValue)));
        }
        else if (frame.EffectiveType is VBNullType)
        {
            return RuntimeSemanticsEvaluationResult.Success(VBNullValue.Null);
        }
        else if (frame.EffectiveType is VBErrorType)
        {
            // MS-VBAL 5.6.9.5: two standard error codes compare by their numeric value.
            return RuntimeSemanticsEvaluationResult.Success(
                new VBBooleanValue(ComparisonOp(((VBErrorValue)lhs).Value, ((VBErrorValue)rhs).Value)));
        }

        return RuntimeSemanticsEvaluationResult.InternalError();
    }

    /// <summary>
    /// 💥 Creates and returns a new <see cref="RuntimeSemanticsEvaluationResult"/> with a <see cref="VBRuntimeErrorId.ObjectRequired"/> error.
    /// </summary>
    /// <param name="expression">The <em>binary arithmetic operator expression</em> whose <c>ResultSymbol</c> the error result will be attached to.</param>
    /// <param name="verbose">A detailed <c>Verbose</c> message about the error.</param>
    protected static RuntimeSemanticsEvaluationResult OnObjectRequired(ExpressionNode expression, string verbose)
        => RuntimeSemanticsEvaluationResult.Error(OnRuntimeError(VBRuntimeErrorId.ObjectRequired, expression.SourceLocation, verbose));
}