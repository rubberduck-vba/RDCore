using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Semantics.Precompiler;

/// <summary>
/// Constant-folds a conditional-compilation expression (<strong>MS-VBAL §5.6.16.2</strong>) — a
/// <c>#If</c>/<c>#ElseIf</c> condition, or a <c>#Const</c> right-hand side — against the workspace's
/// conditional-compilation constants. <strong>MS-VBAL §5.6.16.2</strong>: "the constant value ... is
/// determined statically by evaluating the expression as if it was being evaluated at runtime with
/// conditional compilation constants being replaced by their defined values."
/// </summary>
/// <remarks>
/// This is a small, standalone evaluator, not a reuse of the ordinary runtime operator semantics
/// (<c>RDCore.Runtime</c>): the SDK cannot depend on the GPLv3 runtime project, and this needs no
/// session, coercion pipeline, or error-info plumbing — just a value. Only the operator subset most
/// real-world <c>#If</c> conditions actually use is implemented: the six comparisons, the five logical
/// operators (<c>And</c>/<c>Or</c>/<c>Not</c>/<c>Xor</c>/<c>Eqv</c>/<c>Imp</c>), and
/// <c>+</c>/<c>-</c>/<c>*</c>/<c>/</c>/<c>Mod</c>. <c>&amp;</c>, <c>^</c>, <c>\</c>, <c>Is</c>, <c>Like</c>,
/// and every intrinsic function (<c>CInt</c>, <c>CBool</c>, ...) are not — an expression using one of
/// them, or naming an undefined/unresolvable constant, is reported as indeterminate rather than
/// evaluated wrong: whether a branch is live must never be guessed.
/// </remarks>
public static class PrecompilerConstantExpressionEvaluator
{
    /// <summary>
    /// Evaluates <paramref name="expression"/> to its constant <see cref="VBTypedValue"/>.
    /// </summary>
    /// <param name="resolver">Resolves a <see cref="PrecompilerNameExpressionNode"/> against the workspace's conditional-compilation constants.</param>
    /// <param name="expression">The expression to fold — a <c>#If</c>/<c>#ElseIf</c> condition or a <c>#Const</c> right-hand side.</param>
    /// <param name="value">The folded value, when the whole expression could be determined.</param>
    /// <returns><c>false</c> when any part of the expression could not be determined.</returns>
    public static bool TryEvaluate(ISymbolResolver resolver, ExpressionNode expression, out VBTypedValue? value)
    {
        switch (expression)
        {
            case LiteralExpressionNode literal:
                value = literal.StaticValue;
                return true;

            case ConditionalExpressionNode { Inputs: [ExpressionNode inner, ..] }:
                return TryEvaluate(resolver, inner, out value);

            case PrecompilerNameExpressionNode name:
                return TryResolveConstant(resolver, name.Name, out value);

            case VBUnaryOperatorExpressionNode unary:
                return TryEvaluateUnary(resolver, unary, out value);

            case VBBinaryOperatorExpressionNode binary:
                return TryEvaluateBinary(resolver, binary, out value);

            default:
                value = null;
                return false;
        }
    }

    /// <summary>
    /// Evaluates <paramref name="expression"/> and Let-coerces the result to its <c>Boolean</c> truth
    /// value — the shape a <c>#If</c>/<c>#ElseIf</c> condition needs.
    /// </summary>
    /// <returns><c>false</c> when the expression, or its coercion to <c>Boolean</c>, could not be determined.</returns>
    public static bool TryEvaluateBoolean(ISymbolResolver resolver, ExpressionNode expression, out bool value)
    {
        value = false;
        return TryEvaluate(resolver, expression, out var result) && result is not null && TryCoerceBoolean(result, out value);
    }

