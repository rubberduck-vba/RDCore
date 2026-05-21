using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;

namespace RDCore.SDK.Semantics.Runtime.Operators;

/// <summary>
/// MS-VBAL 5.6.9.8.2 Binary 'And' Operator
/// </summary>
public record class BinaryAndBitwiseOperatorRuntimeSemantics : BinaryBitwiseOperatorRuntimeSemantics
{
    protected override int EvaluateBitwise(int lhs, int rhs) => lhs & rhs;

    protected override VBTypedValue? EvaluateSemanticallly(IVBExecutionContext context, VBBinaryOperatorExpression expression, VBType effectiveType, VBTypedValue lhs, VBTypedValue rhs)
    {
        if (lhs is VBNumericTypedValue lhsNumeric && rhs is VBNullValue)
        {
            if (lhsNumeric.ManagedValue == 0)
            {
                return VBIntegerType.Zero;
            }
            else
            {
                return VBNullValue.Null;
            }
        }
        
        if (rhs is VBNumericTypedValue rhsNumeric && lhs is VBNullValue)
        {
            if (rhsNumeric.ManagedValue == 0)
            {
                return VBIntegerType.Zero;
            }
            else
            {
                return VBNullValue.Null;
            }
        }

        return default;
    }
}
