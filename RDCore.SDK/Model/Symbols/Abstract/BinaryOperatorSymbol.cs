using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents any static operator symbol that accepts two operands.
/// </summary>
/// <remarks>
/// The abstractions do not presume of the position of the operands relative to the operator.
/// </remarks>
/// <param name="name">The name of the static symbol.</param>
/// <param name="staticSemantics">The static semantics specified for this operator symbol.</param>
/// <param name="runtimeSemantics">The runtime semantics specified for this operator symbol.</param>
public abstract record class BinaryOperatorSymbol(string Token, IStaticSemantics StaticSemantics, IRuntimeSemantics RuntimeSemantics) 
    : OperatorSymbol(Token, StaticSemantics, RuntimeSemantics) { }

