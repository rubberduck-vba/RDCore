using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators
{
    /// <summary>
    /// <strong>MS-VBAL 5.6.9.3.5</strong> Binary '/' Operator (static semantics)
    /// </summary>
    public sealed record class BinaryDivisionOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
    {
        protected override VBType? DetermineOperatorStaticType(IVBExecutionContext context, VBType lhs, VBType rhs)
        {
            return lhs switch
            {
                VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType 
                    when rhs is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType => VBDoubleType.TypeInfo,

                VBDoubleType or VBFixedStringType or VBStringType or VBCurrencyType or VBDateType when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType => VBDoubleType.TypeInfo,
                VBNumericType or VBFixedStringType or VBStringType or VBDateType when rhs is VBDoubleType or VBFixedStringType or VBStringType or VBCurrencyType or VBDateType => VBDoubleType.TypeInfo,

                _ => base.DetermineOperatorStaticType(context, lhs, rhs)
            };
        }
    }
}
