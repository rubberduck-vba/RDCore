namespace RDCore.SDK.Semantics.Runtime.Operators.Context
{
    public sealed record class UnaryArithmeticOperatorSemanticContext : SemanticContext<ArithmeticOperatorSemanticFlags>
    {
        public ConversionOperationSemanticContext UnaryOperandConversionContext { get; } = new();
    }
}
