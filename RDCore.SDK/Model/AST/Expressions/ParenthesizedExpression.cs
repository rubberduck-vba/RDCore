using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RDCore.SDK.Model.AST.Expressions;

public sealed record class ParenthesizedExpression : ValuedExpression
{
    public ParenthesizedExpression(Location location, ValuedExpression innerExpression)
        : base(location)
    {
        InnerExpression = innerExpression;
    }

    public ValuedExpression InnerExpression { get; init; }
}
