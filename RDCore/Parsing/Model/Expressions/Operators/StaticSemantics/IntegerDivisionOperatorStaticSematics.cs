using RDCore.Parsing.Model.Expressions.Operators.StaticSemantics.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticSemantics;

/// <summary>
/// MS-VBAL 5.6.9.3.6 Binary '\' Operator and 'Mod' Operator (static semantics)
/// </summary>
internal record class IntegerDivisionOperatorStaticSematics : BinaryArithmeticOperatorStaticSemantics
{
    protected override VBType? DetermineOperatorStaticType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            IFloatingPointNumericType or IFixedPointNumericType or VBStringType or VBFixedStringType or VBDateType
                when rhs is INumericType or VBStringType or VBFixedStringType or VBDateType => VBLongType.TypeInfo,

            INumericType or VBStringType or VBFixedStringType or VBDateType
                when rhs is IFloatingPointNumericType or IFixedPointNumericType or VBStringType or VBFixedStringType or VBDateType => VBLongType.TypeInfo,

            _ => base.DetermineOperatorStaticType(lhs, rhs)
        };
    }
}