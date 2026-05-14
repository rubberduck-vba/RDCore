using RDCore.Parsing.Model.Expressions.Operators.StaticSemantics.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticSemantics;

/// <summary>
/// MS-VBAL 5.6.9.3.2 Binary '+' Operator (static semantics)
/// </summary>
internal record class AdditionOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
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
