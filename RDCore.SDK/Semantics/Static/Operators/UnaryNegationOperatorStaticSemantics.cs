using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static.Operators
{
    /// <summary>
    /// <strong>MS-VBAL 5.6.9.3.1</strong> Unary '-' Operator (static semantics)
    /// </summary>
    public sealed record class UnaryNegationOperatorStaticSemantics : UnaryArithmeticOperatorStaticSemantics
    {
        protected override VBType? DetermineOperatorStaticType(IVBExecutionContext context, VBType operand)
        {
            return operand switch
            {
                VBByteType => VBIntegerType.TypeInfo,
                _ => base.DetermineOperatorStaticType(context, operand)
            };
        }
    }
}
