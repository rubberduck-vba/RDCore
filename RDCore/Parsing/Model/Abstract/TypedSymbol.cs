using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Abstract;

namespace RDCore.Parsing.Model.Abstract;

/// <summary>
/// Represents a symbol that can be resolved to a <c>VBType</c>.
/// </summary>
internal abstract record class TypedSymbol : Symbol
{
    protected TypedSymbol(Uri workspaceRoot, string name, Accessibility accessibility, SymbolKind kind, Uri? parentUri = null)
        : base(workspaceRoot, name, kind, parentUri)
    {
        Accessibility = accessibility;
        ResolvedType = UnresolvedType.VBType;
    }

    public Accessibility Accessibility { get; init; }

    public VBType ResolvedType { get; init; }

    public TypedSymbol WithAccessibility(Accessibility accessibility) => this with { Accessibility = accessibility };
    public TypedSymbol WithResolvedType(VBType vbType) => this with { ResolvedType = vbType };
}
