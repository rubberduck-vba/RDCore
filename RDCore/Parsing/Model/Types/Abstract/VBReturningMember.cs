using RDCore.Parsing.Model.Types.Complex;
using RDCore.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Types.Abstract;

internal abstract record class VBReturningMember : VBTypeMember
{
    protected VBReturningMember(Uri workspaceRoot, string name, Accessibility accessibility, SymbolKindExt kind, Uri parentUri, Range? range = default, Range? selectionRange = default, bool isHidden = false)
        : base(workspaceRoot, name, kind, accessibility, parentUri, range, selectionRange, isHidden)
    {
    }
}
