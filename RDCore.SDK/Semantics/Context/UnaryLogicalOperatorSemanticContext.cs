using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Semantics.Context;

public sealed record class UnaryLogicalOperatorSemanticContext : OperatorSemanticContext<LogicalOperatorSemanticFlags>
{
    /// <summary>
    /// Gets the conversion semantic context of the operand.
    /// </summary>
    public ConversionOperationSemanticContext UnaryOperandConversionContext => ConversionContextOf(InputIndex.UnaryOperand);
}
