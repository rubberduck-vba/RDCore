using RDCore.SDK.Semantics.Context.Abstract;

namespace RDCore.SDK.Semantics.Context;

/// <summary>
/// The semantic context of a binary operator expression.
/// </summary>
/// <typeparam name="TFlags">The specific type of semantic flags in this context.</typeparam>
public record class BinaryOperatorSemanticContext<TFlags> : OperatorSemanticContext<TFlags> where TFlags : struct, Enum
{
    /// <summary>
    /// Gets the conversion semantic context of the <em>left-hand side</em> operand.
    /// </summary>
    public ConversionOperationSemanticContext LeftOperandConversionContext => ConversionContextOf(InputIndex.BinaryLeftOperand);

    /// <summary>
    /// Gets the conversion semantic context of the <em>right-hand side</em> operand.
    /// </summary>
    public ConversionOperationSemanticContext RightOperandConversionContext => ConversionContextOf(InputIndex.BinaryRightOperand);
}
