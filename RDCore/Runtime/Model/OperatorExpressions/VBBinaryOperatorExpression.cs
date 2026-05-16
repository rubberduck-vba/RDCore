using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values.Abstract;

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

    public VBType? EvaluateStaticSemantics() => Symbol.StaticSemantics.DetermineDeclaredType(Left.StaticType, Right.StaticType);
    public VBTypedValue? EvaluateRuntimeSemantics(VBExecutionContext context) => Symbol.RuntimeSemantics.Evaluate(context, this, Left.RuntimeValue!, Right.RuntimeValue!);
}
