using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Values;

namespace RDCore.Runtime.Model;

internal sealed record class LiteralExpression<TValue> : ValuedExpression
{
    public LiteralExpression(Location location, char? typeHint = default)
        : base(location)
    {
        TypeHint = typeHint;
    }

    public char? TypeHint { get; init; }

    public override VBTypedValue Evaluate(VBExecutionContext context) => ResultValue;
}
