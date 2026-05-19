using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Model.Expressions.Operators;

/// <summary>
/// Represents any operator expression; a valued expression that is associated to an <c>OperatorSymbol</c>.
/// </summary>
public abstract record class VBOperatorExpression : ValuedExpression
{
    protected VBOperatorExpression(OperatorSymbol symbol, Location location) : base(location)
    {
        Symbol = symbol;
    }

    /// <summary>
    /// The operator symbol.
    /// </summary>
    /// <remarks>
    /// This should be a <c>GlobalSymbols</c> static instance.
    /// </remarks>
    public OperatorSymbol Symbol { get; init; }
}
