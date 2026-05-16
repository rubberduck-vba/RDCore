using RDCore.Semantics.Runtime.Abstract;
using RDCore.Semantics.Static.Abstract;

namespace RDCore.Parsing.Model.Symbols.Abstract;

internal abstract record class UnaryOperatorSymbol : OperatorSymbol
{
    protected UnaryOperatorSymbol(string token, StaticSemantics staticSemantics, RuntimeSemantics executionSemantics)
        : base(token, staticSemantics, executionSemantics)
    {
    }
}

