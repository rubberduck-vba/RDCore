using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Semantics.Runtime.Operators.Context;

namespace RDCore.SDK.Model.Symbols.Abstract
{
    /// <summary>
    /// Represents any static operator symbol that accepts two operands.
    /// </summary>
    /// <remarks>
    /// The abstractions do not presume of the position of the operands relative to the operator.
    /// </remarks>
    /// <param name="name">The name of the static symbol.</param>
    public abstract record class BinaryOperatorSymbol<TFlags>(string Token) 
        : OperatorSymbol<BinaryOperatorSemanticContext<TFlags>, TFlags>(Token) where TFlags : struct, Enum 
    {
    }

    /// <summary>
    /// Represents any binary arithmetic operator symbol.
    /// </summary>
    /// <param name="name">The name of the static symbol.</param>
    public abstract record class BinaryArithmeticOperatorSymbol(string Token)
        : BinaryOperatorSymbol<ArithmeticOperatorSemanticFlags>(Token)
    {
    }

    /// <summary>
    /// Represents any binary arithmetic operator symbol.
    /// </summary>
    /// <param name="name">The name of the static symbol.</param>
    public abstract record class BinaryArithmeticOperatorSymbol<TFlags>(string Token)
        : BinaryOperatorSymbol<TFlags>(Token) 
    where TFlags : struct, Enum
    {
    }
}