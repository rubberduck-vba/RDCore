using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Operators;

namespace RDCore.SDK.Model.Symbols.Operators;

public record class AdditionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.AdditionOp), new BinaryAdditionOperatorStaticSemantics(), new BinaryAdditionOperatorRuntimeSemantics()) { }
public record class SubtractionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.SubtractionOp), new BinarySubtractionOperatorStaticSemantics(), new BinarySubtractionOperatorRuntimeSematics()) { }
public record class MultiplicationOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.MultiplicationOp), new BinaryMultiplicationOperatorStaticSemantics(), new BinaryMultiplicationOperatorRuntimeSemantics()) { }
public record class DivisionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.DivisionOp), new BinaryDivisionOperatorStaticSemantics(), new BinaryDivisionOperatorRuntimeSemantics()) { }
public record class IntegerDivisionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.IntegerDivisionOp), new BinaryIntegerDivisionOperatorStaticSematics(), new BinaryIntegerDivisionOperatorRuntimeSemantics()) { }
public record class ExponentiationOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.PowerOp), new BinaryExponentOperatorStaticSemantics(), new BinaryExponentOperatorRuntimeSemantics()) { }
public record class ModuloOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.ModuloOp), new BinaryIntegerDivisionOperatorStaticSematics(), new BinaryModuloOperatorRuntimeSemantics()) { }
public record class ConcatOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.ConcatOp), new BinaryConcatOperatorStaticSemantics(), new BinaryConcatOperatorRuntimeSemantics()) { }

//public record class ParenthesizedExpressionOperatorSymbol() : UnaryOperatorSymbol("ParensOp", ParenthesisOperatorStaticSemantics.Semantics, ParenthesisOperatorRuntimeSemantics.Semantics) { }
//public record class UnaryPlusOperatorSymbol() : UnaryOperatorSymbol("UnaryPlusOp", UnaryPlusOperatorStaticSemantics.Semantics, UnaryPlusOperatorRuntimeSemantics.Semantics) { }
public record class UnaryNegationOperatorSymbol() : UnaryOperatorSymbol("NegationOp", new UnaryNegationOperatorStaticSemantics(), new UnaryNegationOperatorRuntimeSemantics()) { }
public record class BitwiseNotOperatorSymbol() : UnaryOperatorSymbol(nameof(Tokens.LogicalNotOp), new UnaryLogicalOperatorStaticSemantics(), new UnaryNotOperatorRuntimeSemantics()) { }
public record class BitwiseAndOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalAndOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryAndBitwiseOperatorRuntimeSemantics()) { }
public record class BitwiseOrOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalOrOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryOrBitwiseOperatorRuntimeSemantics()) { }
public record class BitwiseXOrOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalXOrOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryXorBitwiseOperatorRuntimeSemantics()) { }
public record class BitwiseImpOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalImpOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryImpBitwiseOperatorRuntimeSemantics()) { }
public record class BitwiseEqvOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalEqvOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryEqvBitwiseOperatorRuntimeSemantics()) { }

public record class EqualityOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareEqualOp), new BinaryRelationalOperatorStaticSemantics(), new EqualityRelationalOperatorRuntimeSemantics()) { }
public record class InequalityOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareNotEqualOp), new BinaryRelationalOperatorStaticSemantics(), new InequalityRelationalOperatorRuntimeSemantics()) { }
public record class LessThanOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareLessThanOp), new BinaryRelationalOperatorStaticSemantics(), new LessThanRelationalOperatorRuntimeSemantics()) { }
public record class GreaterThanOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareGreaterThanOp), new BinaryRelationalOperatorStaticSemantics(), new GreaterThanRelationalOperatorRuntimeSemantics()) { }
public record class LessThanOrEqualOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareLessThanOrEqualOp), new BinaryRelationalOperatorStaticSemantics(), new LessThanEqRelationalOperatorRuntimeSemantics()) { }
public record class GreaterThanOrEqualOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareGreaterThanOrEqualOp), new BinaryRelationalOperatorStaticSemantics(), new GreaterThanEqRelationalOperatorRuntimeSemantics()) { }

public record class LikeOperatorSymbol(): BinaryOperatorSymbol(nameof(Tokens.Like), new BinaryRelationalOperatorStaticSemantics(), new LikeRelationalOperatorRuntimeSemantics()) { }

public record class IsRefEqOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareIsOp), new BinaryIsRefEqOperatorStaticSemantics(), new IsRefEqRelationalOperatorRuntimeSemantics()) { }
//public record class MemberAccessOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.MemberAccess), default!, default!) { }