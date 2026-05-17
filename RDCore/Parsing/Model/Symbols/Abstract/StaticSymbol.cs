using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.Workspace.Services;

namespace RDCore.Parsing.Model.Symbols.Abstract;

internal record class StaticSymbol : TypedSymbol
{
    public StaticSymbol(string name, SymbolKindExt kind, VBType? typeInfo = default)
        : base(UriExtensions.GlobalUri, ScopeKind.Global, name, kind, Accessibility.Undefined, UriExtensions.GlobalUri, default!, default!)
    {
        ResolvedType = typeInfo ?? UnresolvedVBType.TypeInfo;
    }
}
