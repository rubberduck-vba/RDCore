using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics.Runtime;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Operators;

namespace RDCore.SDK.Model.Symbols.Operators;

/// <summary>
/// Defines the internal operators symbol names.
/// </summary>
/// <remarks>
/// These names are intentionally illegal VBA identifier names, 
/// and they should all match the regular expression <c>"___?(?&lt;opname&gt;.{2,3})_op"</c>.
/// </remarks>
public static class OperatorSymbolNames
{
    public const string BinaryArithmeticAdditionOp = "__add_op";
    public const string BinaryArithmeticDivisionOp = "__div_op";
    public const string BinaryArithmeticExponentOp = "__exp_op";
    public const string BinaryArithmeticIntegerDivisionOp = "__idv_op";
    public const string BinaryArithmeticModuloOp = "__mod_op";
    public const string BinaryArithmeticMultiplicationOp = "__mul_op";
    public const string BinaryArithmeticSubtractionOp = "__sub_op";

    public const string BinaryAssignmentValueOp = "__let_op";
    public const string BinaryAssignmentReferenceOp = "__set_op";

    public const string BinaryBitwiseAndOp = "__and_op";
    public const string BinaryBitwiseEqvOp = "__eqv_op";
    public const string BinaryBitwiseImpOp = "__imp_op";
    public const string BinaryBitwiseOrOp = "___or_op";
    public const string BinaryBitwiseXOrOp = "__xor_op";

    public const string BinaryCompareEqOp = "___eq_op";
    public const string BinaryCompareNeqOp = "__neq_op";
    public const string BinaryCompareLtOp = "___lt_op";
    public const string BinaryCompareLtEqOp = "__lte_op";
    public const string BinaryCompareGtOp = "___gt_op";
    public const string BinaryCompareGtEqOp = "__gte_op";
    public const string BinaryCompareLikeOp = "__lik_op";
    public const string BinaryCompareIsOp = "___is_op";

    public const string BinaryDictionaryAccessOp = "__bda_op";
    public const string BinaryMemberAccessOp = "__bma_op";
    public const string BinaryStringConcatOp = "__cat_op";

    public const string UnaryArithmeticAdditionOp = "__ad1_op";
    public const string UnaryArithmeticNegationOp = "__ng1_op";
    public const string UnaryArithmeticPrecedenceOp = "__p()_op";

    public const string UnaryBitwiseNotOp = "__not_op";
    public const string UnaryLetCoerceOp = "__c()_op"; // yes, it's what you think.
}

#region Arithmetic operators
/// <summary>
/// The addition ('+') binary operator static symbol.
/// </summary>
public sealed record class BinaryArithmeticAdditionOperatorSymbol() : BinaryOperatorSymbol(OperatorSymbolNames.BinaryArithmeticAdditionOp, new BinaryAdditionOperatorStaticSemantics(), new BinaryAdditionOperatorRuntimeSemantics()) { }
/// <summary>
/// The division ('/') binary operator static symbol.
/// </summary>
public record class BinaryArithmeticDivisionOperatorSymbol() : BinaryOperatorSymbol(OperatorSymbolNames.BinaryArithmeticDivisionOp, new BinaryDivisionOperatorStaticSemantics(), new BinaryDivisionOperatorRuntimeSemantics()) { }
/// <summary>
/// The exponentiation ('^') binary operator static symbol.
/// </summary>
public record class BinaryArithmeticExponentOperatorSymbol() : BinaryOperatorSymbol(OperatorSymbolNames.BinaryArithmeticExponentOp, new BinaryExponentOperatorStaticSemantics(), new BinaryExponentOperatorRuntimeSemantics()) { }
/// <summary>
/// The integer division ('\') binary operator static symbol.
/// </summary>
public record class BinaryArithmeticIntegerDivisionOperatorSymbol() : BinaryOperatorSymbol(OperatorSymbolNames.BinaryArithmeticIntegerDivisionOp, new BinaryIntegerDivisionOperatorStaticSematics(), new BinaryIntegerDivisionOperatorRuntimeSemantics()) { }
/// <summary>
/// The modulo ('Mod') binary operator static symbol.
/// </summary>
public record class BinaryArithmeticModuloOperatorSymbol() : BinaryOperatorSymbol(OperatorSymbolNames.BinaryArithmeticModuloOp, new BinaryIntegerDivisionOperatorStaticSematics(), new BinaryModuloOperatorRuntimeSemantics()) { }
/// <summary>
/// The multiplication ('*') binary operator static symbol.
/// </summary>
public record class BinaryArithmeticMultiplicationOperatorSymbol() : BinaryOperatorSymbol(OperatorSymbolNames.BinaryArithmeticMultiplicationOp, new BinaryMultiplicationOperatorStaticSemantics(), new BinaryMultiplicationOperatorRuntimeSemantics()) { }
/// <summary>
/// The subtraction ('-') binary operator static symbol.
/// </summary>
public record class BinaryArithmeticSubtractionOperatorSymbol() : BinaryOperatorSymbol(OperatorSymbolNames.BinaryArithmeticSubtractionOp, new BinarySubtractionOperatorStaticSemantics(), new BinarySubtractionOperatorRuntimeSematics()) { }

