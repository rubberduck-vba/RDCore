using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Semantics.Runtime.Abstract;
using RDCore.Semantics.Static.Abstract;

namespace RDCore.Runtime.Model;

internal sealed record class LiteralExpression : ValuedExpression
{
    public LiteralExpression(Location location, VBTypedValue value, char? typeHint = default)
        : base(location, value)
    {
        TypeHint = typeHint;
    }

    public char? TypeHint { get; init; }
    public override StaticSemantics StaticSemantics { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
    public override RuntimeSemantics RuntimeSemantics { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
}
