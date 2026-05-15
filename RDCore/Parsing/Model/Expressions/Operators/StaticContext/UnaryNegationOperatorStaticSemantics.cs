using RDCore.Parsing.Model.Expressions.Operators.StaticContext.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticContext;

/// <summary>
/// MS-VBAL 5.6.9.3.1 Unary '-' Operator (static semantics)
/// </summary>
internal sealed record class UnaryNegationOperatorStaticSemantics : UnaryArithmeticOperatorStaticSemantics
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
