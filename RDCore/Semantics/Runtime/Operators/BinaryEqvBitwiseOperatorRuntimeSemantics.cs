using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;
using RDCore.Semantics.Runtime.Abstract;

namespace RDCore.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.8.5 Binary 'Eqv' Operator
/// </summary>
internal record class BinaryEqvBitwiseOperatorRuntimeSemantics : BinaryBitwiseOperatorRuntimeSemantics
{
    protected override int EvaluateBitwise(int lhs, int rhs)
    {
        unchecked // TODO verify if this should be allowed to overflow (edge case)
        {
            return Convert.ToInt32(~((long)lhs ^ (long)rhs));
        }
    }

    protected override VBTypedValue? EvaluateSemanticallly(VBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        return lhs switch
        {
            VBTypedValue when lhs.TypeInfo is IIntegralNumericType && rhs is VBNullValue => VBNullValue.Null,
            VBNullValue when rhs.TypeInfo is IIntegralNumericType => VBNullValue.Null,
            _ => default
        };
    }
}
