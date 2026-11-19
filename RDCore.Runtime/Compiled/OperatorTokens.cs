using RDCore.SDK.Model;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// The operators of an operation's plan (its token), as the payload operations name them.
/// </summary>
internal static class OperatorTokens
{
    /// <summary>The comparison <paramref name="token"/> is; <see langword="null"/> for any other operator.</summary>
    public static ComparisonOperator? ComparisonOf(string token) => token switch
    {
        Tokens.CompareEqualOp => ComparisonOperator.Equal,
        Tokens.CompareNotEqualOp => ComparisonOperator.NotEqual,
        Tokens.CompareLessThanOp => ComparisonOperator.Less,
        Tokens.CompareLessThanOrEqualOp => ComparisonOperator.LessOrEqual,
        Tokens.CompareGreaterThanOp => ComparisonOperator.Greater,
        Tokens.CompareGreaterThanOrEqualOp => ComparisonOperator.GreaterOrEqual,
        _ => null,
    };

    /// <summary>The logical operator <paramref name="token"/> is; <see langword="null"/> for any other operator.</summary>
    public static LogicalOperator? LogicalOf(string token) => token switch
    {
        Tokens.LogicalAndOp => LogicalOperator.And,
        Tokens.LogicalOrOp => LogicalOperator.Or,
        Tokens.LogicalXOrOp => LogicalOperator.Xor,
        Tokens.LogicalEqvOp => LogicalOperator.Eqv,
        Tokens.LogicalImpOp => LogicalOperator.Imp,
        _ => null,
    };

    /// <summary>
    /// The operation of payloads of an integral kind <paramref name="token"/> is - <c>+</c>, <c>-</c>, <c>*</c>, <c>\</c>, <c>Mod</c> - done in
    /// <paramref name="kind"/>; <see cref="PayloadStatus.Decline"/> for any other operator.
    /// </summary>
    public static PayloadStatus OperateIntegral(string token, ScalarKind kind, long left, long right, out long result)
    {
        if (VariantOperators.Of(token) is { } @operator)
        {
            return OperateIntegral(@operator, kind, left, right, out result);
        }

        result = 0;
        return PayloadStatus.Decline;
    }

    /// <summary>
    /// The operation of payloads of an integral kind <paramref name="operator"/> is, as <see cref="OperateIntegral(string, ScalarKind, long, long, out long)"/>
    /// does the operation of its token.
    /// </summary>
    public static PayloadStatus OperateIntegral(VariantOperator @operator, ScalarKind kind, long left, long right, out long result)
    {
        switch (@operator)
        {
            case VariantOperator.Add:
                return PayloadOperations.Add(kind, left, right, out result);
            case VariantOperator.Subtract:
                return PayloadOperations.Subtract(kind, left, right, out result);
            case VariantOperator.Multiply:
                return PayloadOperations.Multiply(kind, left, right, out result);
            case VariantOperator.IntegerDivide:
                return PayloadOperations.IntegerDivide(kind, left, right, out result);
            case VariantOperator.Modulo:
                return PayloadOperations.Modulo(kind, left, right, out result);
            default:
                result = 0;
                return PayloadStatus.Decline;
        }
    }
}
