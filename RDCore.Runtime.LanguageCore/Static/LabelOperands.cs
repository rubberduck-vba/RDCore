using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.4.2.12, §5.4.2.16, §5.4.4.1, §5.4.4.2
// a label is not a symbol: its operand is read with these helpers, never evaluated as an expression.
internal static class LabelOperands
{
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
