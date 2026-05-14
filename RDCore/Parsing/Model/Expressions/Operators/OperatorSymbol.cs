using RDCore.Server.ProtocolExtensions;
using static RDCore.Parsing.Model.Expressions.Operators.SymbolOperation;

namespace RDCore.Parsing.Model.Expressions.Operators;

internal abstract record class OperatorSymbol : StaticSymbol
{
    protected OperatorSymbol(string token, UnaryOperation unaryOp)
        : base(token, SymbolKindExt.Operator)
    {
        ExecuteUnaryOp = unaryOp;
    }

    protected OperatorSymbol(string token, BinaryOperation binaryOp)
        : base(token, SymbolKindExt.Operator)
    {
        ExecuteBinaryOp = binaryOp;
    }

    public virtual UnaryOperation ExecuteUnaryOp { get; init; } = default!;
    public virtual BinaryOperation ExecuteBinaryOp { get; init; } = default!;
}

internal abstract record class UnaryOperatorSymbol : OperatorSymbol
{
    protected UnaryOperatorSymbol(string token, UnaryOperation operation)
        : base(token, operation)
    {
    }
}

internal abstract record class BinaryOperatorSymbol : OperatorSymbol
{
    protected BinaryOperatorSymbol(string name, BinaryOperation operation)
        : base(name, operation)
    {
    }
}

internal abstract record class BitwiseOperatorSymbol : BinaryOperatorSymbol
{
    protected BitwiseOperatorSymbol(string name, BinaryOperation operation)
        : base(name, operation)
    {
    }
}

internal record class AdditionOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.AdditionOp}", SymbolOperation.EvaluateBinaryAddition) { }
internal record class SubtractionOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.SubtractionOp}", SymbolOperation.EvaluateBinarySubtraction) { }
internal record class MultiplicationOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.MultiplicationOp}", SymbolOperation.EvaluateBinaryMultiplication) { }
internal record class DivisionOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.DivisionOp}", SymbolOperation.EvaluateBinaryDivision) { }
internal record class IntegerDivisionOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.IntegerDivisionOp}", SymbolOperation.EvaluateBinaryIntegerDivision) { }
internal record class ExponentiationOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.PowerOp}", SymbolOperation.EvaluateBinaryExponentiation) { }
internal record class ModuloOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.ModuloOp}", SymbolOperation.EvaluateBinaryModulo) { }
internal record class ConcatOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.ConcatOp}", SymbolOperation.EvaluateBinaryConcat) { }

internal record class ParenthesizedExpressionOperatorSymbol() : UnaryOperatorSymbol("UnaryParens", SymbolOperation.EvaluateUnaryParentheses) { }
internal record class UnaryPlusOperatorSymbol() : UnaryOperatorSymbol($"UnaryOp{Tokens.AdditionOp}", SymbolOperation.EvaluateUnaryPlus) { }
internal record class UnaryMinusOperatorSymbol() : UnaryOperatorSymbol($"UnaryOp{Tokens.SubtractionOp}", SymbolOperation.EvaluateUnaryMinus) { }
internal record class BitwiseNotOperatorSymbol() : UnaryOperatorSymbol($"UnaryOp{Tokens.LogicalNotOp}", SymbolOperation.EvaluateUnaryBitwiseNot) { }
internal record class BitwiseAndOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.LogicalAndOp}", SymbolOperation.EvaluateBinaryBitwiseAnd) { }
internal record class BitwiseOrOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.LogicalOrOp}", SymbolOperation.EvaluateBinaryBitwiseOr) { }
internal record class BitwiseXOrOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.LogicalXOrOp}", SymbolOperation.EvaluateBinaryBitwiseXOr) { }
internal record class BitwiseImpOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.LogicalImpOp}", SymbolOperation.EvaluateBinaryBitwiseImp) { }
internal record class BitwiseEqvOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.LogicalEqvOp}", SymbolOperation.EvaluateBinaryBitwiseEqv) { }

internal record class EqualityOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.CompareEqualOp}", default!) { }
internal record class InequalityOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.CompareNotEqualOp}", default!) { }
internal record class LessThanOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.CompareLessThanOp}", default!) { }
internal record class GreaterThanOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.CompareGreaterThanOp}", default!) { }
internal record class LessThanOrEqualOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.CompareLessThanOrEqualOp}", default!) { }
internal record class GreaterThanOrEqualOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.CompareGreaterThanOrEqualOp}", default!) { }

internal record class IsRefEqOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.CompareIsOp}", SymbolOperation.EvaluateBinaryIsRefEquality) { }
internal record class MemberAccessOperatorSymbol() : BinaryOperatorSymbol($"BinaryOp{Tokens.MemberAccess}", SymbolOperation.EvaluateBinaryMemberAccess) { }