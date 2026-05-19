using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Model.Expressions.Operators;

/// <summary>
/// A unary operator expression operates with a single <c>ValuedExpression</c> operand.
/// </summary>
public record class VBUnaryOperatorExpression : VBOperatorExpression
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
