using RDCore.Parsing.Model.Expressions.Operators.StaticContext.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticContext;

/// <summary>
/// MS-VBAL 5.6.9.3.7 Binary '^' Operator (static semantics)
/// </summary>
internal sealed record class ExponentOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
{
    protected override VBType? DetermineOperatorStaticType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            INumericType or VBStringType or VBFixedStringType or VBDateType
                when rhs is INumericType or VBStringType or VBFixedStringType or VBDateType => VBDoubleType.TypeInfo,

            _ => base.DetermineOperatorStaticType(lhs, rhs)
        };
    }
}
