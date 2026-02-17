using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Abstract;

/// <summary>
/// Represents a symbol that can be resolved to a <c>VBType</c>.
/// </summary>
internal abstract record class TypedSymbol : Symbol
{
    protected TypedSymbol(Uri workspaceRoot, string name, SymbolKindExt kind, Accessibility accessibility, Uri? parentUri = default, Range? range = default, Range? selectionRange = default)
        : base(workspaceRoot, name, kind, range, selectionRange, parentUri)
    {
        Accessibility = accessibility;
        ResolvedType = UnresolvedType.VBType;
    }

    public Accessibility Accessibility { get; init; }
    public VBType ResolvedType { get; init; }

    public TypedSymbol WithAccessibility(Accessibility accessibility) => this with { Accessibility = accessibility };
    public TypedSymbol WithResolvedType(VBType type) => this with { ResolvedType = type };
}