/// <summary>
/// The unary addition ('+') prefix operator static symbol.
/// </summary>
/// <remarks>
/// NOTE: This operator is technically unspecified. Its static semantics are those of unary arithmetic operators (MS-VBAL 5.6.9.3)
/// </remarks>
public record class UnaryArithmeticAdditionOperatorSymbol() : UnaryOperatorSymbol(OperatorSymbolNames.UnaryArithmeticAdditionOp, new UnaryArithmeticOperatorStaticSemantics(), new UnaryPlusOperatorRuntimeSemantics()) { }
/// <summary>
/// The unary negation ('-') prefix operator static symbol.
/// </summary>
public record class UnaryArithmeticNegationOperatorSymbol() : UnaryOperatorSymbol(OperatorSymbolNames.UnaryArithmeticNegationOp, new UnaryNegationOperatorStaticSemantics(), new UnaryNegationOperatorRuntimeSemantics()) { }
#endregion

/// <summary>
/// The concatenation ('&') binary operator static symbol.
/// </summary>
public record class BinaryStringConcatOperatorSymbol() : BinaryOperatorSymbol(OperatorSymbolNames.BinaryStringConcatOp, new BinaryConcatOperatorStaticSemantics(), new BinaryConcatOperatorRuntimeSemantics()) { }

#region Bitwise (logical) operators
/// <summary>
/// The bitwise/logical <c>And</c> operator static symbol.
/// </summary>
public record class BinaryBitwiseAndOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalAndOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryAndBitwiseOperatorRuntimeSemantics()) { }
/// <summary>
/// The bitwise/logical <c>Or</c> operator static symbol.
/// </summary>
public record class BinaryBitwiseOrOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalOrOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryOrBitwiseOperatorRuntimeSemantics()) { }
/// <summary>
/// The bitwise/logical <c>XOr</c> operator static symbol.
/// </summary>
public record class BinaryBitwiseXOrOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalXOrOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryXorBitwiseOperatorRuntimeSemantics()) { }
/// <summary>
/// The bitwise/logical <c>Imp</c> operator static symbol.
/// </summary>
public record class BinaryBitwiseImpOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalImpOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryImpBitwiseOperatorRuntimeSemantics()) { }
/// <summary>
/// The bitwise/logical <c>Eqv</c> operator static symbol.
/// </summary>
public record class BinaryBitwiseEqvOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalEqvOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryEqvBitwiseOperatorRuntimeSemantics()) { }
#endregion

#region Comparison operators
/// <summary>
/// The value equality ('=') comparison operator static symbol.
/// </summary>
public record class BinaryCompareEqOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareEqualOp), new BinaryRelationalOperatorStaticSemantics(), new EqualityRelationalOperatorRuntimeSemantics()) { }
/// <summary>
/// The value inequality ('&lt;&gt;') comparison operator static symbol.
/// </summary>
public record class InequalityOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareNotEqualOp), new BinaryRelationalOperatorStaticSemantics(), new InequalityRelationalOperatorRuntimeSemantics()) { }
/// <summary>
/// The <em>less than</em> ('&lt;') comparison operator static symbol.
/// </summary>
public record class LessThanOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareLessThanOp), new BinaryRelationalOperatorStaticSemantics(), new LessThanRelationalOperatorRuntimeSemantics()) { }
/// <summary>
/// The <em>greater than</em> ('&gt;') comparison operator static symbol.
/// </summary>
public record class GreaterThanOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareGreaterThanOp), new BinaryRelationalOperatorStaticSemantics(), new GreaterThanRelationalOperatorRuntimeSemantics()) { }
/// <summary>
/// The <em>less than or equal</em> ('&lt;=') comparison operator static symbol.
/// </summary>
public record class LessThanOrEqualOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareLessThanOrEqualOp), new BinaryRelationalOperatorStaticSemantics(), new LessThanEqRelationalOperatorRuntimeSemantics()) { }
/// <summary>
/// The <em>greater than or equal</em> ('&gt;=') comparison operator static symbol.
/// </summary>
public record class GreaterThanOrEqualOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareGreaterThanOrEqualOp), new BinaryRelationalOperatorStaticSemantics(), new GreaterThanEqRelationalOperatorRuntimeSemantics()) { }
/// <summary>
/// The <c>Like</c> string comparison operator static symbol.
/// </summary>
public record class LikeOperatorSymbol(): BinaryOperatorSymbol(nameof(Tokens.Like), new BinaryRelationalOperatorStaticSemantics(), new LikeRelationalOperatorRuntimeSemantics()) { }
/// <summary>
/// The <c>Is</c> reference equality comparison operator static symbol.
/// </summary>
public record class IsRefEqOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareIsOp), new BinaryIsRefEqOperatorStaticSemantics(), new IsRefEqRelationalOperatorRuntimeSemantics()) { }
#endregion

//public record class BinaryMemberAccessOperatorSymbol() : BinaryOperatorSymbol(nameof(OperatorSymbolNames.BinaryMemberAccessOp), default!, default!) { }

/// <summary>
/// The bitwise/logical <c>Not</c> operator static symbol.
/// </summary>
public record class UnaryBitwiseNotOperatorSymbol() : UnaryOperatorSymbol(nameof(Tokens.LogicalNotOp), new UnaryLogicalOperatorStaticSemantics(), new UnaryNotOperatorRuntimeSemantics()) { }

/// <summary>
/// The unary let-coercion ('()') operator static symbol.
/// </summary>
public record class UnaryLetCoercionOperatorSymbol() : UnaryOperatorSymbol(OperatorSymbolNames.UnaryLetCoerceOp, LetCoercionStaticSemantics.Instance, LetCoercionRuntimeSemantics.Instance) { }

