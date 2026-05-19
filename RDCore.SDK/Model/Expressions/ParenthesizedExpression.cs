using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Expressions;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.Runtime.Model;

public sealed record class ParenthesizedExpression : ValuedExpression
{
    public ParenthesizedExpression(Location location, ValuedExpression innerExpression)
        : base(location)
    {
        InnerExpression = innerExpression;
    }

    public ValuedExpression InnerExpression { get; init; }

    public override StaticSemantics StaticSemantics { get; } = default!;
    public override RuntimeSemantics RuntimeSemantics { get; } = default!;
}
