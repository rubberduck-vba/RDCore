using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Expressions.Operators;
using RDCore.Parsing.Model.Values;

namespace RDCore.Runtime.Model.Operators;

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

    public override VBTypedValue Evaluate(VBExecutionContext context)
    {
        var lhsValue = Left.Evaluate(context);
        var rhsValue = Right.Evaluate(context);
        return Symbol.Execute(context, this, lhsValue, rhsValue);
    }
}
