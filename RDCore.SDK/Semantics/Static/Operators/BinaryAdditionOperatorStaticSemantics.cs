using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators;

/// <summary>
/// <strong>MS-VBAL 5.6.9.3.2</strong> Binary '+' Operator (static semantics)
/// </summary>
public sealed record class BinaryAdditionOperatorStaticSemantics() : BinaryArithmeticOperatorStaticSemantics()
{
    protected override VBType? DetermineOperatorStaticType(VBType lhs, VBType rhs)
    {
        if (lhs is VBStringType && rhs is VBStringType)
        {
            return VBStringType.TypeInfo;
        }

        return base.DetermineOperatorStaticType(lhs, rhs);
    }
}
