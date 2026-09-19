using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Runtime.Semantics.LetCoercion;

/// <summary>
/// Which operand of an <em>operator expression</em> a conversion fact is about: the operand of a unary operator, or the left or
/// right operand of a binary one.
/// </summary>
internal static class OperandPositionFlags
{
    public static ConversionSemanticFlags Of(VBOperatorExpression expression, InputIndex operandIndex) => expression switch
    {
        VBUnaryOperatorExpressionNode when operandIndex == InputIndex.UnaryOperand
            => ConversionSemanticFlags.UnaryOperand,

        VBBinaryOperatorExpressionNode when operandIndex == InputIndex.BinaryLeftOperand
            => ConversionSemanticFlags.BinaryLeftOperand,

        VBBinaryOperatorExpressionNode when operandIndex == InputIndex.BinaryRightOperand
            => ConversionSemanticFlags.BinaryRightOperand,

        _ => 0
    };
}
