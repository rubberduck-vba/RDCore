using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics.Runtime;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Operators;

namespace RDCore.SDK.Model.Symbols.Operators;

/// <summary>
/// The addition ('+') binary (infix) operator static symbol.
/// </summary>
public sealed record class AdditionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.AdditionOp), new BinaryAdditionOperatorStaticSemantics(), new BinaryAdditionOperatorRuntimeSemantics()) { }
/// <summary>
/// The subtraction ('-') binary operator static symbol.
/// </summary>
public record class SubtractionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.SubtractionOp), new BinarySubtractionOperatorStaticSemantics(), new BinarySubtractionOperatorRuntimeSematics()) { }
/// <summary>
/// The multiplication ('*') binary operator static symbol.
/// </summary>
public record class MultiplicationOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.MultiplicationOp), new BinaryMultiplicationOperatorStaticSemantics(), new BinaryMultiplicationOperatorRuntimeSemantics()) { }
/// <summary>
/// The division ('/') binary operator static symbol.
/// </summary>
public record class DivisionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.DivisionOp), new BinaryDivisionOperatorStaticSemantics(), new BinaryDivisionOperatorRuntimeSemantics()) { }
/// <summary>
/// The integer division ('\') binary operator static symbol.
/// </summary>
public record class IntegerDivisionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.IntegerDivisionOp), new BinaryIntegerDivisionOperatorStaticSematics(), new BinaryIntegerDivisionOperatorRuntimeSemantics()) { }
/// <summary>
/// The exponentiation ('^') binary operator static symbol.
/// </summary>
public record class ExponentiationOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.PowerOp), new BinaryExponentOperatorStaticSemantics(), new BinaryExponentOperatorRuntimeSemantics()) { }
/// <summary>
/// The modulo ('Mod') binary operator static symbol.
/// </summary>
public record class ModuloOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.ModuloOp), new BinaryIntegerDivisionOperatorStaticSematics(), new BinaryModuloOperatorRuntimeSemantics()) { }
/// <summary>
/// The concatenation ('&') binary operator static symbol.
/// </summary>
public record class ConcatOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.ConcatOp), new BinaryConcatOperatorStaticSemantics(), new BinaryConcatOperatorRuntimeSemantics()) { }
/// <summary>
/// The unary let-coercion ('()') operator static symbol.
/// </summary>
/// <remarks>
/// NOTE: This deviates from MS-VBAL specifications, which narrowly defines unary operators in a way that excludes parenthesized expressions, but the behavior observed in MS-VBA is identical.
/// </remarks>
public record class UnaryLetCoercionOperatorSymbol() : UnaryOperatorSymbol("ParensOp", LetCoercionStaticSemantics.Instance, LetCoercionRuntimeSemantics.Instance) { }
/// <summary>
/// The unary plus ('+') prefix operator static symbol.
/// </summary>
/// <remarks>
/// NOTE: This operator is technically unspecified. Its static semantics are those of unary arithmetic operators (MS-VBAL 5.6.9.3)
/// </remarks>
public record class UnaryPlusOperatorSymbol() : UnaryOperatorSymbol("UnaryPlusOp", new UnaryArithmeticOperatorStaticSemantics(), new UnaryPlusOperatorRuntimeSemantics()) { }
/// <summary>
/// The unary negation ('-') prefix operator static symbol.
/// </summary>
public record class UnaryNegationOperatorSymbol() : UnaryOperatorSymbol("NegationOp", new UnaryNegationOperatorStaticSemantics(), new UnaryNegationOperatorRuntimeSemantics()) { }
/// <summary>
/// The bitwise/logical <c>Not</c> operator static symbol.
/// </summary>
public record class BitwiseNotOperatorSymbol() : UnaryOperatorSymbol(nameof(Tokens.LogicalNotOp), new UnaryLogicalOperatorStaticSemantics(), new UnaryNotOperatorRuntimeSemantics()) { }
/// <summary>
/// The bitwise/logical <c>And</c> operator static symbol.
/// </summary>
public record class BitwiseAndOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalAndOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryAndBitwiseOperatorRuntimeSemantics()) { }
/// <summary>
/// The bitwise/logical <c>Or</c> operator static symbol.
/// </summary>
public record class BitwiseOrOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalOrOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryOrBitwiseOperatorRuntimeSemantics()) { }
/// <summary>
/// The bitwise/logical <c>XOr</c> operator static symbol.
/// </summary>
public record class BitwiseXOrOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalXOrOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryXorBitwiseOperatorRuntimeSemantics()) { }
/// <summary>
/// The bitwise/logical <c>Imp</c> operator static symbol.
/// </summary>
public record class BitwiseImpOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalImpOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryImpBitwiseOperatorRuntimeSemantics()) { }
/// <summary>
/// The bitwise/logical <c>Eqv</c> operator static symbol.
/// </summary>
public record class BitwiseEqvOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalEqvOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryEqvBitwiseOperatorRuntimeSemantics()) { }
/// <summary>
/// The value equality ('=') comparison operator static symbol.
/// </summary>
public record class EqualityOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareEqualOp), new BinaryRelationalOperatorStaticSemantics(), new EqualityRelationalOperatorRuntimeSemantics()) { }
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
//public record class MemberAccessOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.MemberAccess), default!, default!) { }