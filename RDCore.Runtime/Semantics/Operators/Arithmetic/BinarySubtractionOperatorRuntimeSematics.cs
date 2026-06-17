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

public record class BinarySubtractionOperatorRuntimeSematics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryArithmeticOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    protected override double EvaluateManagedNumericOp(double lhs, double rhs) => lhs - rhs;

    protected override DetermineOperatorEffectiveTypeResult DetermineArithmeticOperatorEffectiveType(
        ISymbolResolver resolver,
        BinaryArithmeticOperatorSemanticContext context,
        VBBinaryOperatorExpression<BinaryArithmeticOperatorSemanticContext,
        ArithmeticOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame) => DetermineOperatorEffectiveTypeResult.NotApplicable(); // no operator-specific overrides

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(
        IVBExecutionContext runtime,
        SemanticContext<ArithmeticOperatorSemanticFlags> context,
        VBBinaryOperatorExpression<BinaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame) => frame.EffectiveType switch
        {
            VBNumericType numericEffectiveType => EvaluateBinaryExpressionResult(numericEffectiveType, expression.ResultSymbol,
                (VBNumericTypedValue)frame[InputIndex.BinaryLeftOperand],
                (VBNumericTypedValue)frame[InputIndex.BinaryRightOperand]),

            VBDateType dateEffectiveType => EvaluateBinaryExpressionResult(dateEffectiveType, expression.ResultSymbol,
                (VBNumericTypedValue)frame[InputIndex.BinaryLeftOperand],
                (VBNumericTypedValue)frame[InputIndex.BinaryRightOperand]),

            VBNullType => EvaluateNullBinaryExpressionResult(expression.ResultSymbol),

            _ => RuntimeSemanticsEvaluationResult.InternalError()
        };
}
