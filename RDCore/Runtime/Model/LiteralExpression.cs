using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Values;

namespace RDCore.Runtime.Model;

internal sealed record class LiteralExpression : ValuedExpression
{
    public LiteralExpression(Location location, VBTypedValue value, char? typeHint = default)
        : base(location, value)
    {
        TypeHint = typeHint;
    }

    public char? TypeHint { get; init; }

}
