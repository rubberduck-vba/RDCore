using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Expressions.Operators;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;

namespace RDCore.Runtime.Model.Operators;

internal record class VBUnaryOperatorExpression : VBOperatorExpression
{
    public VBUnaryOperatorExpression(OperatorSymbol symbol, Location location, ValuedExpression expression)
        : base(symbol, location)
    {
        Expression = expression;
    }

    public ValuedExpression Expression { get; init; }

    public VBType? EvaluateStaticSemantics() => Symbol.StaticSemantics.DetermineDeclaredType(Expression.StaticDeclaredType);
    public VBTypedValue? EvaluateRuntimeSemantics(VBExecutionContext context) => Symbol.RuntimeSemantics.Evaluate(context, this, Expression.ResultValue!);
}

internal record class VBBinaryOperatorExpression : VBOperatorExpression
{
    public VBBinaryOperatorExpression(OperatorSymbol symbol, ValuedExpression left, ValuedExpression right, Location location)
        : base(symbol, location)
    {
        Left = left;
        Right = right;
    }

    public ValuedExpression Left { get; init; }
    public ValuedExpression Right { get; init; }

    public VBType? EvaluateStaticSemantics() => Symbol.StaticSemantics.DetermineDeclaredType(Left.StaticDeclaredType, Right.StaticDeclaredType);
    public VBTypedValue? EvaluateRuntimeSemantics(VBExecutionContext context)
    {
        return Symbol.RuntimeSemantics.Evaluate(context, this, Left.ResultValue!, Right.ResultValue!);
    }
}

internal abstract record VBBitwiseOperatorExpression(OperatorSymbol symbol, ValuedExpression left, ValuedExpression right, Location location)
    : VBBinaryOperatorExpression(symbol, left, right, location)
{ }
