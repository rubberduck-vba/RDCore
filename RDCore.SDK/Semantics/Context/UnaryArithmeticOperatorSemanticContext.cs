using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.SDK.Semantics.Context;

public sealed record class UnaryArithmeticOperatorSemanticContext : SemanticContext<ArithmeticOperatorSemanticFlags>
{
    public ConversionOperationSemanticContext UnaryOperandConversionContext { get; } = new();
}
