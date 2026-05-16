using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Semantics.Static.Abstract;

namespace RDCore.Semantics.Static.Operators;

/// <summary>
/// MS-VBAL 5.6.9.3.3 Binary '-' Operator
/// </summary>
internal record class BinarySubtractionOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
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