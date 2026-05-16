using RDCore.Semantics.Runtime.Abstract;
using RDCore.Semantics.Static.Abstract;
using RDCore.Server.ProtocolExtensions;

namespace RDCore.Parsing.Model.Symbols.Abstract;

internal abstract record class OperatorSymbol : StaticSymbol
{
    protected OperatorSymbol(string token, StaticSemantics staticSemantics, RuntimeSemantics executionSemantics)
        : base(token, SymbolKindExt.Operator)
    {
        StaticSemantics = staticSemantics;
        RuntimeSemantics = executionSemantics;
    }

    public StaticSemantics StaticSemantics { get; init; }
    public RuntimeSemantics RuntimeSemantics { get; init; }
}

