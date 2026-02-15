using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;

namespace RDCore.Parsing.Model.Types.Complex;

internal abstract record class VBTypeMember : TypedSymbol
{
    protected VBTypeMember(Uri uri, string name, SymbolKind kind, Accessibility accessibility, Uri? parentUri = default)
        : this(uri, name, kind, accessibility, parentUri, isUserDefined: true)
    {
    }

    protected VBTypeMember(Uri uri, string name, SymbolKind kind, Accessibility accessibility, Uri? parentUri = default, bool isUserDefined = false, bool isHidden = false)
        : base(uri, name, accessibility, kind, parentUri)
    {
        Uri = uri;
        IsUserDefined = isUserDefined;
        Name = name;
        Kind = kind;
        Accessibility = accessibility;
        IsHidden = isHidden;

        DocString = string.Empty;
    }

    public bool IsUserDefined { get; init; }
    public bool IsHidden { get; init; }

    public string DocString { get; init; }
    public int UserMemId { get; init; }
    public int MemberFlags { get; init; }
}
