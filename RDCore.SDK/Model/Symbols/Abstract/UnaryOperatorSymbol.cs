using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Semantics.Runtime.Operators.Context;

namespace RDCore.SDK.Model.Symbols.Abstract
{
    /// <summary>
    /// Represents any unary operator symbol.
    /// </summary>
    public abstract record class UnaryOperatorSymbol<TContext, TFlags>(string Token) 
        : OperatorSymbol<TContext, TFlags>(Token) 
        where TContext : SemanticContext<TFlags>, new()
        where TFlags : struct, Enum
    { }

    /// <summary>
    /// Represents any unary arithmetic operator symbol.
    /// </summary>
    public abstract record class UnaryArithmeticOperatorSymbol(string Token) 
        : UnaryOperatorSymbol<UnaryArithmeticOperatorSemanticContext, ArithmeticOperatorSemanticFlags>(Token) 
    { }

    /// <summary>
    /// Represents any unary logical operator symbol.
    /// </summary>
    public abstract record class UnaryLogicalOperatorSymbol(string Token) 
        : UnaryOperatorSymbol<UnaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags>(Token) 
    { }
}