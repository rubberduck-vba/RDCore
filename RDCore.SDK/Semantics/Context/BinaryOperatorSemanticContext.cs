using RDCore.SDK.Semantics.Context.Abstract;

namespace RDCore.SDK.Semantics.Context;

public record class BinaryOperatorSemanticContext<TFlags> : OperatorSemanticContext<TFlags> where TFlags : struct, Enum
{
    public ConversionOperationSemanticContext LeftOperandConversionContext { get; init; } = new();
    public ConversionOperationSemanticContext RightOperandConversionContext { get; init; } = new();
}
