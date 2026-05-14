using RDCore.Parsing.Model.Expressions.Operators.StaticSemantics.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticSemantics;

/// <summary>
/// MS-VBAL 5.6.9.3.5 Binary '/' Operator (static semantics)
/// </summary>
internal record class DivisionOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
{
    protected override VBType? DetermineOperatorStaticType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType
                when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType => VBDoubleType.TypeInfo,

            VBDoubleType or VBStringType or VBFixedStringType or VBCurrencyType or VBDateType
                when rhs is INumericType or VBStringType or VBFixedStringType or VBDateType => VBDoubleType.TypeInfo,

            INumericType or VBStringType or VBFixedStringType or VBDateType 
                when rhs is VBDoubleType or VBStringType or VBFixedStringType or VBCurrencyType or VBDateType => VBDoubleType.TypeInfo,

            _ => base.DetermineOperatorStaticType(lhs, rhs)
        };
    }
}
