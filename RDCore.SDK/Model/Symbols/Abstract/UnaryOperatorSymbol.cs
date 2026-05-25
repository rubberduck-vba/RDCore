using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents any unary operator static symbol.
/// </summary>
public abstract record class UnaryOperatorSymbol(string Token, IStaticSemantics StaticSemantics, IRuntimeSemantics RuntimeSemantics) 
    : OperatorSymbol(Token, StaticSemantics, RuntimeSemantics) { }
