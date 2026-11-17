using RDCore.SDK.Model;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// A binary operator compiled code applies to <c>Variant</c> operands (<see cref="VariantOperations.Binary(VariantOperator, VariantPayload, VariantPayload, out VariantPayload)"/>):
/// an arithmetic operator (<strong>MS-VBAL §5.6.9.3</strong>), a comparison (<strong>MS-VBAL §5.6.9.5</strong>) or a logical operator
/// (<strong>MS-VBAL §5.6.9.8</strong>). Another operator is named by its token (<see cref="VariantOperations.Binary(string, VariantPayload, VariantPayload, out VariantPayload)"/>).
/// </summary>
public enum VariantOperator
{
    /// <summary>
    /// <c>+</c>, the sum of both operands (<strong>MS-VBAL §5.6.9.3.2</strong>).
    /// </summary>
    Add,

    /// <summary>
    /// Binary <c>-</c>, the difference of both operands (<strong>MS-VBAL §5.6.9.3.3</strong>).
    /// </summary>
    Subtract,

    /// <summary>
    /// <c>*</c>, the product of both operands (<strong>MS-VBAL §5.6.9.3.4</strong>).
    /// </summary>
    Multiply,

    /// <summary>
    /// <c>/</c>, the quotient of both operands, done in a floating-point type (<strong>MS-VBAL §5.6.9.3.5</strong>).
    /// </summary>
    Divide,

    /// <summary>
    /// <c>\</c>, the quotient of both operands rounded to integers, truncated to an integer (<strong>MS-VBAL §5.6.9.3.6</strong>).
    /// </summary>
    IntegerDivide,

    /// <summary>
    /// <c>Mod</c>, the remainder of the integer division of both operands rounded to integers (<strong>MS-VBAL §5.6.9.3.6</strong>).
    /// </summary>
    Modulo,

    /// <summary>
    /// <c>=</c>, whether both operands are equal (<strong>MS-VBAL §5.6.9.5.1</strong>).
    /// </summary>
    Equal,

    /// <summary>
    /// <c>&lt;&gt;</c>, whether both operands are not equal (<strong>MS-VBAL §5.6.9.5.2</strong>).
    /// </summary>
    NotEqual,

    /// <summary>
    /// <c>&lt;</c>, whether the left operand is less than the right (<strong>MS-VBAL §5.6.9.5.3</strong>).
    /// </summary>
    Less,

    /// <summary>
    /// <c>&lt;=</c>, whether the left operand is less than or equal to the right (<strong>MS-VBAL §5.6.9.5.5</strong>).
    /// </summary>
    LessOrEqual,

    /// <summary>
    /// <c>&gt;</c>, whether the left operand is greater than the right (<strong>MS-VBAL §5.6.9.5.4</strong>).
    /// </summary>
    Greater,

    /// <summary>
    /// <c>&gt;=</c>, whether the left operand is greater than or equal to the right (<strong>MS-VBAL §5.6.9.5.6</strong>).
    /// </summary>
    GreaterOrEqual,

    /// <summary>
    /// <c>And</c>, the bitwise conjunction of both operands (<strong>MS-VBAL §5.6.9.8.2</strong>).
    /// </summary>
    And,

    /// <summary>
    /// <c>Or</c>, the bitwise disjunction of both operands (<strong>MS-VBAL §5.6.9.8.3</strong>).
    /// </summary>
    Or,

    /// <summary>
    /// <c>Xor</c>, the bitwise exclusive disjunction of both operands (<strong>MS-VBAL §5.6.9.8.4</strong>).
    /// </summary>
    Xor,

    /// <summary>
    /// <c>Eqv</c>, the bitwise equivalence of both operands (<strong>MS-VBAL §5.6.9.8.5</strong>).
    /// </summary>
    Eqv,

    /// <summary>
    /// <c>Imp</c>, the bitwise implication of the right operand by the left (<strong>MS-VBAL §5.6.9.8.6</strong>).
    /// </summary>
    Imp,
}

/// <summary>
/// The token of a <see cref="VariantOperator"/>, and the operator of a token.
/// </summary>
internal static class VariantOperators
{
    /// <summary>
    /// How many operators there are.
    /// </summary>
    public const int Count = (int)VariantOperator.Imp + 1;

