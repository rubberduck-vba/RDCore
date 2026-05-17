using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Expressions.Abstract;

namespace RDCore.Runtime.Model;

internal abstract record class ValuedExpression : BoundExpression
{
    protected internal ValuedExpression(Location location)
        : base(location)
    {
    }

}