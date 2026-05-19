using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;

namespace RDCore.SDK.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.8.4 Binary 'Xor' Operator
/// </summary>
public record class BinaryXorBitwiseOperatorRuntimeSemantics : BinaryBitwiseOperatorRuntimeSemantics
{
    protected override int EvaluateBitwise(int lhs, int rhs)
    {
        unchecked // TODO verify if this should be allowed to overflow (edge case)
        {
            return Convert.ToInt32((long)lhs ^ (long)rhs);
        }
    }

    protected override VBTypedValue? EvaluateSemanticallly(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        return lhs switch
        {
            VBTypedValue when lhs.TypeInfo is IIntegralNumericType && rhs is VBNullValue => VBNullValue.Null,
            VBNullValue when rhs.TypeInfo is IIntegralNumericType => VBNullValue.Null,
            _ => default
        };
    }
}
