using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3.3</strong> Binary '-' Operator
/// </summary>
public record class BinarySubtractionOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
{
    protected override VBType? DetermineOperatorStaticType(VBType lhs, VBType rhs)
    {
        if (lhs is VBDateType && rhs is VBDateType)
        {
            return VBDoubleType.TypeInfo;
        }

        return base.DetermineOperatorStaticType(lhs, rhs);
    }
}