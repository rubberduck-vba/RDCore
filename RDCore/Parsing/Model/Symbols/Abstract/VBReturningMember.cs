using RDCore.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols.Abstract;

internal abstract record class VBReturningMember : VBTypeMemberSymbol
{
    protected VBReturningMember(Uri workspaceRoot, string name, Accessibility accessibility, SymbolKindExt kind, Uri parentUri)
        : base(workspaceRoot, name, kind, accessibility, parentUri)
    {
    }
    protected VBReturningMember(Uri workspaceRoot, string name, Accessibility accessibility, SymbolKindExt kind, Uri parentUri, Range range, Range selectionRange)
        : base(workspaceRoot, name, kind, accessibility, parentUri, range, selectionRange)
    {
    }
}
