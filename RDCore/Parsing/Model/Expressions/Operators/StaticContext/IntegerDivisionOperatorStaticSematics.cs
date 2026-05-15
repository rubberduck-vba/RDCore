using RDCore.Parsing.Model.Expressions.Operators.StaticContext.Abstract;
using RDCore.Parsing.Model.Types;

namespace RDCore.Parsing.Model.Expressions.Operators.StaticContext;

/// <summary>
/// MS-VBAL 5.6.9.3.6 Binary '\' Operator and 'Mod' Operator (static semantics)
/// </summary>
internal sealed record class IntegerDivisionOperatorStaticSematics : BinaryArithmeticOperatorStaticSemantics
{
    protected override VBType? DetermineOperatorStaticType(VBType lhs, VBType rhs)
    {
        return lhs switch
        {
            IFloatingPointNumericType or IFixedPointNumericType or VBStringType or VBFixedStringType or VBDateType
                when rhs is INumericType or VBStringType or VBFixedStringType or VBDateType => VBLongType.TypeInfo,

            INumericType or VBStringType or VBFixedStringType or VBDateType
                when rhs is IFloatingPointNumericType or IFixedPointNumericType or VBStringType or VBFixedStringType or VBDateType => VBLongType.TypeInfo,

            // these *should* be covered by the above...
            VBSingleType when rhs is VBByteType or VBBooleanType or VBIntegerType => VBLongType.TypeInfo,
            VBByteType or VBBooleanType or VBIntegerType when rhs is VBSingleType => VBLongType.TypeInfo,

            VBSingleType when rhs is VBLongType or VBLongLongType => VBLongType.TypeInfo,
            VBLongType or VBLongLongType when rhs is VBSingleType => VBLongType.TypeInfo,

            VBDoubleType or VBStringType or VBFixedStringType when rhs is IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBFixedStringType => VBLongType.TypeInfo,
            IIntegralNumericType or IFloatingPointNumericType or VBStringType or VBFixedStringType when rhs is VBDoubleType or VBStringType or VBFixedStringType => VBLongType.TypeInfo,

            VBCurrencyType when rhs is INumericType or VBStringType or VBFixedStringType => VBLongType.TypeInfo,
            INumericType or VBStringType or VBFixedStringType when rhs is (VBCurrencyType) => VBLongType.TypeInfo,

            VBDateType when rhs is INumericType or VBStringType or VBFixedStringType or VBDateType => VBLongType.TypeInfo,
            INumericType or VBStringType or VBFixedStringType or VBDateType when rhs is VBDateType => VBLongType.TypeInfo,

            _ => base.DetermineOperatorStaticType(lhs, rhs)
        };
    }
}