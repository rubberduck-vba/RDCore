using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators
{
    /// <summary>
    /// <strong>MS-VBAL 5.6.9.3.6</strong> Binary '\' Operator and 'Mod' Operator (static semantics)
    /// </summary>
    public sealed record class BinaryIntegerDivisionOperatorStaticSematics : BinaryArithmeticOperatorStaticSemantics
    {
        protected override VBType? DetermineOperatorStaticType(IVBExecutionContext context, VBType lhs, VBType rhs)
        {
            return lhs switch
            {
                IFloatingPointNumericType or IFixedPointNumericType or VBFixedStringType or VBStringType or VBDateType
                    when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType => VBLongType.TypeInfo,

                VBNumericType or VBFixedStringType or VBStringType or VBDateType
                    when rhs is IFloatingPointNumericType or IFixedPointNumericType or VBFixedStringType or VBStringType or VBDateType => VBLongType.TypeInfo,

                // these *should* be covered by the above...
                VBSingleType when rhs is VBByteType or VBBooleanType or VBIntegerType => VBLongType.TypeInfo,
                VBByteType or VBBooleanType or VBIntegerType when rhs is VBSingleType => VBLongType.TypeInfo,

                VBSingleType when rhs is VBLongType or VBLongLongType => VBLongType.TypeInfo,
                VBLongType or VBLongLongType when rhs is VBSingleType => VBLongType.TypeInfo,

                VBDoubleType or VBFixedStringType or VBStringType when rhs is IIntegralNumericType or IFloatingPointNumericType or VBFixedStringType or VBStringType => VBLongType.TypeInfo,
                IIntegralNumericType or IFloatingPointNumericType or VBFixedStringType or VBStringType when rhs is VBDoubleType or VBFixedStringType or VBStringType => VBLongType.TypeInfo,

                VBCurrencyType when rhs is VBNumericType or VBFixedStringType or VBStringType => VBLongType.TypeInfo,
                VBNumericType or VBFixedStringType or VBStringType when rhs is (VBCurrencyType) => VBLongType.TypeInfo,

                VBDateType when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType => VBLongType.TypeInfo,
                VBNumericType or VBFixedStringType or VBStringType or VBDateType when rhs is VBDateType => VBLongType.TypeInfo,

                _ => base.DetermineOperatorStaticType(context, lhs, rhs)
            };
        }
    }
}