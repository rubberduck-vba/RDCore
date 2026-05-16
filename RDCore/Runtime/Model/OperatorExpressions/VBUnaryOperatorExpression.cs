using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values.Abstract;

namespace RDCore.Runtime.Model.Operators;

internal record class VBUnaryOperatorExpression : VBOperatorExpression
{
    public VBUnaryOperatorExpression(OperatorSymbol symbol, Location location, ValuedExpression expression)
        : base(symbol, location)
    {
        Expression = expression;
    }

    public ValuedExpression Expression { get; init; }

    public VBType? EvaluateStaticSemantics() => Symbol.StaticSemantics.DetermineDeclaredType(Expression.StaticType);
    public VBTypedValue? EvaluateRuntimeSemantics(VBExecutionContext context) => Symbol.RuntimeSemantics.Evaluate(context, this, Expression.RuntimeValue!);
}
