using RDCore.Parsing.Model.Expressions.Operators.StaticContext.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticContext;

/// <summary>
/// MS-VBAL 5.6.9.3.4 Binary '*' Operator (static semantics)
/// </summary>
internal sealed record class MultiplicationOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
{
    protected override VBType? DetermineOperatorStaticType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBCurrencyType when rhs is VBSingleType or VBDoubleType or VBStringType or VBFixedStringType => VBDoubleType.TypeInfo,
            VBSingleType or VBDoubleType or VBStringType or VBFixedStringType when rhs is VBCurrencyType => VBDoubleType.TypeInfo,
            VBDateType when rhs is INumericType or VBStringType or VBFixedStringType or VBDateType => VBDoubleType.TypeInfo,
            INumericType or VBStringType or VBFixedStringType or VBDateType when rhs is VBDateType => VBDoubleType.TypeInfo,
            _ => base.DetermineOperatorStaticType(lhs, rhs)
        };
    }
}
