using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Abstract;

/// <summary>
/// Represents a symbol that can be resolved to a <c>VBType</c>.
/// </summary>
internal abstract record class TypedSymbol : Symbol
{
    protected TypedSymbol(Uri workspaceRoot, string name, SymbolKind kind, Accessibility accessibility, Uri? parentUri = default, Range? range = default, Range? selectionRange = default)
        : base(workspaceRoot, name, kind, range, selectionRange, parentUri)
    {
        Accessibility = accessibility;
        ResolvedType = UnresolvedType.VBType;
    }

    public Accessibility Accessibility { get; init; }

    public VBType ResolvedType { get; init; }

    public TypedSymbol WithAccessibility(Accessibility accessibility) => this with { Accessibility = accessibility };
    public TypedSymbol WithResolvedType(VBType vbType) => this with { ResolvedType = vbType };
}
