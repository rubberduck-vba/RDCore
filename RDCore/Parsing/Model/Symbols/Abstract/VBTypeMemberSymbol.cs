using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols.Abstract;

internal abstract record class VBTypeMemberSymbol : TypedSymbol
{
    protected VBTypeMemberSymbol(Uri uri, string name, SymbolKindExt kind, Accessibility accessibility, Uri parentUri)
        : base(uri, name, kind, accessibility, parentUri)
    {
        Uri = uri;
        Accessibility = accessibility;
    }

    protected VBTypeMemberSymbol(Uri uri, string name, SymbolKindExt kind, Accessibility accessibility, Uri parentUri, Range range, Range selectionRange)
        : base(uri, ScopeKind.Unallocated, name, kind, accessibility, parentUri, range, selectionRange)
    {
        Uri = uri;
        Accessibility = accessibility;
    }
}
