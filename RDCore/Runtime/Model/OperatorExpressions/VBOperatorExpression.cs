using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Symbols.Abstract;

namespace RDCore.Runtime.Model.Operators;

internal abstract record class VBOperatorExpression : ValuedExpression
{
    protected VBOperatorExpression(OperatorSymbol symbol, Location location) : base(location)
    {
        Symbol = symbol;
    }

    public OperatorSymbol Symbol { get; init; }
}
