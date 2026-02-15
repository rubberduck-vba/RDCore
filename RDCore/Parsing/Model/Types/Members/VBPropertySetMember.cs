using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Types.Members;

internal record class VBPropertySetMember : VBProcedureMember, IVBProperty
{
    public VBPropertySetMember(Uri workspaceUri, string name, SymbolKind kind, Accessibility accessibility, Uri parentUri, Range? range = default, Range? selectionRange = default, bool isHidden = false)
        : base(workspaceUri, name, kind, accessibility, parentUri, range, selectionRange, isHidden)
    {
    }
}
