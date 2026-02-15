using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Abstract;

namespace RDCore.Parsing.Model.Types.Members;

internal record class VBPropertySetMember : VBProcedureMember, IVBProperty
{
    public VBPropertySetMember(Uri workspaceUri, string name, SymbolKind kind, Accessibility accessibility, Uri parentUri, bool isUserDefined = false, bool isHidden = false)
        : base(workspaceUri, name, kind, accessibility, parentUri, isUserDefined, isHidden)
    {
    }
}
