using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Types.Complex;

internal abstract record class VBTypeMember : TypedSymbol
{
    protected VBTypeMember(Uri uri, string name, SymbolKind kind, Accessibility accessibility, Uri? parentUri = default, Range? range = default, Range? selectionRange = default, bool isHidden = false)
        : base(uri, name, kind, accessibility, parentUri, range, selectionRange)
    {
        Uri = uri;
        Name = name;
        Kind = kind;
        Accessibility = accessibility;
        IsHidden = isHidden;

        DocString = string.Empty;
    }

    public bool IsHidden { get; init; }

    public string DocString { get; init; }
    public int UserMemId { get; init; }
    public int MemberFlags { get; init; }
}
