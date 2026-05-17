using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Semantics.Runtime.Abstract;
using RDCore.Semantics.Static.Abstract;

namespace RDCore.Runtime.Model.Operators;

internal record class VBUnaryOperatorExpression : VBOperatorExpression
{
    public VBUnaryOperatorExpression(OperatorSymbol symbol, Location location, ValuedExpression expression)
        : base(symbol, location)
    {
        Expression = expression;
    }

    public ValuedExpression Expression { get; init; }

    public override StaticSemantics StaticSemantics => Symbol.StaticSemantics;
    public override RuntimeSemantics RuntimeSemantics => Symbol.RuntimeSemantics;
}
