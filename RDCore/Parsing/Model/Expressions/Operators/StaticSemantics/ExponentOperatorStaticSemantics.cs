using RDCore.Parsing.Model.Expressions.Operators.StaticSemantics.Abstract;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticSemantics;

/// <summary>
/// MS-VBAL 5.6.9.3.7 Binary '^' Operator (static semantics)
/// </summary>
internal record class ExponentOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
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
