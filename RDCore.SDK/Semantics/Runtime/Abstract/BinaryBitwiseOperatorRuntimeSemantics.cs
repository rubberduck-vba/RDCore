using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.SDK.Semantics.Runtime.Abstract;

public abstract record class BinaryBitwiseOperatorRuntimeSemantics : BinaryOperatorRuntimeSemantics
{
    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (lhs.TypeInfo is IIntegralNumericType && rhs.TypeInfo is IIntegralNumericType)
        {
            //context.AddDiagnostic(RDCoreDiagnostic.BitwiseOperator(expression.Location.Range));
            if (CoerceAndUnwrapNumericValue(lhs) is double lhsValue && CoerceAndUnwrapNumericValue(rhs) is double rhsValue)
            {
                var bitwiseResult = EvaluateBitwise(Convert.ToInt32(lhsValue), Convert.ToInt32(rhsValue));
                return new VBIntegerValue(expression.Symbol).WithValue(bitwiseResult);
            }
        }

        if (lhs is VBNullValue && rhs is VBNullValue)
        {
            return VBNullValue.Null;
        }

        return EvaluateSemanticallly(context, expression, effectiveType, lhs, rhs);
    }

    /// <summary>
    /// Evaluates the bitwise evaluation branch of the MS-VBAL specifications for a logical operator.
    /// </summary>
    /// <remarks>
    /// Operands are <strong>explicitly specified</strong> as being evaluated bitwise only given specific operands.
    /// </remarks>
    protected abstract int EvaluateBitwise(int lhs, int rhs);
    /// <summary>
    /// Evaluates the not-bitwise evaluation branches of the MS-VBAL specifications for a logical operator.
    /// </summary>
    /// <remarks>
    /// Operands are <strong>explicitly specified</strong> as being evaluated bitwise only given specific operands.
    /// Base implementation has already handled the case where both operands are integers, and the case where they're both <c>Null</c>.
    /// </remarks>
    protected abstract VBTypedValue? EvaluateSemanticallly(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs);
}
