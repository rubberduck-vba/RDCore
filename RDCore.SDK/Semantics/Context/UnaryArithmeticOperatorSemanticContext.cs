using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.SDK.Semantics.Context;

public sealed record class UnaryArithmeticOperatorSemanticContext : OperatorSemanticContext<ArithmeticOperatorSemanticFlags>
{
    /// <summary>
    /// Gets the conversion semantic context of the operand.
    /// </summary>
    public ConversionOperationSemanticContext UnaryOperandConversionContext => ConversionContextOf(InputIndex.UnaryOperand);
}
