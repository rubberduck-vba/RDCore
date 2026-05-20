using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents any static operator symbol that accepts two operands.
/// </summary>
/// <remarks>
/// The abstractions do not presume of the position of the operands relative to the operator.
/// </remarks>
public abstract record class BinaryOperatorSymbol : OperatorSymbol
{
    /// <summary>
    /// Creates a new static operator symbol representing a binary operator.
    /// </summary>
    /// <param name="name">The name of the static symbol.</param>
    /// <param name="staticSemantics">The static semantics specified for this operator symbol.</param>
    /// <param name="runtimeSemantics">The runtime semantics specified for this operator symbol.</param>
    protected BinaryOperatorSymbol(string name, StaticSemantics staticSemantics, RuntimeSemantics runtimeSemantics)
        : base(name, staticSemantics, runtimeSemantics) { }
}

