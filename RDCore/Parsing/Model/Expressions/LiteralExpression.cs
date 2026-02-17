using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;

namespace RDCore.Parsing.Model.Expressions;

internal sealed record class LiteralExpression<TValue> : ValuedExpression
{
    public LiteralExpression(Location location, char? typeHint = default)
        : base(location)
    {
        TypeHint = typeHint;
    }

    public char? TypeHint { get; init; }
}
