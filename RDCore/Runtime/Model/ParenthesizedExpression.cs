using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Semantics.Runtime.Abstract;
using RDCore.Semantics.Static.Abstract;

namespace RDCore.Runtime.Model;

internal sealed record class ParenthesizedExpression : ValuedExpression
{
    public ParenthesizedExpression(Location location, ValuedExpression innerExpression)
        : base(location)
    {
        InnerExpression = innerExpression;
    }

    public ValuedExpression InnerExpression { get; init; }

    // TODO
    public override StaticSemantics StaticSemantics { get; } = default!;
    public override RuntimeSemantics RuntimeSemantics { get; } = default!;
}
