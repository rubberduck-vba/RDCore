using RDCore.SDK.Model.AST.Expressions;
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
/// MS-VBAL 5.6.9.8.5 Binary 'Eqv' Operator
/// </summary>
public record class BinaryEqvLogicalOperatorRuntimeSemantics(
    ILetCoercionRuntimeSemanticsProvider LetCoercionSemanticsProvider,
    IVerboseMessageBuilder FormatterService)
    : BinaryLogicalOperatorRuntimeSemantics(LetCoercionSemanticsProvider, FormatterService)
{
    protected override double EvaluateBitwiseOp(int lhs, int rhs) => ~((long)lhs ^ (long)rhs);

    protected override RuntimeSemanticsEvaluationResult EvaluateSemanticallly(
        IVBExecutionContext context, 
        VBBinaryOperatorExpression<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> expression, 
        OperatorEvaluationFrame frame)
    {
        var lhs = frame[InputIndex.BinaryLeftOperand];
        var rhs = frame[InputIndex.BinaryRightOperand];
        return lhs switch
        {
            VBTypedValue when lhs.TypeInfo is IIntegralNumericType && rhs is VBNullValue
                => EvaluateNullBinaryExpressionResult(expression.ResultSymbol),

            VBNullValue when rhs.TypeInfo is IIntegralNumericType 
                => EvaluateNullBinaryExpressionResult(expression.ResultSymbol),

            _ => RuntimeSemanticsEvaluationResult.InternalError()
        };
    }
}
