using RDCore.Parsing.Model.Expressions.Operators.StaticContext;
using RDCore.Parsing.Model.Expressions.Operators.StaticContext.Abstract;
using RDCore.Runtime.Model.Operators.RuntimeSemantics;
using RDCore.Server.ProtocolExtensions;

namespace RDCore.Parsing.Model.Expressions.Operators;

internal abstract record class OperatorSymbol : StaticSymbol
{
    protected OperatorSymbol(string token, StaticSemantics staticSemantics, RuntimeSemantics executionSemantics)
        : base(token, SymbolKindExt.Operator)
    {
        StaticSemantics = staticSemantics;
        RuntimeSemantics = executionSemantics;
    }

    public StaticSemantics StaticSemantics { get; init; }
    public RuntimeSemantics RuntimeSemantics { get; init; }
}

internal abstract record class UnaryOperatorSymbol : OperatorSymbol
{
    protected UnaryOperatorSymbol(string token, StaticSemantics staticSemantics, RuntimeSemantics executionSemantics)
        : base(token, staticSemantics, executionSemantics)
    {
    }
}

internal abstract record class BinaryOperatorSymbol : OperatorSymbol
{
    protected BinaryOperatorSymbol(string name, StaticSemantics staticSemantics, RuntimeSemantics executionSemantics)
        : base(name, staticSemantics, executionSemantics)
    {
    }
}

internal record class AdditionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.AdditionOp), new AdditionOperatorStaticSemantics(), new AdditionOperatorRuntimeSemantics()) { }
internal record class SubtractionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.SubtractionOp), new SubtractionOperatorStaticSemantics(), new SubtractionOperatorRuntimeSematics()) { }
internal record class MultiplicationOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.MultiplicationOp), new MultiplicationOperatorStaticSemantics(), new MultiplicationOperatorRuntimeSemantics()) { }
internal record class DivisionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.DivisionOp), new DivisionOperatorStaticSemantics(), new DivisionOperatorRuntimeSemantics()) { }
internal record class IntegerDivisionOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.IntegerDivisionOp), new IntegerDivisionOperatorStaticSematics(), new IntegerDivisionOperatorRuntimeSemantics()) { }
internal record class ExponentiationOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.PowerOp), new ExponentOperatorStaticSemantics(), new ExponentOperatorRuntimeSemantics()) { }
internal record class ModuloOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.ModuloOp), new IntegerDivisionOperatorStaticSematics(), new ModuloOperatorRuntimeSemantics()) { }
internal record class ConcatOperatorSymbol() : BinaryOperatorSymbol(nameof(Tokens.ConcatOp), new ConcatOperatorStaticSemantics(), new ConcatOperatorRuntimeSemantics()) { }

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