using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Semantics.Runtime.Abstract;
using RDCore.Semantics.Static.Abstract;
using RDCore.Semantics.Static.Expressions;

namespace RDCore.Runtime.Model;

internal sealed record class LiteralExpression : ValuedExpression
{
    public LiteralExpression(Location location, VBTypedValue value, char? typeHint = default)
        : base(location)
    {
        TypeHint = typeHint;
    }

    public char? TypeHint { get; init; }
    public override StaticSemantics StaticSemantics { get; } = new LiteralExpressionStaticSemantics();
    public override RuntimeSemantics RuntimeSemantics { get; } = default!; //= new LiteralExpressionRuntimeSemantics();
}
