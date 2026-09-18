using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Reads the operand of a statement that names a jump target: <c>GoTo</c>, <c>GoSub</c>,
/// <c>On…GoTo</c>, <c>On…GoSub</c>, <c>On Error GoTo</c> and <c>Resume</c> (<strong>MS-VBAL
/// §5.4.2.12</strong>–<strong>§5.4.2.16</strong>, <strong>§5.4.4.1</strong>, <strong>§5.4.4.2</strong>).
/// </summary>
/// <remarks>
/// The grammar admits any <c>expression</c> there, so the AST carries an <see cref="ExpressionNode"/>
/// even though MS-VBAL's <c>statement-label</c> is only an identifier or a line number. A label is not
/// a symbol: its operand must be read with these helpers, never evaluated as an expression.
/// </remarks>
internal static class LabelOperands
{
    /// <summary>
    /// Reads <paramref name="operand"/> as the name of a label: an identifier, or a non-negative
    /// integer literal, which is the name a line number label is defined under.
    /// </summary>
    /// <param name="operand">The operand of a jump statement.</param>
    /// <param name="name">The label name the operand refers to, if it has the shape of one.</param>
    /// <returns><c>false</c> if the operand is not shaped like a label reference at all.</returns>
    public static bool TryGetLabelName(ExpressionNode operand, [NotNullWhen(true)] out string? name)
    {
        switch (operand)
        {
            case SimpleNameExpressionNode simpleName:
                name = simpleName.IdentifierName;
                return true;
            case LiteralExpressionNode literal when TryGetIntegralValue(literal, out var number) && number >= 0:
                name = number.ToString(CultureInfo.InvariantCulture);
                return true;
            default:
                name = null;
                return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="operand"/> is the integer constant <paramref name="value"/>: a literal, or
    /// the negation of one — <c>-1</c> is a unary minus over the literal <c>1</c>, not a literal itself.
    /// </summary>
    /// <param name="operand">The operand of a jump statement.</param>
    /// <param name="value">The constant to test for.</param>
    public static bool IsIntegerConstant(ExpressionNode operand, long value)
    {
        switch (operand)
        {
            case LiteralExpressionNode literal:
                return TryGetIntegralValue(literal, out var number) && number == value;
            case VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp, Operand: LiteralExpressionNode literal }:
                return TryGetIntegralValue(literal, out var negated) && -negated == value;
            default:
                return false;
        }
    }

    private static bool TryGetIntegralValue(LiteralExpressionNode literal, out long value)
    {
        value = 0;
        if (!literal.StaticValue.Handle.BindingCapabilities.HasFlag(BindingCapabilities.GetValue))
        {
            return false;
        }

        switch (literal.StaticValue)
        {
            case VBIntegerValue integer:
                value = integer.Value;
                return true;
            case VBLongValue @long:
                value = @long.Value;
                return true;
            case VBLongLongValue longLong:
                value = longLong.Value;
                return true;
            default:
                return false;
        }
    }
}