    private static readonly string[] _tokens =
    [
        Tokens.AdditionOp, Tokens.SubtractionOp, Tokens.MultiplicationOp, Tokens.DivisionOp, Tokens.IntegerDivisionOp, Tokens.ModuloOp,
        Tokens.CompareEqualOp, Tokens.CompareNotEqualOp, Tokens.CompareLessThanOp, Tokens.CompareLessThanOrEqualOp, Tokens.CompareGreaterThanOp,
        Tokens.CompareGreaterThanOrEqualOp,
        Tokens.LogicalAndOp, Tokens.LogicalOrOp, Tokens.LogicalXOrOp, Tokens.LogicalEqvOp, Tokens.LogicalImpOp,
    ];

    /// <summary>
    /// The token of <paramref name="operator"/>, as an operation's plan names it.
    /// </summary>
    /// <param name="operator">The operator.</param>
    /// <returns>Its token.</returns>
    public static string TokenOf(VariantOperator @operator) => _tokens[(int)@operator];

    /// <summary>
    /// The operator <paramref name="token"/> is.
    /// </summary>
    /// <param name="token">The operator's token.</param>
    /// <returns>The operator; <see langword="null"/> for one that is named by its token.</returns>
    public static VariantOperator? Of(string token) => token switch
    {
        Tokens.AdditionOp => VariantOperator.Add,
        Tokens.SubtractionOp => VariantOperator.Subtract,
        Tokens.MultiplicationOp => VariantOperator.Multiply,
        Tokens.DivisionOp => VariantOperator.Divide,
        Tokens.IntegerDivisionOp => VariantOperator.IntegerDivide,
        Tokens.ModuloOp => VariantOperator.Modulo,
        Tokens.CompareEqualOp => VariantOperator.Equal,
        Tokens.CompareNotEqualOp => VariantOperator.NotEqual,
        Tokens.CompareLessThanOp => VariantOperator.Less,
        Tokens.CompareLessThanOrEqualOp => VariantOperator.LessOrEqual,
        Tokens.CompareGreaterThanOp => VariantOperator.Greater,
        Tokens.CompareGreaterThanOrEqualOp => VariantOperator.GreaterOrEqual,
        Tokens.LogicalAndOp => VariantOperator.And,
        Tokens.LogicalOrOp => VariantOperator.Or,
        Tokens.LogicalXOrOp => VariantOperator.Xor,
        Tokens.LogicalEqvOp => VariantOperator.Eqv,
        Tokens.LogicalImpOp => VariantOperator.Imp,
        _ => null,
    };

    /// <summary>
    /// Whether <paramref name="operator"/> is a comparison, which <see cref="ComparisonOf"/> names.
    /// </summary>
    /// <param name="operator">The operator.</param>
    /// <returns>Whether it is a comparison.</returns>
    public static bool IsComparison(VariantOperator @operator) => @operator is >= VariantOperator.Equal and <= VariantOperator.GreaterOrEqual;

    /// <summary>
    /// Whether <paramref name="operator"/> is a logical operator, which <see cref="LogicalOf"/> names.
    /// </summary>
    /// <param name="operator">The operator.</param>
    /// <returns>Whether it is a logical operator.</returns>
    public static bool IsLogical(VariantOperator @operator) => @operator >= VariantOperator.And;

    /// <summary>
    /// The comparison <paramref name="operator"/> is.
    /// </summary>
    /// <param name="operator">The operator, a comparison (<see cref="IsComparison"/>).</param>
    /// <returns>The comparison.</returns>
    public static ComparisonOperator ComparisonOf(VariantOperator @operator) => (ComparisonOperator)(@operator - VariantOperator.Equal);

    /// <summary>
    /// The logical operator <paramref name="operator"/> is.
    /// </summary>
    /// <param name="operator">The operator, a logical operator (<see cref="IsLogical"/>).</param>
    /// <returns>The logical operator.</returns>
    public static LogicalOperator LogicalOf(VariantOperator @operator) => (LogicalOperator)(@operator - VariantOperator.And);
}
