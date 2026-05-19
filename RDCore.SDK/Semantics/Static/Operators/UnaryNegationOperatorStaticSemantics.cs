using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3.1</strong> Unary '-' Operator (static semantics)
/// </summary>
public sealed record class UnaryNegationOperatorStaticSemantics : UnaryArithmeticOperatorStaticSemantics
{
    protected override VBType? DetermineOperatorStaticType(VBType operand)
    {
        if (operand is VBByteType)
        {
            return VBIntegerType.TypeInfo;
        }

        return base.DetermineOperatorStaticType(operand);
    }
}
