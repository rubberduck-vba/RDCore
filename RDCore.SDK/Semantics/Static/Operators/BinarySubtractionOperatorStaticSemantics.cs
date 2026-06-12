using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators
{
    /// <summary>
    /// <strong>MS-VBAL 5.6.9.3.3</strong> Binary '-' Operator
    /// </summary>
    public record class BinarySubtractionOperatorStaticSemantics : BinaryArithmeticOperatorStaticSemantics
    {
        protected override VBType? DetermineOperatorStaticType(IVBExecutionContext context, VBType lhs, VBType rhs)
        {
            return lhs switch
            {
                VBDateType when rhs is VBDateType => VBDoubleType.TypeInfo,
                _ => base.DetermineOperatorStaticType(context, lhs, rhs)
            };
        }
    }
}