using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Semantics.Static.Abstract;

namespace RDCore.Semantics.Static.Operators;

/// <summary>
/// MS-VBAL 5.6.9.3.5 Binary '/' Operator (static semantics)
/// </summary>
internal sealed record class BinaryDivisionOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
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
