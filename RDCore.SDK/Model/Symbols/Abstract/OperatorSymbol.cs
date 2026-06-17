using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;

namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Represents any operator static symbol.
/// </summary>
public abstract record class OperatorSymbol<TContext, TFlags>(string Token)
    : StaticSymbol(Token, SymbolKindExt.Operator, VBUnknownType.TypeInfo) 
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
{ }