    private static bool TryResolveConstant(ISymbolResolver resolver, string name, out VBTypedValue? value)
    {
        var result = resolver.ResolveValue(name, ScopeKind.Global, StaticSymbol.GlobalUri);
        if (result.Symbol is PrecompilerConstantSymbol constant)
        {
            value = constant.Value;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryEvaluateUnary(ISymbolResolver resolver, VBUnaryOperatorExpressionNode unary, out VBTypedValue? value)
    {
        value = null;
        if (!TryEvaluate(resolver, unary.Operand, out var operand) || operand is null)
        {
            return false;
        }

        switch (unary.Token)
        {
            case Tokens.NegationOp when TryGetDouble(operand, out var number):
                value = new VBDoubleValue(-number);
                return true;
            case Tokens.LogicalNotOp when TryCoerceBoolean(operand, out var truth):
                value = new VBBooleanValue(!truth);
                return true;
            default:
                return false;
        }
    }

    private static bool TryEvaluateBinary(ISymbolResolver resolver, VBBinaryOperatorExpressionNode binary, out VBTypedValue? value)
    {
        value = null;
        if (!TryEvaluate(resolver, binary.Left, out var left) || left is null
            || !TryEvaluate(resolver, binary.Right, out var right) || right is null)
        {
            return false;
        }

        // MS-VBAL 5.6.16.2's expression grammar admits string comparison (= <>) alongside the numeric
        // one; every other comparison/arithmetic operator here works over the numeric representation.
        if (left is VBStringValue leftString && right is VBStringValue rightString
            && binary.Token is Tokens.CompareEqualOp or Tokens.CompareNotEqualOp)
        {
            var equal = string.Equals(leftString.Value, rightString.Value, StringComparison.Ordinal);
            value = new VBBooleanValue(binary.Token == Tokens.CompareEqualOp ? equal : !equal);
            return true;
        }

        switch (binary.Token)
        {
            case Tokens.LogicalAndOp or Tokens.LogicalOrOp or Tokens.LogicalXOrOp or Tokens.LogicalEqvOp or Tokens.LogicalImpOp:
                if (!TryCoerceBoolean(left, out var leftTruth) || !TryCoerceBoolean(right, out var rightTruth))
                {
                    return false;
                }
                value = new VBBooleanValue(binary.Token switch
                {
                    Tokens.LogicalAndOp => leftTruth && rightTruth,
                    Tokens.LogicalOrOp => leftTruth || rightTruth,
                    Tokens.LogicalXOrOp => leftTruth ^ rightTruth,
                    Tokens.LogicalEqvOp => leftTruth == rightTruth,
                    Tokens.LogicalImpOp => !leftTruth || rightTruth,
                    _ => false,
                });
                return true;

            default:
                if (!TryGetDouble(left, out var leftNumber) || !TryGetDouble(right, out var rightNumber))
                {
                    return false;
                }
                switch (binary.Token)
                {
                    case Tokens.CompareEqualOp: value = new VBBooleanValue(leftNumber == rightNumber); return true;
                    case Tokens.CompareNotEqualOp: value = new VBBooleanValue(leftNumber != rightNumber); return true;
                    case Tokens.CompareGreaterThanOp: value = new VBBooleanValue(leftNumber > rightNumber); return true;
                    case Tokens.CompareGreaterThanOrEqualOp: value = new VBBooleanValue(leftNumber >= rightNumber); return true;
                    case Tokens.CompareLessThanOp: value = new VBBooleanValue(leftNumber < rightNumber); return true;
                    case Tokens.CompareLessThanOrEqualOp: value = new VBBooleanValue(leftNumber <= rightNumber); return true;
                    case Tokens.AdditionOp: value = new VBDoubleValue(leftNumber + rightNumber); return true;
                    case Tokens.SubtractionOp: value = new VBDoubleValue(leftNumber - rightNumber); return true;
                    case Tokens.MultiplicationOp: value = new VBDoubleValue(leftNumber * rightNumber); return true;
                    case Tokens.DivisionOp or Tokens.IntegerDivisionOp when rightNumber != 0:
                        value = new VBDoubleValue(binary.Token == Tokens.DivisionOp ? leftNumber / rightNumber : Math.Truncate(leftNumber / rightNumber));
                        return true;
                    case Tokens.ModuloOp when rightNumber != 0:
                        value = new VBDoubleValue(leftNumber % rightNumber);
                        return true;
                    default:
                        return false;
                }
        }
    }

    private static bool TryCoerceBoolean(VBTypedValue value, out bool result)
    {
        if (value is VBBooleanValue boolean)
        {
            result = boolean.Value.StoredValue != 0;
            return true;
        }

        if (TryGetDouble(value, out var number))
        {
            result = number != 0;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryGetDouble(VBTypedValue value, out double result)
    {
        switch (value)
        {
            case VBBooleanValue boolean: result = boolean.Value.StoredValue != 0 ? -1 : 0; return true;
            case VBIntegerValue integer: result = integer.Value; return true;
            case VBLongValue longValue: result = longValue.Value; return true;
            case VBLongLongValue longLong: result = longLong.Value; return true;
            case VBSingleValue single: result = single.Value; return true;
            case VBDoubleValue @double: result = @double.Value; return true;
            default: result = 0; return false;
        }
    }
}
