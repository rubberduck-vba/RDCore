using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents a <em>parenthesized expression</em>, implemented as a unary operator to enforce let-coercion semantics.
/// </summary>
/// <remarks>
/// Implements <strong>MS-VBAL 5.6.6</strong> Parenthesized Expressions.
/// </remarks>
public sealed record class UnaryLetCoercionOperatorSymbol : UnaryOperatorSymbol
{
    public UnaryLetCoercionOperatorSymbol(string token, StaticSemantics staticSemantics, RuntimeSemantics executionSemantics)
        : base(token, staticSemantics, executionSemantics) { }
}