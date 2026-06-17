using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Arithmetic;

/// <summary>
/// MS-VBAL 5.6.9.3.4 Binary '*' Operator (runtime semantics)
/// </summary>
public record class BinaryMultiplicationOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryArithmeticOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    protected override double EvaluateManagedNumericOp(double lhs, double rhs) => lhs * rhs;

    protected override DetermineOperatorEffectiveTypeResult DetermineArithmeticOperatorEffectiveType(
        ISymbolResolver resolver,
        BinaryArithmeticOperatorSemanticContext context,
        VBBinaryOperatorExpression<BinaryArithmeticOperatorSemanticContext,
        ArithmeticOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame) => frame[InputIndex.BinaryLeftOperand].TypeInfo switch
        {
            VBCurrencyType
                when frame[InputIndex.BinaryRightOperand].TypeInfo is VBSingleType or VBDoubleType or VBFixedStringType or VBStringType
                => DetermineOperatorEffectiveTypeResult.Success(VBDoubleType.TypeInfo),

            VBSingleType or VBDoubleType or VBFixedStringType or VBStringType
                when frame[InputIndex.BinaryRightOperand].TypeInfo is VBCurrencyType
                => DetermineOperatorEffectiveTypeResult.Success(VBDoubleType.TypeInfo),

            VBDateType
                when frame[InputIndex.BinaryRightOperand].TypeInfo is VBNumericType or VBFixedStringType or VBStringType or VBDateType
                => DetermineOperatorEffectiveTypeResult.Success(VBDoubleType.TypeInfo),

            VBNumericType or VBFixedStringType or VBStringType or VBDateType
                when frame[InputIndex.BinaryRightOperand].TypeInfo is VBDateType
                => DetermineOperatorEffectiveTypeResult.Success(VBDoubleType.TypeInfo),

            _ => DetermineOperatorEffectiveTypeResult.NotApplicable()
        };

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        IVBExecutionContext runtime,
        SemanticContext<ArithmeticOperatorSemanticFlags> context,
        VBBinaryOperatorExpression<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame) => frame.EffectiveType switch
        {
            VBNumericType numericEffectiveType
                when frame[InputIndex.BinaryLeftOperand] is VBNumericTypedValue lhsNumeric
                  && frame[InputIndex.BinaryRightOperand] is VBNumericTypedValue rhsNumeric
                    => EvaluateBinaryExpressionResult(numericEffectiveType, expression.ResultSymbol, lhsNumeric, rhsNumeric),

            VBNullType => EvaluateNullBinaryExpressionResult(expression.ResultSymbol),

            _ => RuntimeSemanticsEvaluationResult.InternalError()
        };
}
