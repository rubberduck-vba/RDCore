using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Semantics.Context;

public sealed record class BinaryLogicalOperatorSemanticContext : OperatorSemanticContext<LogicalOperatorSemanticFlags>
{
    public ConversionOperationSemanticContext BinaryLeftOperandConversionContext { get; } = new();
    public ConversionOperationSemanticContext BinaryRightOperandConversionContext { get; } = new();
}