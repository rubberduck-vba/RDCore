using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Model.Symbols.Abstract;

public abstract record class BinaryOperatorSymbol : OperatorSymbol
{
    protected BinaryOperatorSymbol(string name, StaticSemantics staticSemantics, RuntimeSemantics runtimeSemantics)
        : base(name, staticSemantics, runtimeSemantics) { }
}

