using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators
{
    /// <summary>
    /// <strong>MS-VBAL 5.6.9.3.2</strong> Binary '+' Operator (static semantics)
    /// </summary>
    public sealed record class BinaryAdditionOperatorStaticSemantics() : BinaryArithmeticOperatorStaticSemantics()
    {
        protected override VBType? DetermineOperatorStaticType(IVBExecutionContext context, VBType lhs, VBType rhs)
        {
            return lhs switch
            {
                VBStringType when rhs is VBStringType => VBStringType.TypeInfo,
                _ => base.DetermineOperatorStaticType(context, lhs, rhs)
            };
        }
    }
}
