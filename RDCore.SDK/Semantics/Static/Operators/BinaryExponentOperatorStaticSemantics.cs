using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators
{
    /// <summary>
    /// <strong>MS-VBAL 5.6.9.3.7</strong> Binary '^' Operator (static semantics)
    /// </summary>
    public sealed record class BinaryExponentOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
    {
        protected override VBType? DetermineOperatorStaticType(IVBExecutionContext context, VBType lhs, VBType rhs)
        {
            return lhs switch
            {
                VBNumericType or VBFixedStringType or VBStringType or VBDateType when rhs is VBNumericType or VBFixedStringType or VBStringType or VBDateType => VBDoubleType.TypeInfo,

                _ => base.DetermineOperatorStaticType(context, lhs, rhs)
            };
        }
    }
}
