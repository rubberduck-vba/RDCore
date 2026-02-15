using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using RDCore.Parsing.Model.Types.Complex;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Types.Abstract;

internal abstract record class VBReturningMember : VBTypeMember
{
    protected VBReturningMember(Uri workspaceRoot, string name, Accessibility accessibility, SymbolKind kind, Uri parentUri, Range? range = default, Range? selectionRange = default, bool isHidden = false)
        : base(workspaceRoot, name, kind, accessibility, parentUri, range, selectionRange, isHidden)
    {
    }
}
