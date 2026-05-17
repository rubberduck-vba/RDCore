using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols.Abstract;

/// <summary>
/// Represents a symbol that can be resolved to a <c>VBType</c>.
/// </summary>
internal abstract record class TypedSymbol : Symbol
{
    protected TypedSymbol(Uri workspaceRoot, string name, SymbolKindExt kind, Accessibility accessibility, Uri parentUri, ScopeKind? scope = ScopeKind.Global)
        : base(workspaceRoot, name, kind, parentUri, scope)
    {
        Accessibility = accessibility;
        ResolvedType = UnresolvedVBType.TypeInfo;
    }
    protected TypedSymbol(Uri workspaceRoot, ScopeKind scope, string name, SymbolKindExt kind, Accessibility accessibility, Uri parentUri, Range range, Range selectionRange)
        : base(workspaceRoot, scope, name, kind, range, selectionRange, parentUri)
    {
        Accessibility = accessibility;
        ResolvedType = UnresolvedVBType.TypeInfo;
    }

    public Accessibility Accessibility { get; init; }
    public VBType ResolvedType { get; init; }

    public TypedSymbol WithAccessibility(Accessibility accessibility) => this with { Accessibility = accessibility };
    public TypedSymbol WithResolvedType(VBType type) => this with { ResolvedType = type };
}
