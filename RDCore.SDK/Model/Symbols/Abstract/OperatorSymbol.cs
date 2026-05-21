using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents any operator static symbol.
/// </summary>
public abstract record class OperatorSymbol(string Token, IStaticSemantics StaticSemantics, IRuntimeSemantics ExecutionSemantics) 
    : StaticSymbol(Token, SymbolKindExt.Operator, VBUnknownType.TypeInfo)
{ }
