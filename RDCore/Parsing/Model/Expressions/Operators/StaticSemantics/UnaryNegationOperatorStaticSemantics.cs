using RDCore.Parsing.Model.Expressions.Operators.StaticSemantics.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticSemantics;

/// <summary>
/// MS-VBAL 5.6.9.3.1 Unary '-' Operator (static semantics)
/// </summary>
internal record class UnaryNegationOperatorStaticSemantics : UnaryArithmeticOperatorStaticSemantics
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
