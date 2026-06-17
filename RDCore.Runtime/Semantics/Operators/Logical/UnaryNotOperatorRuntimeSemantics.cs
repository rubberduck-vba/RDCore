using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Runtime.Semantics.Operators.Logical;

/// <summary>
/// <strong>MS-VBAL 5.6.9.8.1</strong> <c>Not</c> operator.
/// </summary>
public record class UnaryNotOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider, 
    IVerboseMessageBuilder FormatterService) 
    : UnaryLogicalOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    protected override double EvaluateBitwiseOp(double operand) => ~(long)operand;

    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(IVBExecutionContext runtime,
        SemanticContext<LogicalOperatorSemanticFlags> context,
        VBOperatorExpression<UnaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame) =>
        frame.EffectiveType switch
        {
            VBNumericType numericEffectiveType when frame.EffectiveType is IIntegralNumericType && frame[InputIndex.UnaryOperand] is VBNumericTypedValue numericOperand 
                => RuntimeSemanticsEvaluationResult.Success(EvaluateRuntimeSemantics(numericEffectiveType, expression.ResultSymbol, numericOperand)),

            VBNullType nullEffectiveType when frame[InputIndex.UnaryOperand] is VBNullValue nullOperand 
                => RuntimeSemanticsEvaluationResult.Success(EvaluateRuntimeSemantics(nullEffectiveType, expression.ResultSymbol, nullOperand)),

            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };
}