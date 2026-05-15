using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using RDCore.Server.ProtocolExtensions;
using RDCore.Workspace.Services;

namespace RDCore.Parsing.Model.Expressions.Operators;

internal record class StaticSymbol : TypedSymbol
{
    public StaticSymbol(string name, SymbolKindExt kind, VBType? typeInfo = default)
        : base(UriExtensions.GlobalUri, ScopeKind.Global, name, kind, Accessibility.Undefined, UriExtensions.GlobalUri, default!, default!)
    {
        ResolvedType = typeInfo ?? UnresolvedType.TypeInfo;
    }
}
