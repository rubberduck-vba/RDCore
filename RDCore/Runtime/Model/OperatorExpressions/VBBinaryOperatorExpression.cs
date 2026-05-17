using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Semantics.Runtime.Abstract;
using RDCore.Semantics.Static.Abstract;

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

    public override StaticSemantics StaticSemantics => Symbol.StaticSemantics;
    public override RuntimeSemantics RuntimeSemantics => Symbol.RuntimeSemantics;
}
