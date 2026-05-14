using RDCore.Parsing.Model.Expressions.Operators.StaticSemantics.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticSemantics;

/// <summary>
/// MS-VBAL 5.6.9.3.3 Binary '-' Operator
/// </summary>
internal record class SubtractionOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
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