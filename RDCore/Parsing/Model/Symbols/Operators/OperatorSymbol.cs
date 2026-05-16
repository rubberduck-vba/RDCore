using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Semantics.Runtime.Operators;
using RDCore.Semantics.Static.Abstract;
using RDCore.Semantics.Static.Operators;

namespace RDCore.Parsing.Model.Expressions.Operators;

internal record class AdditionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.AdditionOp), new BinaryAdditionOperatorStaticSemantics(), new BinaryAdditionOperatorRuntimeSemantics()) { }
internal record class SubtractionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.SubtractionOp), new BinarySubtractionOperatorStaticSemantics(), new BinarySubtractionOperatorRuntimeSematics()) { }
internal record class MultiplicationOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.MultiplicationOp), new BinaryMultiplicationOperatorStaticSemantics(), new BinaryMultiplicationOperatorRuntimeSemantics()) { }
internal record class DivisionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.DivisionOp), new BinaryDivisionOperatorStaticSemantics(), new BinaryDivisionOperatorRuntimeSemantics()) { }
internal record class IntegerDivisionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.IntegerDivisionOp), new BinaryIntegerDivisionOperatorStaticSematics(), new BinaryIntegerDivisionOperatorRuntimeSemantics()) { }
internal record class ExponentiationOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.PowerOp), new BinaryExponentOperatorStaticSemantics(), new BinaryExponentOperatorRuntimeSemantics()) { }
internal record class ModuloOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.ModuloOp), new BinaryIntegerDivisionOperatorStaticSematics(), new BinaryModuloOperatorRuntimeSemantics()) { }
internal record class ConcatOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.ConcatOp), new BinaryConcatOperatorStaticSemantics(), new BinaryConcatOperatorRuntimeSemantics()) { }

//internal record class ParenthesizedExpressionOperatorSymbol() : UnaryOperatorSymbol("ParensOp", ParenthesisOperatorStaticSemantics.Semantics, ParenthesisOperatorRuntimeSemantics.Semantics) { }
//internal record class UnaryPlusOperatorSymbol() : UnaryOperatorSymbol("UnaryPlusOp", UnaryPlusOperatorStaticSemantics.Semantics, UnaryPlusOperatorRuntimeSemantics.Semantics) { }
internal record class UnaryNegationOperatorSymbol() : UnaryOperatorSymbol("NegationOp", new UnaryNegationOperatorStaticSemantics(), new UnaryNegationOperatorRuntimeSemantics()) { }
internal record class BitwiseNotOperatorSymbol() : UnaryOperatorSymbol(nameof(Tokens.LogicalNotOp), new UnaryLogicalOperatorStaticSemantics(), new UnaryNotOperatorRuntimeSemantics()) { }
internal record class BitwiseAndOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalAndOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryAndBitwiseOperatorRuntimeSemantics()) { }
internal record class BitwiseOrOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalOrOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryOrBitwiseOperatorRuntimeSemantics()) { }
internal record class BitwiseXOrOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalXOrOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryXorBitwiseOperatorRuntimeSemantics()) { }
internal record class BitwiseImpOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalImpOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryImpBitwiseOperatorRuntimeSemantics()) { }
internal record class BitwiseEqvOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.LogicalEqvOp), new BinaryLogicalOperatorStaticSemantics(), new BinaryEqvBitwiseOperatorRuntimeSemantics()) { }

internal record class EqualityOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareEqualOp), new BinaryRelationalOperatorStaticSemantics(), new EqualityRelationalOperatorRuntimeSemantics()) { }
internal record class InequalityOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareNotEqualOp), new BinaryRelationalOperatorStaticSemantics(), new InequalityRelationalOperatorRuntimeSemantics()) { }
internal record class LessThanOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareLessThanOp), new BinaryRelationalOperatorStaticSemantics(), new LessThanRelationalOperatorRuntimeSemantics()) { }
internal record class GreaterThanOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareGreaterThanOp), new BinaryRelationalOperatorStaticSemantics(), new GreaterThanRelationalOperatorRuntimeSemantics()) { }
internal record class LessThanOrEqualOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareLessThanOrEqualOp), new BinaryRelationalOperatorStaticSemantics(), new LessThanEqRelationalOperatorRuntimeSemantics()) { }
internal record class GreaterThanOrEqualOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareGreaterThanOrEqualOp), new BinaryRelationalOperatorStaticSemantics(), new GreaterThanEqRelationalOperatorRuntimeSemantics()) { }

internal record class LikeOperatorSymbol(): BinaryOperatorSymbol(nameof(Tokens.Like), new BinaryRelationalOperatorStaticSemantics(), new LikeRelationalOperatorRuntimeSemantics()) { }

internal record class IsRefEqOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.CompareIsOp), new IsRefEqOperatorStaticSemantics(), new IsRefEqRelationalOperatorRuntimeSemantics()) { }
//internal record class MemberAccessOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.MemberAccess), default!, default!) { }