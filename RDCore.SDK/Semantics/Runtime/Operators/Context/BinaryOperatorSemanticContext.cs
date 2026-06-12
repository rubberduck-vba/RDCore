namespace RDCore.SDK.Semantics.Runtime.Operators.Context
{
    public record class BinaryOperatorSemanticContext<TFlags> : OperatorSemanticContext<TFlags> where TFlags : struct, Enum
    {
        public ConversionOperationSemanticContext LeftOperandConversionContext { get; init; } = new();
        public ConversionOperationSemanticContext RightOperandConversionContext { get; init; } = new();
    }
}
