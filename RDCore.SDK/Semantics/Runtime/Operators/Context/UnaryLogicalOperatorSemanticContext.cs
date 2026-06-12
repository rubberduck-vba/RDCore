using RDCore.SDK.Semantics.Flags;

namespace RDCore.SDK.Semantics.Runtime.Operators.Context
{
    public sealed record class UnaryLogicalOperatorSemanticContext : SemanticContext<LogicalOperatorSemanticFlags>
    {
        public ConversionOperationSemanticContext UnaryOperandConversionContext { get; } = new();
    }
}
