using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Semantics.Context;

public sealed record class UnaryLogicalOperatorSemanticContext : SemanticContext<LogicalOperatorSemanticFlags>
{
    public ConversionOperationSemanticContext UnaryOperandConversionContext { get; } = new();
}
