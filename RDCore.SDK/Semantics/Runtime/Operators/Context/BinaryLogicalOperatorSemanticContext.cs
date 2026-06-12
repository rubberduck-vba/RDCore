using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Semantics.Runtime.Operators.Context
{
    public sealed record class BinaryLogicalOperatorSemanticContext : OperatorSemanticContext<LogicalOperatorSemanticFlags>
    {
        public ConversionOperationSemanticContext BinaryLeftOperandConversionContext { get; } = new();
        public ConversionOperationSemanticContext BinaryRightOperandConversionContext { get; } = new();
    }
}