using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.LetCoercion;
using RDCore.SDK.Semantics.Runtime.Operators.Context;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.SDK.Semantics.Runtime.Operators.Semantics.Logical;

/// <summary>
/// <strong>MS-VBAL 5.6.9.8.1</strong> <c>Not</c> operator.
/// </summary>
public record class UnaryNotOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionProvider, 
    IVerboseMessageBuilder FormatterService) 
    : UnaryLogicalOperatorRuntimeSemantics(LetCoercionProvider, FormatterService)
{
    /// <summary>
    /// Evaluates the numeric result of a unary logical/bitwise operation.
    /// </summary>
    /// <param name="operand">The underlying managed value of a numeric unary expression operand.</param>
    protected override double EvaluateBitwiseOp(double operand) => ~(long)operand;

    /// <summary>
    /// Evaluates a resulting <c>VBTypedValue</c> for a given <c>BoundExpression</c>.
    /// </summary>
    /// <param name="runtime">The current execution context..</param>
    /// <param name="context">The semantic context of this operation, built by <c>Analyze</c>.</param>
    /// <param name="expression">Any <c>BoundExpression</c> to be evaluated.</param>
    /// <param name="frame">The <see cref="OperatorEvaluationFrame"/> holding the semantic evaluation inputs.</param>
    protected override RuntimeSemanticsEvaluationResult EvaluateExpressionResult(IVBExecutionContext runtime,
        SemanticContext<LogicalOperatorSemanticFlags> context,
        VBOperatorExpression<UnaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> expression,
        OperatorEvaluationFrame frame) =>
        frame.EffectiveType switch
        {
            VBNumericType numericEffectiveType when frame.EffectiveType is IIntegralNumericType && frame[OperandIndex.UnaryOperand] is VBNumericTypedValue numericOperand 
                => RuntimeSemanticsEvaluationResult.Success(EvaluateRuntimeSemantics(numericEffectiveType, expression.ResultSymbol, numericOperand)),

            VBNullType nullEffectiveType when frame[OperandIndex.UnaryOperand] is VBNullValue nullOperand 
                => RuntimeSemanticsEvaluationResult.Success(EvaluateRuntimeSemantics(nullEffectiveType, expression.ResultSymbol, nullOperand)),

            _ => RuntimeSemanticsEvaluationResult.InternalError(),
        };
}