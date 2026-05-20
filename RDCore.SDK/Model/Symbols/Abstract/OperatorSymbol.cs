using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents any operator static symbol.
/// </summary>
public abstract record class OperatorSymbol : StaticSymbol
{
    protected OperatorSymbol(string token, StaticSemantics staticSemantics, RuntimeSemantics executionSemantics)
        : base(token, SymbolKindExt.Operator, VBUnknownType.TypeInfo)
    {
        StaticSemantics = staticSemantics;
        RuntimeSemantics = executionSemantics;
    }

    public StaticSemantics StaticSemantics { get; init; }
    public RuntimeSemantics RuntimeSemantics { get; init; }
}
