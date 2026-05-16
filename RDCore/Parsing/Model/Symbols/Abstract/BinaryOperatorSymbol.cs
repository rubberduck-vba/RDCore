using RDCore.Semantics.Runtime.Abstract;
using RDCore.Semantics.Static.Abstract;

namespace RDCore.Parsing.Model.Symbols.Abstract;

internal abstract record class BinaryOperatorSymbol : OperatorSymbol
{
    protected BinaryOperatorSymbol(string name, StaticSemantics staticSemantics, RuntimeSemantics executionSemantics)
        : base(name, staticSemantics, executionSemantics)
    {
    }
}

